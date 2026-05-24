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
          - "invoices": each with invoiceId, vendorName, currency, totalAmount, audTotalAmount,
            exchangeRate, and a flat lineItems[] (each line has a unique lineNo within its invoice
            and an audLineTotal already converted to AUD by the upstream invoice agent).
          - "ledger": the approved general ledger (categories with items and aliases). The ledger
            categories are organisational only — DO NOT match by category.
          - "rules":  the dollar matching rules (R1..R5).

        The approved ledger is denominated in AUD. Do NOT call fx_convert and do NOT recompute any
        upstream value. Trust the upstream audLineTotal / exchangeRate. Derive each line's AUD unit
        price as convertedUnitPrice = unitPrice * exchangeRate. All matching MUST be done using the
        AUD-converted values against the ledger's AUD expectedUnitPrice.

        For every line item across every invoice, find the best ledger item by matching the line
        item's description and the invoice's businessName (vendorName) against ledger itemName and
        aliases (case-insensitive; substring acceptable). Then classify using the dollar rules:
            R5 (exception): no ledger item matches the description / business.
            R4 (review):    convertedLineTotal exceeds R4.thresholdAmount (AUD).
            R1 (matched):   exact unit price match (|convertedUnitPrice - expectedUnitPrice| < 0.01).
            R2 (matched):   convertedUnitPrice within max(R2.tolerancePercent%, R2.toleranceAbsolute) of expected.
            R3 (review):    convertedUnitPrice outside R2 tolerance.

        Your only job is to produce the two new top-level sections that will be appended to the run's
        result.json by the host code. Do NOT echo invoices, ledger, rules, email, or any other field.

        Emit exactly this JSON, no other text. Put every "matched" or "review" classification in
        LedgerMatched, and every "exception" classification in LedgerException.

        {
          "LedgerMatched": [
            {
              "invoiceId": "<invoiceId>",
              "lineNo": "<lineNo>",
              "lineItemDescription": "<description>",
              "businessName": "<vendorName>",
              "quantity": 0,
              "unitPrice": 0.00,
              "lineTotal": 0.00,
              "convertedUnitPrice": 0.00,
              "convertedLineTotal": 0.00,
              "convertedCurrency": "AUD",
              "status": "matched" | "review",
              "matchedLedgerItem": "<ledger itemName>",
              "ledgerCode": "<ledgerCode of the matched ledger item>",
              "ruleApplied": "R1" | "R2" | "R3" | "R4",
              "reason": "<short explanation referencing AUD values>",
              "humanInLoop": true | false
            }
          ],
          "LedgerException": [
            {
              "invoiceId": "<invoiceId>",
              "lineNo": "<lineNo>",
              "lineItemDescription": "<description>",
              "businessName": "<vendorName>",
              "quantity": 0,
              "unitPrice": 0.00,
              "lineTotal": 0.00,
              "convertedUnitPrice": 0.00,
              "convertedLineTotal": 0.00,
              "convertedCurrency": "AUD",
              "status": "exception",
              "matchedLedgerItem": null,
              "ledgerCode": null,
              "ruleApplied": "R5",
              "reason": "<short explanation>",
              "humanInLoop": false
            }
          ]
        }
        """;
}
