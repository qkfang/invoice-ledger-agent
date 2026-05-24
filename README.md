# invoice-ledger-agent

An automated **invoice‑to‑general‑ledger** pipeline built on
[Azure AI Foundry](https://learn.microsoft.com/azure/ai-foundry/) agents.

The system ingests vendor invoices, extracts and structures their content, matches
each line item against an approved general ledger for accounting, billing, and audit,
and routes anything that does not auto‑match to the right human team — end to end,
with no manual data entry.

## Purpose

Consolidate invoice line items into the **general ledger** so that:

- every dollar on every invoice is tied to an approved ledger category and sub‑item;
- billing can be reconciled against the ledger without spreadsheets;
- auditors get a deterministic, replayable trail of how each invoice was posted;
- exceptions (unknown vendors, unknown items, out‑of‑tolerance amounts) are escalated
  to procurement, finance, or vendor‑management automatically.

## How it works

The workflow is automated by a small team of Foundry agents that hand structured JSON
to each other. Each agent has a single responsibility and reaches the application's
domain logic through a single MCP server hosted by the app itself.

```
inbox ─▶ Ingestion ─▶ Invoice ─▶ Processing ─▶ ┬─▶ Ledger (Q&A)
                                                └─▶ Exception ─▶ Notification (email)
```

See [`agents.md`](./agents.md) for the full agent catalog, responsibilities, tools,
and orchestration rules.

## Architecture

- **Host:** ASP.NET Core web app (`src/invledger_app`, .NET 10) exposing the REST
  API (`Api/Api.cs`), a static web UI (`wwwroot`), an MCP server at `/mcp`, and a
  health endpoint at `/health`.
- **Agents:** Declarative Foundry agents under `src/invledger_app/Agents/`,
  instantiated at startup through `BaseAgent` and driven by the Azure AI Foundry
  Responses API.
- **MCP tools:** `src/invledger_app/Mcp/InvLedgerMcpTools.cs` exposes document
  extraction (`extractDoc_DI`, `extractDoc_CU`), notification (`notification`), FX
  conversion (`fx_convert`), ledger CRUD (`ledger_list`/`get`/`add`/`update`/`delete`),
  and read‑only access to the approved ledger and matching rules
  (`get_approved_ledger`, `get_processing_rules`). The tool is registered with
  `NeverRequireApproval`, so the host auto‑approves all tool calls.
- **Services:** `src/invledger_app/Services/` wraps Azure Document Intelligence,
  Azure Content Understanding, Azure Blob Storage, the notification channel, the
  in‑memory general ledger, FX rates, local run storage, and (optionally) a Fabric
  lakehouse.
- **Infrastructure:** Bicep templates under `bicep/` provision the Foundry project,
  Document Intelligence, storage, Fabric, and the web app.

## Project layout

```
src/invledger_app/
  Agents/      Foundry agents (one file per agent)
  Api/         REST endpoints
  Mcp/         MCP tool surface exposed to the agents
  Services/    Azure service wrappers and in‑memory stores
  wwwroot/     Static web UI, sample invoices, ledger, rules, and FX data
  Program.cs   Host, DI, and agent wiring
bicep/         Azure infrastructure as code
agents.md      Agent catalog and orchestration details
```

## Configuration

The application reads its settings from `src/invledger_app/appsettings.json` or
environment variables. The keys consumed by `Program.cs` are:

| Key | Required | Purpose |
|---|---|---|
| `AZURE_AI_PROJECT_ENDPOINT` | yes | Foundry project endpoint used to create agent versions and run responses. |
| `AZURE_AI_FOUNDRY_ENDPOINT` | no | Foundry account endpoint (defaults to the authority of the project endpoint). |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | yes | Chat/Responses model deployment used by the agents. |
| `AZURE_DOC_INTELLIGENCE_ENDPOINT` | yes | Azure AI Document Intelligence endpoint. |
| `AZURE_STORAGE_ACCOUNT_NAME` | yes | Storage account that holds uploaded documents. |
| `AZURE_CU_GPT41_DEPLOYMENT`, `AZURE_CU_GPT41_MINI_DEPLOYMENT`, `AZURE_CU_EMBEDDING_DEPLOYMENT` | no | Deployments used by the Content Understanding service. Defaults: `gpt-4.1`, `gpt-4.1-mini`, `text-embedding-3-large`. |
| `AZURE_TENANT_ID` | no | Tenant for `DefaultAzureCredential`. |
| `APP_MCP_URL` | no | Public base URL the Foundry agents use to reach this app's `/mcp` endpoint (defaults to `http://localhost:5001`). |
| `FABRIC_LAKEHOUSE_WORKSPACE_ID`, `FABRIC_LAKEHOUSE_ID` | no | Enable the optional Fabric lakehouse integration when both are set. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | no | Enables OpenTelemetry export to Azure Monitor. |

Authentication uses `DefaultAzureCredential` (with the Visual Studio Code
credential excluded), so the host (developer machine or web app managed identity)
must have role assignments for Foundry, Document Intelligence, the storage
account, and any optional services in use.

## Build and run

Prerequisites: the .NET 10 SDK and Azure credentials available to
`DefaultAzureCredential` (for example via `az login`).

```bash
cd src/invledger_app
dotnet restore
dotnet build
dotnet run
```

By default the app serves the UI at `/`, exposes the REST API, hosts the MCP
server at `/mcp`, and reports liveness at `/health`. In Development, Swagger UI is
available under `/swagger`.

## Deploy

Infrastructure templates live in `bicep/`. A typical deployment provisions the
Foundry project, Document Intelligence, storage, Fabric, and the web app, then
publishes the application to App Service. See `bicep/deploy.ps1` for an example
flow and adjust the parameter file (`bicep/main.bicepparam`) for your environment.
