#nullable enable
using Core.TMX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed record MarketIndexRow(string Name, decimal? Change)
{
    public string ChangeText => Change?.ToString("+0.00;-0.00;0.00") + (Change.HasValue ? "%" : "Unavailable");
    public string Color => Change switch { > 0 => "#55D68B", < 0 => "#FF8585", _ => "#A9BACD" };
}

public sealed record MarketContextSnapshot(DateTimeOffset ReceivedAt, IReadOnlyDictionary<string, decimal?> Changes,
    int? Advancers, int? Decliners, int? Unchanged)
{
    // Preserve provider nulls and reject duplicates; receipt time is never a source observation time.
    public static MarketContextSnapshot Parse(string json, DateTimeOffset receivedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            throw new InvalidOperationException("Market snapshot was not available.");
        var data = root.GetProperty("data");
        var changes = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        if (data.TryGetProperty("quotes", out var quotes) && quotes.ValueKind == JsonValueKind.Array)
            foreach (var quote in quotes.EnumerateArray())
            {
                if (quote.ValueKind != JsonValueKind.Object || !quote.TryGetProperty("symbol", out var name) ||
                    name.GetString() is not { } symbol) continue;
                decimal? value = quote.TryGetProperty("percentChange", out var percent) && percent.TryGetDecimalSafe(out var number) ? number : null;
                if (!changes.TryAdd(symbol, value)) throw new InvalidOperationException("Duplicate market index.");
            }
        int? up = null, down = null, flat = null;
        if (data.TryGetProperty("breadth", out var breadth) && breadth.ValueKind == JsonValueKind.Array)
        {
            var tsx = breadth.EnumerateArray().Where(x => x.TryGetProperty("exchange", out var exchange) && exchange.GetString() == "TSX").ToArray();
            if (tsx.Length > 1) throw new InvalidOperationException("Duplicate TSX breadth.");
            if (tsx.Length == 1) { up = Count(tsx[0], "advancers"); down = Count(tsx[0], "decliners"); flat = Count(tsx[0], "unchanged"); }
        }
        return new(receivedAt, changes, up, down, flat);
    }
    private static int? Count(JsonElement row, string field) => row.TryGetProperty(field, out var item) &&
        item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value) && value >= 0 ? value : null;
}

internal static class MarketJsonNumber
{
    internal static bool TryGetDecimalSafe(this JsonElement element, out decimal value)
    { value = 0; return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value); }
}

public sealed class MarketContextViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<CancellationToken, Task<MarketContextSnapshot>> read;
    private readonly CancellationTokenSource lifetime = new();
    private MarketContextSnapshot? snapshot;
    private bool started, disposed;
    public MarketContextViewModel(Func<CancellationToken, Task<MarketContextSnapshot>> read) => this.read = read;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Status { get; private set; } = "Latest available snapshot";
    public string Timing => snapshot == null ? "TMX · source quote time unavailable" :
        $"TMX · received {TradingAccountOverview.Local(snapshot.ReceivedAt.UtcDateTime)} · provider quote time / delay unverified";
    public MarketIndexRow Composite => new("TSX Composite", snapshot?.Changes.GetValueOrDefault("^TSX"));
    public MarketIndexRow Tsx60 => new("TSX 60", snapshot?.Changes.GetValueOrDefault("^TX60"));
    public string Breadth => snapshot?.Advancers is { } up && snapshot.Decliners is { } down
        ? $"TSX breadth  {up:N0} ↑ / {down:N0} ↓" : "TSX breadth unavailable";
    public string Unchanged => snapshot?.Unchanged is { } flat ? $"{flat:N0} unchanged" : "Unchanged unavailable";
    public IReadOnlyList<MarketIndexRow> Sectors => TsxSectorSymbols.All.Select(pair =>
        new MarketIndexRow(pair.Value, snapshot?.Changes.GetValueOrDefault(pair.Key)))
        .OrderByDescending(row => row.Change.HasValue).ThenByDescending(row => row.Change).ThenBy(row => row.Name).ToArray();

    public async Task LoadOnceAsync()
    {
        if (started || disposed) return;
        started = true; Status = "Loading latest available snapshot…"; Notify();
        try
        {
            var result = await read(lifetime.Token);
            if (disposed) return;
            snapshot = result;
            int count = Sectors.Count(row => row.Change.HasValue) + (Composite.Change.HasValue ? 1 : 0) + (Tsx60.Change.HasValue ? 1 : 0);
            Status = count == 13 && result.Advancers != null && result.Decliners != null && result.Unchanged != null
                ? "Latest available snapshot · loaded on opening" : "Partial snapshot · some values unavailable";
        }
        catch (OperationCanceledException) when (disposed) { return; }
        catch { if (!disposed) Status = "Market snapshot unavailable"; }
        if (!disposed) Notify();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new(null));
    public void Dispose() { disposed = true; lifetime.Cancel(); lifetime.Dispose(); }
}

public sealed record SectorExposureRow(string Sector, decimal Value, decimal Share)
{
    public decimal Percent => Share * 100;
    public string Amount => $"{Value:N2} CAD";
    public string ShareText => Share.ToString("P1");
}
