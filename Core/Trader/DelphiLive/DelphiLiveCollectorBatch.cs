#nullable enable
using Core.TMX.Models.Domain;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Trader.DelphiLive;

/// <summary>One immutable source request/response, including failures. Contains private market data.</summary>
public sealed record DelphiLiveCollectorBatch(Guid RequestId, string SourceContract, string Symbol,
    DateTime SessionOpenUtc, DateTime SessionCloseUtc, DateTime ExpectedEndUtc, TimeSpan DeadlineOffset,
    DateTime FetchStartedUtc, DateTime ReceivedUtc, DateTime ValidatedUtc,
    ImmutableArray<OhlcvBar> Bars, string RawResponseJson, string? FailureCode)
{
    // Absent on original session-wide receipts. Omitting null preserves their original fingerprints.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? RequestedStartUtc { get; init; }

    public override string ToString() => "Collector batch (private market evidence omitted)";
}

/// <summary>Pure transactional preparation shared by durable storage and offline recovery tests.</summary>
public static class DelphiLiveCollectorBatches
{
    public static void Validate(DelphiLiveCollectorBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.RequestedStartUtc is { } start && (start.Kind != DateTimeKind.Utc ||
            start != batch.ExpectedEndUtc.AddMinutes(-1) || batch.Bars.IsDefault || batch.Bars.Any(b => b?.TimestampUtc != start)))
            throw new ArgumentException("A latest-minute receipt can admit only the requested bar.");
        if (batch.RequestId == Guid.Empty || batch.SourceContract is null || batch.SourceContract.Length > 128 ||
            batch.Symbol is null || batch.Symbol.Length > 20 || batch.Bars.IsDefault || batch.Bars.Length > 1440 ||
            batch.RawResponseJson is null || Encoding.UTF8.GetByteCount(batch.RawResponseJson) > 1048576 ||
            batch.FailureCode is not null && (string.IsNullOrWhiteSpace(batch.FailureCode) || batch.FailureCode.Length > 64 || !batch.Bars.IsEmpty))
            throw new ArgumentException("A bounded explicit collector receipt is required.");
        using var raw = JsonDocument.Parse(batch.RawResponseJson);
        if (raw.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new ArgumentException("Raw receipt must be a JSON object or array (empty object for a transport failure).");
        var empty = Empty(batch);
        // Reuse UTC, symbol, session, OHLCV, duplicate, deadline and completion validation.
        _ = DelphiLiveMinuteEvidence.Observe(empty, Read(batch, batch.ExpectedEndUtc), batch.ValidatedUtc);
    }

    public static DelphiLiveMinuteEvidenceBook Empty(DelphiLiveCollectorBatch batch) => new(batch.Symbol,
        batch.SourceContract, batch.SessionOpenUtc, batch.SessionCloseUtc, [], []);

    public static DelphiLiveMinuteEvidenceBook Apply(DelphiLiveMinuteEvidenceBook saved, DelphiLiveCollectorBatch batch)
    {
        Validate(batch);
        DelphiLiveMinuteEvidence.Validate(saved);
        if (saved.Symbol != batch.Symbol || saved.SourceContract != batch.SourceContract ||
            saved.SessionOpenUtc != batch.SessionOpenUtc || saved.SessionCloseUtc != batch.SessionCloseUtc)
            throw new ArgumentException("Collector history cannot cross identity or session bounds.");
        if (saved.Observations.Any(o => o.CheckedUtc > batch.ValidatedUtc))
            throw new ArgumentException("Collector validation time cannot move backwards.");
        if (batch.FailureCode is not null) return saved;
        // Compare the entire validated response once, including corrections outside the target minute.
        var byStart = saved.Observations.ToDictionary(o => o.Bar.TimestampUtc);
        var byId = saved.Observations.ToDictionary(o => o.ObservationId, o => o.EndUtc);
        var conflicts = saved.ConflictingStartsUtc.ToHashSet();
        foreach (var bar in batch.Bars)
            if (bar.TimestampUtc.AddMinutes(1) < batch.FetchStartedUtc &&
                byStart.TryGetValue(bar.TimestampUtc, out var original) && original.Bar != bar)
                conflicts.Add(bar.TimestampUtc);

        Guid CheckedId(DateTime end)
        {
            var id = ObservationId(batch.RequestId, end);
            if (byId.TryGetValue(id, out var previousEnd) && previousEnd != end)
                throw new ArgumentException("An observation identity cannot represent another minute.");
            return id;
        }

        var targetId = CheckedId(batch.ExpectedEndUtc);
        foreach (var bar in batch.Bars)
        {
            var end = bar.TimestampUtc.AddMinutes(1);
            if (end > batch.ExpectedEndUtc || end >= batch.FetchStartedUtc) continue;
            var id = end == batch.ExpectedEndUtc ? targetId : CheckedId(end);
            if (conflicts.Count != 0 || byStart.ContainsKey(bar.TimestampUtc)) continue;
            byId.Add(id, end);
            byStart.Add(bar.TimestampUtc, new(id, bar, batch.FetchStartedUtc, batch.ReceivedUtc,
                batch.ValidatedUtc, end.Add(batch.DeadlineOffset)));
        }
        if (conflicts.Count != 0)
            return saved with { ConflictingStartsUtc = conflicts.Order().ToImmutableArray() };
        if (byStart.Count == saved.Observations.Length) return saved;
        return saved with { Observations = byStart.Values.OrderBy(o => o.EndUtc).ToImmutableArray() };
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Guid ObservationId(Guid requestId, DateTime end)
    {
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(requestId.ToString("D") + "/" + end.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new Guid(identity.AsSpan(0, 16));
    }

    private static DelphiLiveMinuteRead Read(DelphiLiveCollectorBatch batch, DateTime end) =>
        new(ObservationId(batch.RequestId, end), batch.Symbol, batch.SourceContract, end,
            end.Add(batch.DeadlineOffset), batch.FetchStartedUtc, batch.ReceivedUtc, batch.Bars);
}
