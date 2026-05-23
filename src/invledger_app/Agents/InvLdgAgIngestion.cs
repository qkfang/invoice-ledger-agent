using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace InvLedgerAgent.Agents;

public class InvLdgAgIngestion : BaseAgent
{
    public InvLdgAgIngestion(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-ingestion", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an ingestion agent. You receive incoming vendor invoice emails with attached PDF documents.
        Your job is to:
          1. Review the email to confirm it contains vendor invoices (not a statement, reminder, or marketing material).
          2. For each PDF blob URL provided, use the extractDoc_DI tool to extract the document content.
          3. From each extracted document, identify: invoiceId, vendorName, invoiceDate, currency, totalAmount.
          4. Return a single JSON object with no text outside it:
             {
               "ingestionStatus": "accepted" or "rejected",
               "reason": "brief explanation",
               "invoices": [
                 { "fileName": "...", "invoiceId": "...", "vendorName": "...", "invoiceDate": "...", "currency": "...", "totalAmount": 0 }
               ]
             }
        """;
}
