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

The workflow is fully automated by a team of Foundry agents that hand structured JSON
to each other. Each agent has a single responsibility and reaches the application's
domain logic through a single MCP server hosted by the app itself.

```
inbox ─▶ Ingestion ─▶ Extract (DI / CU) ─▶ Invoice ─▶ Processing ─▶ ┬─▶ Ledger
                                                                    └─▶ Exception ─▶ Notification / Correspondence
```

See [`agents.md`](./agents.md) for the full agent catalog, responsibilities, tools,
and orchestration rules.

## Architecture

- **Host:** ASP.NET Core web app (`src/invledger_app`) exposing the REST API
  (`Api/Api.cs`), a static web UI (`wwwroot`), an MCP server at `/mcp`, and a health
  endpoint at `/health`.
- **Agents:** Declarative Foundry agents under `src/invledger_app/Agents/`,
  instantiated at startup through `BaseAgent` and driven by the Azure AI Foundry
  Responses API.
- **MCP tools:** `src/invledger_app/Mcp/InvLedgerMcpTools.cs` exposes document
  extraction, notification, and ledger operations. Tools that produce outbound
  effects require human approval; internal tools auto‑approve.
- **Services:** `src/invledger_app/Services/` wraps Azure Document Intelligence,
  Azure Content Understanding, Azure Blob Storage, the notification channel, the
  in‑memory general ledger, and the pending‑approval store.
- **Infrastructure:** Bicep templates under `bicep/` provision the Foundry project,
  Document Intelligence, storage, and the web app.

## Project layout

```
src/invledger_app/
  Agents/      Foundry agents (one file per agent)
  Api/         REST endpoints
  Mcp/         MCP tool surface exposed to the agents
  Services/    Azure service wrappers and in‑memory stores
  wwwroot/     Static web UI
  Program.cs   Host, DI, and agent wiring
bicep/         Azure infrastructure as code
agents.md      Agent catalog and orchestration details
```

## Configuration

The application reads its settings from `src/invledger_app/appsettings.json` or
environment variables. The required keys are:

| Key | Purpose |
|---|---|
| `AZURE_AI_PROJECT_ENDPOINT` | Foundry project endpoint used to create agent versions and run responses. |
| `AZURE_AI_FOUNDRY_ENDPOINT` | Foundry account endpoint (defaults to the authority of the project endpoint). |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | Chat/Responses model deployment used by the agents. |
| `AZURE_DOC_INTELLIGENCE_ENDPOINT` | Azure AI Document Intelligence endpoint. |
| `AZURE_STORAGE_ACCOUNT_NAME` | Storage account that holds uploaded documents. |
| `AZURE_CU_GPT41_DEPLOYMENT`, `AZURE_CU_GPT41_MINI_DEPLOYMENT`, `AZURE_CU_EMBEDDING_DEPLOYMENT` | Deployments used by the Content Understanding service. |
| `AZURE_TENANT_ID` | Tenant for `DefaultAzureCredential`. |
| `APP_MCP_URL` | Public base URL the Foundry agents use to reach this app's `/mcp` endpoint. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Optional — enables OpenTelemetry export to Azure Monitor. |

Authentication uses `DefaultAzureCredential`, so the host (developer machine or web
app identity) must have role assignments for Foundry, Document Intelligence, and the
storage account.

## Build and run

Prerequisites: .NET SDK matching `src/invledger_app/invledger.csproj`, and Azure
credentials available to `DefaultAzureCredential` (for example via `az login`).

```bash
cd src/invledger_app
dotnet restore
dotnet build
dotnet run
```

By default the app starts on the URLs configured in `Properties/launchSettings.json`,
serves the UI at `/`, exposes the REST API, and hosts the MCP server at `/mcp`.

## Deploy

Infrastructure templates live in `bicep/`. A typical deployment provisions the
Foundry project, Document Intelligence, storage, and the web app, then publishes
the application to App Service. See `bicep/deploy.ps1` for an example flow and
adjust the parameter file (`bicep/main.bicepparam`) for your environment.
