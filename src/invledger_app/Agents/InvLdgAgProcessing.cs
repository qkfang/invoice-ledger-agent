using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace InvLedgerAgent.Agents;

public class InvLdgAgProcessing : BaseAgent
{
    public InvLdgAgProcessing(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-processing", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are a processing agent. You receive an invoice (with categories and line items).

        For each invoice:
          1. Call the fx_convert MCP tool to convert the invoice totalAmount from the original currency to AUD,
             passing the invoiceDate as the invoiceDate parameter.
          2. Produce a standardised extracted invoice JSON with this exact structure:

        {
          "invoiceId": "<invoiceId>",
          "businessName": "<vendorName>",
          "fromDate": "<invoiceDate>",
          "toDate": "<dueDate or empty string if not present>",
          "originalInvoiceAmount": <totalAmount as number>,
          "originalInvoiceCurrency": "<currency>",
          "exchangeRate": <the 'rate' field returned by fx_convert>,
          "convertedInvoiceAmount": <the 'convertedAmount' field returned by fx_convert>,
          "lineItems": [
            {
              "lineItem": "<lineItemId>",
              "lineItemDescription": "<description>",
              "lineItemCategory": "<categoryName>",
              "businessName": "<vendorName>",
              "quantity": <quantity>,
              "unitPrice": <unitPrice>,
              "lineTotal": <lineTotal>
            }
          ]
        }

        Flatten all line items from all categories into the single lineItems array, preserving the categoryName in lineItemCategory.
        Return exactly one JSON object. No text outside the JSON.
        """;
}
