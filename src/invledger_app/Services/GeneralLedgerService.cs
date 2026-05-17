using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FxAgent.Services;

public record LedgerEntry(
    string Id,
    string InvoiceNumber,
    string Vendor,
    string Account,
    decimal Amount,
    string Currency,
    DateTime EntryDate,
    string Status,
    string? Description);

public class GeneralLedgerService
{
    private readonly ConcurrentDictionary<string, LedgerEntry> _entries = new();
    private readonly ILogger<GeneralLedgerService> _logger;

    public GeneralLedgerService(ILogger<GeneralLedgerService> logger)
    {
        _logger = logger;
        SeedSampleData();
    }

    public IReadOnlyCollection<LedgerEntry> ListEntries() => _entries.Values.ToList();

    public LedgerEntry? GetEntry(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry : null;

    public LedgerEntry AddEntry(
        string invoiceNumber,
        string vendor,
        string account,
        decimal amount,
        string currency,
        DateTime entryDate,
        string status,
        string? description)
    {
        var id = Guid.NewGuid().ToString("N");
        var entry = new LedgerEntry(id, invoiceNumber, vendor, account, amount, currency, entryDate, status, description);
        _entries[id] = entry;
        _logger.LogInformation("Ledger entry added: {Id} invoice={Invoice} amount={Amount}", id, invoiceNumber, amount);
        return entry;
    }

    public LedgerEntry? UpdateEntry(
        string id,
        string? invoiceNumber,
        string? vendor,
        string? account,
        decimal? amount,
        string? currency,
        DateTime? entryDate,
        string? status,
        string? description)
    {
        if (!_entries.TryGetValue(id, out var existing))
            return null;

        var updated = existing with
        {
            InvoiceNumber = invoiceNumber ?? existing.InvoiceNumber,
            Vendor = vendor ?? existing.Vendor,
            Account = account ?? existing.Account,
            Amount = amount ?? existing.Amount,
            Currency = currency ?? existing.Currency,
            EntryDate = entryDate ?? existing.EntryDate,
            Status = status ?? existing.Status,
            Description = description ?? existing.Description
        };
        _entries[id] = updated;
        _logger.LogInformation("Ledger entry updated: {Id}", id);
        return updated;
    }

    public bool DeleteEntry(string id)
    {
        var removed = _entries.TryRemove(id, out _);
        if (removed)
            _logger.LogInformation("Ledger entry deleted: {Id}", id);
        return removed;
    }

    private void SeedSampleData()
    {
        AddEntry("INV-1001", "Acme Supplies", "6000-Office", 245.50m, "USD", DateTime.UtcNow.AddDays(-10), "posted", "Office supplies");
        AddEntry("INV-1002", "Contoso Services", "6200-Consulting", 1500.00m, "USD", DateTime.UtcNow.AddDays(-5), "pending", "Consulting fees");
    }
}
