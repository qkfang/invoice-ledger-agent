using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace FxAgent.Agents;

public class CtAgInvoice : BaseAgent
{
    public CtAgInvoice(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "ct-ag-invoice", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an invoice extraction agent. Given an ingested vendor invoice document, extract structured invoice details:
          - invoiceId, vendorName, invoiceDate, dueDate, currency, totalAmount
          - categories: each with categoryName, categoryTotal
          - lineItems: each with categoryName, description, quantity, unitPrice, lineTotal
        Make sure the sum of lineItems within a category equals the categoryTotal, and the sum of categoryTotals equals totalAmount.
        Return a single JSON object with the structure above. No text outside the JSON.
        """;
}
