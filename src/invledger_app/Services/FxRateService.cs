using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvLedgerAgent.Services;

public record FxRate(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
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

    public decimal? GetRate(string from, string to) =>
        _rates.FirstOrDefault(r =>
            r.From.Equals(from, StringComparison.OrdinalIgnoreCase) &&
            r.To.Equals(to, StringComparison.OrdinalIgnoreCase))?.Rate;

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
        new("USD", "AUD", 1.55m),
        new("EUR", "AUD", 1.70m),
        new("GBP", "AUD", 2.00m)
    ];

    private class FxRatesFile
    {
        [JsonPropertyName("rates")]
        public List<FxRate> Rates { get; set; } = [];
    }
}
