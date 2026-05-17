# Agents

This repository implements an automated **invoice‑to‑general‑ledger** workflow using
[Azure AI Foundry](https://learn.microsoft.com/azure/ai-foundry/) agents. Each agent is a
declarative Foundry agent created at startup (see `src/invledger_app/Agents/BaseAgent.cs`)
that runs against an Azure OpenAI deployment and reaches the application's domain logic
through a single MCP (Model Context Protocol) server hosted by the app itself
(`src/invledger_app/Mcp/InvLedgerMcpTools.cs`).

The agents are orchestrated by the ASP.NET host (`Program.cs` + `Api/Api.cs`) and exchange
JSON. Together they cover the full pipeline:

```
inbox ─▶ Ingestion ─▶ Extract (DI / CU) ─▶ Invoice ─▶ Processing ─▶ ┬─▶ Ledger
                                                                    └─▶ Exception ─▶ Notification / Correspondence
```

## Design principles

- **One responsibility per agent.** Each agent has a focused instruction set and returns
  a strict JSON envelope (except the conversational correspondence agent).
- **Tools over prompts.** All side effects (document extraction, blob access, ledger
  reads/writes, email send) are exposed as MCP tools so agents stay declarative.
- **Human‑in‑the‑loop where it matters.** Outbound communication agents use an
  MCP approval policy (`AlwaysRequireApproval`) so a reviewer must confirm before a
  message is sent; internal pipeline agents auto‑approve to keep throughput high.
- **Deterministic contracts.** Downstream agents consume the previous agent's JSON
  output, which makes the pipeline easy to replay, test, and audit.

## Shared infrastructure

| Component | Source | Purpose |
|---|---|---|
| `BaseAgent` | `Agents/BaseAgent.cs` | Creates the Foundry agent version, owns the Responses client, drives the run loop, and handles MCP tool‑approval items. |
| MCP server | `Mcp/InvLedgerMcpTools.cs` | Exposes `extractDoc_DI`, `extractDoc_CU`, `notification`, and ledger query tools to every agent. |
| Services | `Services/*.cs` | Azure Document Intelligence, Azure Content Understanding, blob storage, notification, pending‑approval store, and the in‑memory general ledger. |

## Agents

### `inv-ldg-ag-ingestion` — Ingestion
- **File:** `Agents/InvLdgAgIngestion.cs`
- **Role:** First touch on a document dropped into the inbox. Confirms the file is a
  vendor invoice (not a statement, reminder, or marketing material) and extracts the
  envelope: `invoiceId`, `vendorName`, `invoiceDate`, `currency`, `totalAmount`.
- **Output:** JSON with `ingestionStatus` of `accepted` or `rejected` plus a reason.

### `inv-ldg-ag-extract-di` — Document Intelligence Extraction
- **File:** `Agents/InvLdgAgExtractDI.cs`
- **Tools:** `extractDoc_DI` (Azure AI Document Intelligence `prebuilt-layout`).
- **Role:** Given a blob URL, calls the DI tool, then extracts a fixed field set
  (entity, jurisdiction, dates, amount due, tax type), produces a renamed
  "transformed" view, and classifies the document into a form category.
- **Output:** `{ extracted, transformed, category }` JSON.

### `inv-ldg-ag-extract-cu` — Content Understanding Extraction
- **File:** `Agents/InvLdgAgExtractCU.cs`
- **Role:** Operates on raw text (typically fed by the Content Understanding service)
  and uses contextual reasoning to fill the same fields as the DI agent when layout
  parsing is ambiguous. Used as a fallback / second opinion.
- **Output:** Strict JSON with the same field schema.

### `inv-ldg-ag-invoice` — Invoice Extraction
- **File:** `Agents/InvLdgAgInvoice.cs`
- **Role:** Takes the ingested document and produces the structured invoice:
  header, `categories[]` with totals, and `lineItems[]` with quantity, unit price,
  and line total. Enforces that line items roll up to category totals and category
  totals roll up to the invoice total.
- **Output:** Single invoice JSON object.

### `inv-ldg-ag-processing` — Ledger Matching
- **File:** `Agents/InvLdgAgProcessing.cs`
- **Role:** Consolidates the extracted invoice into the general ledger. For every
  line item it applies, in order:
  1. **Category match** against approved ledger categories (with rule aliases).
  2. **Sub‑item match** against approved ledger items under that category.
  3. **Dollar match** against per‑rule absolute or percentage tolerance.
- **Output:** A summary (`matched` / `review` / `exception` counts) and a per‑line
  result with `status`, `matchedLedgerCategory`, `matchedLedgerItem`, `ruleApplied`,
  `reason`, and a `humanInLoop` flag.

### `inv-ldg-ag-exception` — Exception Routing
- **File:** `Agents/InvLdgAgException.cs`
- **Tools:** `notification`.
- **Role:** Receives line items the processing agent could not auto‑match. Picks the
  responsible team (procurement, finance, vendor‑management), drafts a short email
  describing the unmatched item and suspected cause, and dispatches it through the
  notification tool.
- **Output:** JSON list of dispatched items with `assignedTeam`, recipient, subject,
  body, and the tool's send result.

### `inv-ldg-ag-ledger` — Ledger Q&A
- **File:** `Agents/InvLdgAgLedger.cs`
- **Role:** Read‑only assistant over the approved general ledger. Answers questions
  about categories, sub‑items, balances, and posted invoice history using the ledger
  snapshot provided in the user message as the source of truth.
- **Output:** Concise plain text (markdown tables allowed).

### `inv-ldg-ag-notification` — Notification Routing
- **File:** `Agents/InvLdgAgNotification.cs`
- **Tools:** `notification` (auto‑approved).
- **Role:** Applies a small rule set (`tax-due`, `audit-change`, `cp14`, generic
  `tax-notice`) to a notice payload, drafts a short email, and sends it via the
  notification tool.
- **Output:** JSON with `ruleMatched`, `assignedTeam`, `recipientEmail`, subject,
  body, and the send result.

### `inv-ldg-ag-correspondence` — Human‑in‑the‑Loop Correspondence
- **File:** `Agents/InvLdgAgCorrespondence.cs`
- **Tools:** `notification` (requires approval).
- **Role:** Drafts and iteratively refines a reply with a human reviewer through
  chat. Replies use a fixed `Subject: / To: / --- body ---` envelope so the web UI
  can parse the draft. Calls the `notification` tool only when the user explicitly
  says to send; the Foundry MCP approval policy ensures a final human confirmation
  before the email leaves the system.
- **Output:** Conversational text containing the structured draft block.

## Run loop and approvals

`BaseAgent` provides three modes used by the API layer:

- `RunAsync` — fire‑and‑forget run that auto‑approves any MCP approval requests. Used
  by internal pipeline agents.
- `StartRunAsync` / `ChatAsync` — turn‑based interaction that returns a
  `PendingToolApproval` when the agent asks to invoke a guarded tool. The API stores
  it in `PendingApprovalStore` and surfaces it to the UI.
- `ContinueRunAsync` — resumes the run after a human approves or rejects the pending
  tool call.

This pattern keeps the agents themselves stateless and declarative while letting the
hosting application enforce policy around side effects.

## Adding a new agent

1. Create a class under `src/invledger_app/Agents/` that inherits from `BaseAgent`,
   passes a stable agent id (kebab‑case, prefixed `inv-ldg-ag-…`), and returns its
   system prompt from `GetInstructions()`.
2. If the agent needs side effects, expose them as MCP tools in
   `InvLedgerMcpTools.cs` and pass the corresponding `ResponseTool` instances in.
   Choose `NeverRequireApproval` for safe internal tools and
   `AlwaysRequireApproval` for anything that talks to the outside world.
3. Register the agent in `Program.cs` and wire any new endpoints in `Api/Api.cs`.
4. Keep the instruction set focused on a single responsibility and require strict
   JSON output so downstream agents can consume it deterministically.
