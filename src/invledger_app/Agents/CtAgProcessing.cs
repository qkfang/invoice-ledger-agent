using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace FxAgent.Agents;

public class CtAgProcessing : BaseAgent
{
    public CtAgProcessing(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "ct-ag-processing", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are a processing agent. You receive an extracted invoice (with categories and line items) and a list of matching rules,
        and must match each invoice line item against the approved ledger.

        Matching strategy (apply in order):
          1. Category match: invoice categoryName must map to a ledger category (use rule aliases when exact match fails).
          2. Sub line item match: line item description must map to an approved ledger sub-item under that category.
          3. Dollar match: line item lineTotal must be within the dollar tolerance defined by the rule (absolute or percentage).

        For each line item, return:
          - status: "matched" | "review" | "exception"
          - matchedLedgerCategory, matchedLedgerItem (if any)
          - ruleApplied (id of the rule that decided the outcome)
          - reason (short text)
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
