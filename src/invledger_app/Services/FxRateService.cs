using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvLedgerAgent.Services;

public record FxRate(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("fromDate")] string FromDate,
    [property: JsonPropertyName("toDate")] string? ToDate,
    [property: JsonPropertyName("rate")] decimal Rate);

public class FxRateService
{
    private readonly string _filePath;
    private readonly ILogger<FxRateService> _logger;
    private List<FxRate> _rates;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public FxRateService(IWebHostEnvironment env, ILogger<FxRateService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(env.WebRootPath, "data", "fx-rates.json");
        _rates = LoadFromFile();
    }

    public IReadOnlyList<FxRate> GetRates() => _rates;

    public decimal? GetRate(string from, string to, DateOnly? onDate = null)
    {
        bool DateInRange(FxRate r)
        {
            if (!DateOnly.TryParse(r.FromDate, out var fd)) return false;
            if (onDate < fd) return false;
            if (r.ToDate is not null)
            {
                if (!DateOnly.TryParse(r.ToDate, out var td)) return false;
                if (onDate > td) return false;
            }
            return true;
        }

        return _rates
            .Where(r =>
                r.From.Equals(from, StringComparison.OrdinalIgnoreCase) &&
                r.To.Equals(to, StringComparison.OrdinalIgnoreCase) &&
                (onDate is null || DateInRange(r)))
            .OrderByDescending(r => r.FromDate)
            .FirstOrDefault()?.Rate;
    }

    public void UpdateRates(IEnumerable<FxRate> rates)
    {
        _rates = rates.ToList();
        SaveToFile();
        _logger.LogInformation("FX rates updated: {Count} entries", _rates.Count);
    }

    private List<FxRate> LoadFromFile()
    {
        if (!File.Exists(_filePath))
            return DefaultRates();
        try
        {
            var json = File.ReadAllText(_filePath);
            var doc = JsonSerializer.Deserialize<FxRatesFile>(json, ReadOptions);
            return doc?.Rates ?? DefaultRates();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load fx-rates.json: {Message}", ex.Message);
            return DefaultRates();
        }
    }

    private void SaveToFile()
    {
        try
        {
            var doc = new FxRatesFile { Rates = _rates };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(doc, WriteOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not save fx-rates.json: {Message}", ex.Message);
        }
    }

    private static List<FxRate> DefaultRates() =>
    [
        new("USD", "AUD", "2025-01-01", "2025-03-31", 1.58m),
        new("USD", "AUD", "2025-04-01", "2025-06-30", 1.56m),
        new("USD", "AUD", "2025-07-01", "2025-09-30", 1.54m),
        new("USD", "AUD", "2025-10-01", "2025-12-31", 1.55m),
        new("USD", "AUD", "2026-01-01", null, 1.57m)
    ];

    private class FxRatesFile
    {
        [JsonPropertyName("rates")]
        public List<FxRate> Rates { get; set; } = [];
    }
}
