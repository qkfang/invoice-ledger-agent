using System.ComponentModel;
using System.Text.Json;
using InvLedgerAgent.Services;
using ModelContextProtocol.Server;

namespace InvLedgerAgent.Mcp;

[McpServerToolType]
public class InvLedgerMcpTools
{
    private readonly DocIntelligenceService _docIntelligence;
    private readonly ContentUnderstandingService _contentUnderstanding;
    private readonly NotificationService _notification;
    private readonly GeneralLedgerService _ledger;
    private readonly FxRateService _fxRate;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public InvLedgerMcpTools(
        DocIntelligenceService docIntelligence,
        ContentUnderstandingService contentUnderstanding,
        NotificationService notification,
        GeneralLedgerService ledger,
        FxRateService fxRate)
    {
        _docIntelligence = docIntelligence;
        _contentUnderstanding = contentUnderstanding;
        _notification = notification;
        _ledger = ledger;
        _fxRate = fxRate;
    }

    [McpServerTool(Name = "extractDoc_DI"),
     Description("Use Azure AI Document Intelligence (prebuilt-layout) to extract fields and information from a document URL.")]
    public async Task<string> ExtractDocDI(
        [Description("Public URL of the document to analyze")] string documentUrl)
    {
        if (string.IsNullOrWhiteSpace(documentUrl))
            return "Error: documentUrl is required.";

        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri))
            return "Error: documentUrl is not a valid URL.";

        var result = await _docIntelligence.AnalyzeFromUrlAsync(uri);
        return result.Markdown;
    }

    [McpServerTool(Name = "extractDoc_CU"),
     Description("Use Azure AI Content Understanding to extract fields and information from a document URL.")]
    public async Task<string> ExtractDocCU(
        [Description("Public URL of the document to analyze")] string documentUrl,
        [Description("Content Understanding analyzer id (e.g. prebuilt-documentSearch, prebuilt-invoice, prebuilt-receipt). Defaults to prebuilt-documentSearch.")]
        string analyzerId = "prebuilt-documentSearch")
    {
        if (string.IsNullOrWhiteSpace(documentUrl))
            return "Error: documentUrl is required.";

        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri))
            return "Error: documentUrl is not a valid URL.";

        var result = await _contentUnderstanding.AnalyzeFromUrlAsync(uri, analyzerId);
        return result.Markdown;
    }

    [McpServerTool(Name = "notification"),
     Description("Send a notification email to a recipient.")]
    public async Task<string> Notification(
        [Description("Recipient email address")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body")] string body)
    {
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(subject))
            return "Error: 'to' and 'subject' are required.";

        return await _notification.SendEmailAsync(to, subject, body ?? string.Empty);
    }

    [McpServerTool(Name = "ledger_list"),
     Description("List all entries currently in the simulated general ledger.")]
    public string LedgerList()
    {
        return JsonSerializer.Serialize(_ledger.ListEntries(), JsonOptions);
    }

    [McpServerTool(Name = "ledger_get"),
     Description("Get a single general ledger entry by id.")]
    public string LedgerGet(
        [Description("Ledger entry id")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "Error: id is required.";

        var entry = _ledger.GetEntry(id);
        return entry is null
            ? $"Error: ledger entry '{id}' not found."
            : JsonSerializer.Serialize(entry, JsonOptions);
    }

    [McpServerTool(Name = "ledger_add"),
     Description("Add a new general ledger entry from an invoice. Returns the created entry as JSON.")]
    public string LedgerAdd(
        [Description("Invoice number")] string invoiceNumber,
        [Description("Vendor name")] string vendor,
        [Description("GL account code, e.g. 6000-Office")] string account,
        [Description("Invoice amount as decimal")] decimal amount,
        [Description("ISO currency code, e.g. USD")] string currency = "USD",
        [Description("Entry date in ISO-8601 format. Defaults to today (UTC).")] string? entryDate = null,
        [Description("Status, e.g. pending, posted, paid")] string status = "pending",
        [Description("Optional description")] string? description = null)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber) || string.IsNullOrWhiteSpace(vendor) || string.IsNullOrWhiteSpace(account))
            return "Error: invoiceNumber, vendor and account are required.";

        var date = ParseDate(entryDate) ?? DateTime.UtcNow;
        var entry = _ledger.AddEntry(invoiceNumber, vendor, account, amount, currency, date, status, description);
        return JsonSerializer.Serialize(entry, JsonOptions);
    }

    [McpServerTool(Name = "ledger_update"),
     Description("Update fields on an existing general ledger entry. Only non-null parameters are applied.")]
    public string LedgerUpdate(
        [Description("Ledger entry id to update")] string id,
        [Description("New invoice number, or null to keep")] string? invoiceNumber = null,
        [Description("New vendor, or null to keep")] string? vendor = null,
        [Description("New GL account code, or null to keep")] string? account = null,
        [Description("New amount, or null to keep")] decimal? amount = null,
        [Description("New currency, or null to keep")] string? currency = null,
        [Description("New entry date (ISO-8601), or null to keep")] string? entryDate = null,
        [Description("New status, or null to keep")] string? status = null,
        [Description("New description, or null to keep")] string? description = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "Error: id is required.";

        var date = ParseDate(entryDate);
        var updated = _ledger.UpdateEntry(id, invoiceNumber, vendor, account, amount, currency, date, status, description);
        return updated is null
            ? $"Error: ledger entry '{id}' not found."
            : JsonSerializer.Serialize(updated, JsonOptions);
    }

    [McpServerTool(Name = "ledger_delete"),
     Description("Delete a general ledger entry by id.")]
    public string LedgerDelete(
        [Description("Ledger entry id to delete")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "Error: id is required.";

        return _ledger.DeleteEntry(id)
            ? $"Deleted ledger entry '{id}'."
            : $"Error: ledger entry '{id}' not found.";
    }

    [McpServerTool(Name = "fx_convert"),
     Description("Convert a monetary amount from one currency to AUD using the configured exchange rates.")]
    public string FxConvert(
        [Description("Source currency code, e.g. USD")] string fromCurrency,
        [Description("Target currency code, e.g. AUD")] string toCurrency,
        [Description("Amount to convert")] decimal amount,
        [Description("Invoice date (ISO-8601, e.g. 2025-03-15) used to look up the rate for the applicable period. Defaults to today.")] string? invoiceDate = null)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
            return "Error: fromCurrency and toCurrency are required.";

        var from = fromCurrency.ToUpperInvariant();
        var to = toCurrency.ToUpperInvariant();

        if (from == to)
        {
            return JsonSerializer.Serialize(new
            {
                from,
                to,
                amount,
                rate = 1.0m,
                convertedAmount = amount
            }, JsonOptions);
        }

        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(invoiceDate) && DateOnly.TryParse(invoiceDate, out var parsed))
            date = parsed;

        var rate = _fxRate.GetRate(from, to, date);
        if (rate is null)
            return $"Error: no exchange rate found for {from} to {to}.";

        return JsonSerializer.Serialize(new
        {
            from,
            to,
            amount,
            rate = rate.Value,
            convertedAmount = Math.Round(amount * rate.Value, 2)
        }, JsonOptions);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
