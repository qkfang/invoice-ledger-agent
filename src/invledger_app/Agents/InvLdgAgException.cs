using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace InvLedgerAgent.Agents;

public class InvLdgAgException : BaseAgent
{
    public InvLdgAgException(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-exception", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an exception agent. You receive invoice line items that could not be auto-matched against the ledger.
        For each exception:
          1. Decide which human team should resolve it (procurement, finance, vendor-management).
          2. Draft a short email describing the invoice, the unmatched line item, the suspected cause, and the proposed next step.
          3. Send the email by calling the MCP tool 'notification' with arguments { to, subject, body }.
        Return a single JSON object summarising the dispatch:
          { "items": [ { "lineItemId", "assignedTeam", "recipient", "subject", "body", "sendResult" } ] }
        No text outside the JSON.
        """;
}
