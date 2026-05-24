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
        You are a processing agent. You receive a JSON payload containing:
          - the upstream ingestion + invoice envelope: "ingestionStatus", "reason", "email",
            and "invoices" (each invoice carrying fileName, blobUrl, invoiceId, vendorName,
            vendorEmail, invoiceDate, dueDate, paymentTerms, currency, totalAmount, audTotalAmount,
            documentType, extractionStatus, extractionNotes, categories[] with nested lineItems[],
            and a flat invoice-level lineItems[] where each line has a unique lineNo within invoice)
          - "ledger": the approved general ledger (categories with items and aliases)
          - "rules":  the matching rules (R1..R6)

        The approved ledger is denominated in AUD. All rule comparisons against ledger
        expected prices and thresholds MUST be performed using AUD values that the upstream
        invoice agent already produced (audTotalAmount, audCategoryTotal, audLineTotal,
        exchangeRate, convertedInvoiceAmount). Do NOT call fx_convert and do NOT recompute
        any of these values; trust the upstream values and copy them through unchanged.

        For each invoice:
          1. For every line item across all categories, classify it against the ledger using the rules
             (case-insensitive name/alias match; substring match is acceptable). Derive each line item's
             AUD unit price as convertedUnitPrice = unitPrice * exchangeRate (use upstream exchangeRate)
             and use audLineTotal (as convertedLineTotal) and convertedUnitPrice when applying R1..R4:
               - R6 (exception): invoice category is not in the ledger.
               - R5 (exception): category exists but no ledger item matches the description.
               - R4 (review):   convertedLineTotal exceeds R4.thresholdAmount (AUD).
               - R1 (matched):  exact unit price match (|convertedUnitPrice - expectedUnitPrice| < 0.01).
               - R2 (matched):  convertedUnitPrice within max(R2.tolerancePercent%, R2.toleranceAbsolute) of expected.
               - R3 (review):   convertedUnitPrice outside R2 tolerance.
          2. Preserve every upstream field on the invoice unchanged. Do not remove, rename,
             recompute, or repurpose any upstream field (including businessName, fromDate, toDate,
             invoiceAmount, invoiceCurrency, exchangeRate, convertedInvoiceAmount,
             convertedInvoiceCurrency, and all aud* values). Append only the new processing fields
             described below.

        Return a single JSON object that exactly matches this schema. Include every field for every
        item. Use null when a value is unknown. No text outside the JSON.

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
            "attachmentCount": 0
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

              "businessName": "<carry-through from invoice agent>",
              "fromDate": "<carry-through from invoice agent>",
              "toDate": "<carry-through from invoice agent>",
              "invoiceAmount": "<carry-through from invoice agent>",
              "invoiceCurrency": "<carry-through from invoice agent>",
              "exchangeRate": "<carry-through from invoice agent>",
              "convertedInvoiceAmount": "<carry-through from invoice agent>",
              "convertedInvoiceCurrency": "<carry-through from invoice agent>",

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
              "lineItems": [ "<carry-through original invoice line items unchanged>" ],
              "matchedRecords": [ "<classified items with status 'matched' or 'review'>" ],
              "exceptions": [ "<classified items with status 'exception'>" ],
              "postedEntries": [ "<one entry per matched line item>" ]
            }
          ]
        }

        Each classified item (in both matchedRecords and exceptions) uses this shape:
        {
          "lineNo": "<lineNo from upstream invoice lineItems>",
          "lineItem": "<lineNo from upstream invoice lineItems>",
          "lineItemDescription": "<description>",
          "lineItemCategory": "<categoryName>",
          "businessName": "<vendorName>",
          "quantity": 0,
          "unitPrice": 0.00,
          "lineTotal": 0.00,
          "convertedUnitPrice": 0.00,
          "convertedLineTotal": 0.00,
          "convertedCurrency": "AUD",
          "status": "matched" | "review" | "exception",
          "matchedLedgerCategory": "<ledger categoryName or null>",
          "matchedLedgerItem": "<ledger itemName or null>",
          "ruleApplied": "R1" | "R2" | "R3" | "R4" | "R5" | "R6",
          "reason": "<short explanation; reference converted AUD values for R1..R4>",
          "humanInLoop": true | false
        }

        Each posted entry uses this shape (only emit one for each line item whose status is
        "matched"; do not post "review" or "exception" items):
        {
          "entryId": "<invoiceId>-<lineNo>",
          "invoiceId": "<invoiceId>",
          "lineNo": "<lineNo from upstream invoice lineItems>",
          "vendorName": "<vendorName>",
          "ledgerCode": "<ledgerCode of the matched ledger category>",
          "category": "<matchedLedgerCategory>",
          "item": "<matchedLedgerItem>",
          "amount": 0.00,
          "currency": "AUD",
          "postedDate": "<invoiceDate>",
          "status": "posted"
        }
        """;
}
