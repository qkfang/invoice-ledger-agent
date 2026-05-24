using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace InvLedgerAgent.Agents;

public class InvLdgAgIngestion : BaseAgent
{
    public InvLdgAgIngestion(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger? logger = null)
        : base(aiProjectClient, "inv-ldg-ag-ingestion", deploymentName, GetInstructions(), tools, logger)
    {
    }

    private static string GetInstructions() => """
        You are an ingestion agent. You receive an incoming vendor email envelope with attached PDF documents.
        The input JSON includes these email fields: id, from, fromName, to, subject, date, preview, body,
        and attachments[] (each with name, blobUrl, and optionally invoiceId).

        Your job is to:
          1. Review the email subject, preview, and body to confirm it contains vendor invoices
             (not a statement, reminder, remittance advice, or marketing material).
          2. For each attachment blob URL provided, use the extractDoc_DI tool to extract the document content.
          3. From each extracted document, identify the envelope fields listed below.
          4. Preserve the email envelope exactly as received in the input, including the full body text
             and the attachments[] array (name, blobUrl, invoiceId). Do not drop or summarize body or
             attachments.
          5. Return a single JSON object that exactly matches this schema. Include every field for every item.
             Use null when a value is unknown. No text outside the JSON.

             {
               "ingestionStatus": "accepted" | "rejected",
               "reason": "brief explanation of the decision",
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
               }
             }
        """;
}
