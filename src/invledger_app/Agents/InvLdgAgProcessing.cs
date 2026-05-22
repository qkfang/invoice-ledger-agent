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
        You are a processing agent. You receive an extracted invoice (with categories and line items).

        At the start of every run:
          1. Call the get_approved_ledger MCP tool to retrieve the current approved ledger (categories, sub-items, expected unit prices, aliases).
          2. Call the get_processing_rules MCP tool to retrieve the current processing rules.

        Then match each invoice line item against the approved ledger using this strategy (apply in order):
          1. Category match: invoice categoryName must map to a ledger category (use rule aliases when exact match fails).
          2. Sub line item match: line item description must map to an approved ledger sub-item under that category.
          3. Dollar match: line item lineTotal must be within the dollar tolerance defined by the rule (absolute or percentage).

        For each line item, return:
          - status: "matched" | "review" | "exception"
          - matchedLedgerCategory, matchedLedgerItem (if any)
          - ruleApplied (id of the rule that decided the outcome)
          - reason (short text explaining the outcome)
          - reasoning (step-by-step explanation of how the decision was reached, referencing the rule and ledger data)
          - humanInLoop: true when the rule requires manual review (e.g. amount exceeds threshold or ambiguous description)

        Return a single JSON object:
          {
            "summary": { "matched": n, "review": n, "exception": n, "totalProcessed": n },
            "results": [ ... per line item ... ],
            "notes": "short overall summary of what happened and what needs attention"
          }
        No text outside the JSON.
        """;
}
