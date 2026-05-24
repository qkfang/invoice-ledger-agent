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
inbox ─▶ Ingestion ─▶ Invoice ─▶ Processing ─▶ ┬─▶ Ledger (Q&A)
                                                └─▶ Exception ─▶ Notification (email)
```

## Design principles

- **One responsibility per agent.** Each agent has a focused instruction set and returns
  a strict JSON envelope (except the conversational ledger agent).
- **Tools over prompts.** All side effects (document extraction, FX conversion, email
  send, ledger and rules lookup) are exposed as MCP tools so agents stay declarative.
- **Single MCP server, single approval policy.** All agents share one MCP tool
  (`invledger-mcp`) registered with `NeverRequireApproval`, so the host auto‑approves
  every tool call. `BaseAgent` still supports a turn‑based approval flow for future
  human‑in‑the‑loop scenarios.
- **Deterministic contracts.** Downstream agents consume the previous agent's JSON
  output, which makes the pipeline easy to replay, test, and audit.

## Shared infrastructure

| Component | Source | Purpose |
|---|---|---|
| `BaseAgent` | `Agents/BaseAgent.cs` | Creates the Foundry agent version, owns the Responses client, drives the run loop, and handles MCP tool‑approval items. |
| MCP server | `Mcp/InvLedgerMcpTools.cs` | Exposes document extraction, notification, FX conversion, and ledger/rules tools to every agent. |
| Services | `Services/*.cs` | Azure Document Intelligence, Azure Content Understanding, blob storage, notification, in‑memory general ledger, FX rates, local run storage, and optional Fabric lakehouse. |

### MCP tools

The following tools are registered on the `invledger-mcp` server:

| Tool | Description |
|---|---|
| `extractDoc_DI` | Run Azure AI Document Intelligence (`prebuilt-layout`) on a document URL and return markdown. |
| `extractDoc_CU` | Run Azure AI Content Understanding on a document URL and return markdown. |
| `notification` | Send a notification email (`to`, `subject`, `body`). |
| `fx_convert` | Convert an amount between currencies for a given invoice date. |
| `ledger_list` / `ledger_get` / `ledger_add` / `ledger_update` / `ledger_delete` | CRUD over the in‑memory general ledger. |
| `get_approved_ledger` | Return the approved ledger snapshot (`wwwroot/data/ledger.json`). |
| `get_processing_rules` | Return the matching rules (`wwwroot/data/rules.json`). |

## Agents

### `inv-ldg-ag-ingestion` — Ingestion
- **File:** `Agents/InvLdgAgIngestion.cs`
- **Tools:** `extractDoc_DI`.
- **Role:** First touch on documents dropped into the inbox. Confirms the file is a
  vendor invoice (not a statement, reminder, or marketing material), calls
  `extractDoc_DI` on each blob URL, and extracts the envelope: `invoiceId`,
  `vendorName`, `invoiceDate`, `currency`, `totalAmount`.
- **Output:** JSON with `ingestionStatus` of `accepted` or `rejected`, a `reason`,
  and an `invoices[]` array of envelope fields per file.

### `inv-ldg-ag-invoice` — Invoice Extraction
- **File:** `Agents/InvLdgAgInvoice.cs`
- **Tools:** `extractDoc_CU`, `fx_convert`.
- **Role:** Takes a vendor invoice (URL or content) and produces the structured
  invoice: header, `categories[]` with totals, and `lineItems[]` with quantity,
  unit price, and line total. Enforces that line items roll up to category totals
  and category totals roll up to the invoice total. Calls `fx_convert` to populate
  `audTotalAmount`, `audCategoryTotal`, and `audLineTotal` fields against AUD.
- **Output:** Single invoice JSON object.

### `inv-ldg-ag-processing` — Ledger Matching
- **File:** `Agents/InvLdgAgProcessing.cs`
- **Tools:** `fx_convert`.
- **Role:** Consumes a JSON payload containing extracted invoices, the approved
  ledger, and matching rules `R1..R6`. Calls `fx_convert` for the invoice total,
  then classifies every line item against the ledger:
  - `R6` (exception): invoice category is not in the ledger.
  - `R5` (exception): category exists but no ledger item matches the description.
  - `R4` (review): `lineTotal` exceeds the configured threshold.
  - `R1` (matched): exact unit‑price match.
  - `R2` (matched): unit price within configured percentage/absolute tolerance.
  - `R3` (review): unit price outside tolerance.
- **Output:** `{ "invoices": [ ... ] }` where each invoice contains `lineItems`,
  `exceptions`, and `postedEntries` (one per matched line) with ledger codes, FX
  rate, and converted invoice amount.

### `inv-ldg-ag-exception` — Exception Routing
- **File:** `Agents/InvLdgAgException.cs`
- **Tools:** `notification`.
- **Role:** Receives line items the processing agent could not auto‑match. Picks
  the responsible team (procurement, finance, vendor‑management), drafts a short
  email describing the invoice, the unmatched line item, the suspected cause, and
  the proposed next step, then dispatches it through the `notification` tool.
- **Output:** `{ "items": [ { "lineItemId", "assignedTeam", "recipient", "subject", "body", "sendResult" } ] }`.

### `inv-ldg-ag-ledger` — Ledger Q&A
- **File:** `Agents/InvLdgAgLedger.cs`
- **Tools:** none (no MCP server attached).
- **Role:** Read‑only assistant over the approved general ledger. Answers
  questions about categories, sub‑items, balances, and posted invoice history
  using the ledger snapshot provided in the user message as the source of truth.
- **Output:** Concise plain text (short markdown tables allowed).

## Run loop and approvals

`BaseAgent` provides three modes:

- `RunAsync` — fire‑and‑forget run that auto‑approves any MCP approval requests.
  Used by every agent in this app today.
- `StartRunAsync` / `ChatAsync` — turn‑based interaction that returns a
  `PendingToolApproval` when the agent asks to invoke a guarded tool.
- `ContinueRunAsync` — resumes the run after a human approves or rejects the
  pending tool call.

The hosting app registers the MCP tool with `NeverRequireApproval`, so all tool
calls are auto‑approved. The turn‑based methods remain available for future
human‑in‑the‑loop flows without changing agent code.

## Adding a new agent

1. Create a class under `src/invledger_app/Agents/` that inherits from `BaseAgent`,
   passes a stable agent id (kebab‑case, prefixed `inv-ldg-ag-…`), and returns its
   system prompt from a `GetInstructions()` method.
2. If the agent needs new side effects, expose them as MCP tools in
   `InvLedgerMcpTools.cs`. Choose the appropriate
   `GlobalMcpToolCallApprovalPolicy` when constructing the `ResponseTool` —
   `NeverRequireApproval` for safe internal tools, `AlwaysRequireApproval` for
   anything that must be confirmed by a human.
3. Construct the agent in `Program.cs`, pass the shared MCP tool (or `null` for
   tool‑less agents like the ledger Q&A), and wire any new endpoints in
   `Api/Api.cs`.
4. Keep the instruction set focused on a single responsibility and require strict
   JSON output so downstream agents can consume it deterministically.
