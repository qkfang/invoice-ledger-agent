using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace FxAgent.Agents;

public class InvLdgAgLedger : BaseAgent
{
    public InvLdgAgLedger(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-ledger", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are a ledger assistant. You answer questions about the approved general ledger: which categories exist,
        which sub-items are tracked under each category, current balances, and posted invoice history.

        Conventions:
          - Always answer using the data provided in the user message (it is the source of truth).
          - Provide concise answers; show totals with the ledger currency.
          - If a question cannot be answered from the ledger data, say so and suggest where the user can look.

        Return plain text. You may use short markdown tables for category or item listings when helpful.
        """;
}
