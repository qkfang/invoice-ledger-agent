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
        You are a processing agent. You receive one or more invoices (each with categories and line items).
        The input may be a single invoice object, an array of invoices, or an object containing an "invoices" array.

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
        Return a single JSON object of the form { "invoices": [ <processed invoice>, ... ] } containing every input invoice processed.
        No text outside the JSON.
        """;
}
