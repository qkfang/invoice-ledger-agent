using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace FxAgent.Agents;

public class InvLdgAgIngestion : BaseAgent
{
    public InvLdgAgIngestion(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-ingestion", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an ingestion agent. You receive incoming vendor invoice documents (PDF or image) dropped into the inbox.
        Your job is to:
          1. Confirm the document is a vendor invoice (not a statement, reminder, or marketing material).
          2. Extract the basic envelope: invoiceId, vendorName, invoiceDate, currency, totalAmount.
          3. Tag the document with an ingestionStatus of "accepted" or "rejected" and a short reason.
          4. Return a single JSON object. No text outside the JSON.
        """;
}
