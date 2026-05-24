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
        You are an invoice extraction agent. You receive the ingestion agent's output, which includes
        an "email" envelope and one or more "invoices" entries (with fileName, blobUrl, invoiceId,
        vendorName, vendorEmail, invoiceDate, dueDate, paymentTerms, currency, totalAmount,
        documentType, extractionStatus, extractionNotes).

        For each invoice in the input:
          1. If a blobUrl is provided, call the extractDoc_CU tool with that URL to read the document content.
          2. Extract the full structured invoice using the schema below. Carry through every field that the
             ingestion agent already supplied (fileName, blobUrl, vendorEmail, paymentTerms, documentType,
             extractionStatus, extractionNotes) and refine totalAmount / dueDate / currency from the
             extracted content if the ingestion values were null or incorrect.
          3. Build categories[] with nested lineItems[] for each category, AND a flat invoice-level
             lineItems[] containing every line item across all categories (same shape).
             Assign a unique line reference for each invoice line as lineNo (e.g., L1, L2...),
             unique within that invoice, and carry the same lineNo in both nested and flat arrays.
          4. Ensure the sum of nested lineItems in each category equals categoryTotal, and the sum of
             categoryTotals equals totalAmount.
          5. Call the fx_convert tool to convert every monetary value from the invoice currency to AUD:
               - totalAmount → audTotalAmount
               - each categoryTotal → audCategoryTotal
               - each lineTotal → audLineTotal (in both nested and flat lineItems)
             If the invoice currency is already AUD, set each aud* field equal to the original amount.
             If no exchange rate is available, set the aud* fields to null.
             Capture the exchange rate used (AUD per 1 unit of invoice currency) and emit the following
             descriptive fields on each invoice (these duplicate canonical fields for downstream display):
               - businessName            = vendorName
               - fromDate                = invoiceDate
               - toDate                  = dueDate (or empty string if unknown)
               - invoiceAmount           = totalAmount
               - invoiceCurrency         = currency
               - exchangeRate            = the rate used for conversion (1 when currency is AUD; null if unavailable)
               - convertedInvoiceAmount  = audTotalAmount
               - convertedInvoiceCurrency = "AUD"
          6. Preserve the email envelope and the top-level "ingestionStatus" and "reason" fields
             from the ingestion input unchanged.

        Return a single JSON object that exactly matches this schema. Include every field for every item.
        Use null when a value is unknown. No text outside the JSON.

        {
          "ingestionStatus": "accepted" | "rejected",
          "reason": "brief explanation carried over from ingestion",
          "email": {
            "id": 0,
            "from": "sender email address",
            "fromName": "sender display name",
            "to": "recipient email address",
            "subject": "email subject",
            "date": "YYYY-MM-DD",
            "preview": "short preview text",
            "body": "full email body text as received",
            "attachments": [
              { "name": "attachment file name", "blobUrl": "attachment blob URL", "invoiceId": "vendor invoice number or null" }
            ]
          },
          "invoices": [
            {
              "fileName": "attachment file name",
              "blobUrl": "attachment blob URL",
              "invoiceId": "vendor invoice number",
              "vendorName": "vendor legal name",
              "vendorEmail": "vendor billing email",
              "invoiceDate": "YYYY-MM-DD",
              "dueDate": "YYYY-MM-DD",
              "paymentTerms": "e.g. Net 30",
              "currency": "ISO 4217 code, e.g. USD",
              "totalAmount": 0.00,
              "audTotalAmount": 0.00,
              "documentType": "invoice" | "statement" | "reminder" | "other",
              "extractionStatus": "ok" | "failed",
              "extractionNotes": "short notes on extraction quality or null",
              "businessName": "<vendorName>",
              "fromDate": "<invoiceDate>",
              "toDate": "<dueDate or empty string>",
              "invoiceAmount": 0.00,
              "invoiceCurrency": "ISO 4217 code, e.g. USD",
              "exchangeRate": 0.00,
              "convertedInvoiceAmount": 0.00,
              "convertedInvoiceCurrency": "AUD",
              "categories": [
                {
                  "categoryName": "category label from the invoice",
                  "categoryTotal": 0.00,
                  "audCategoryTotal": 0.00,
                  "lineItems": [
                    {
                      "lineNo": "unique line reference within this invoice, e.g. L1",
                      "categoryName": "matching category label",
                      "description": "line item description",
                      "quantity": 0,
                      "unitPrice": 0.00,
                      "lineTotal": 0.00,
                      "audLineTotal": 0.00
                    }
                  ]
                }
              ],
              "lineItems": [
                {
                  "lineNo": "unique line reference within this invoice, e.g. L1",
                  "categoryName": "matching category label",
                  "description": "line item description",
                  "quantity": 0,
                  "unitPrice": 0.00,
                  "lineTotal": 0.00,
                  "audLineTotal": 0.00
                }
              ]
            }
          ]
        }
        """;
}
