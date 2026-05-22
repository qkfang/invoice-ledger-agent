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
        You are an invoice extraction agent. Given a vendor invoice, extract structured invoice details.

        If the input is a document URL, first call the extractDoc_CU tool with that URL to read the document content.
        Then extract the following fields from the content:
          - invoiceId, vendorName, invoiceDate, dueDate, currency, totalAmount
          - categories: each with categoryName, categoryTotal
          - lineItems: each with categoryName, description, quantity, unitPrice, lineTotal
        Make sure the sum of lineItems within a category equals the categoryTotal, and the sum of categoryTotals equals totalAmount.
        After extracting all amounts, use the fx_convert tool to convert each monetary value from the invoice currency to AUD:
          - Convert totalAmount → audTotalAmount on the invoice header.
          - Convert each categoryTotal → audCategoryTotal on every category.
          - Convert each lineTotal → audLineTotal on every line item.
          - If the invoice currency is already AUD, set each aud* field equal to the corresponding original amount.
          - If no exchange rate is available, omit the aud* fields from the output.
        Return a single JSON object with the structure above including all aud* fields. No text outside the JSON.
        """;
}
