using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace InvLedgerAgent.Agents;

public class InvLdgAgInvoice : BaseAgent
{
    public InvLdgAgInvoice(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-invoice", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an invoice extraction agent. Given an ingested vendor invoice document, extract structured invoice details:
          - invoiceId, vendorName, invoiceDate, dueDate, currency, totalAmount
          - categories: each with categoryName, categoryTotal
          - lineItems: each with categoryName, description, quantity, unitPrice, lineTotal
        Make sure the sum of lineItems within a category equals the categoryTotal, and the sum of categoryTotals equals totalAmount.
        After extracting all amounts, use the fx_convert tool to convert each monetary value from the invoice currency to AUD:
          - Convert totalAmount → audTotalAmount on the invoice header.
          - Convert each categoryTotal → audCategoryTotal on every category.
          - Convert each lineTotal → audLineTotal on every line item.
        Return a single JSON object with the structure above including all aud* fields. No text outside the JSON.
        """;
}
