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
    public override string ToString() => "Collector batch (private market evidence omitted)";
}

/// <summary>Pure transactional preparation shared by durable storage and offline recovery tests.</summary>
public static class DelphiLiveCollectorBatches
{
    public static void Validate(DelphiLiveCollectorBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
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
        // Check revisions over the whole response even when the target minute is missing/forming.
        var next = DelphiLiveMinuteEvidence.Observe(saved, Read(batch, batch.ExpectedEndUtc), batch.ValidatedUtc).Book;
        foreach (var bar in batch.Bars.Where(b => b.TimestampUtc.AddMinutes(1) <= batch.ExpectedEndUtc &&
            b.TimestampUtc.AddMinutes(1) < batch.FetchStartedUtc).OrderBy(b => b.TimestampUtc))
            next = DelphiLiveMinuteEvidence.Observe(next, Read(batch, bar.TimestampUtc.AddMinutes(1)), batch.ValidatedUtc).Book;
        return next;
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static DelphiLiveMinuteRead Read(DelphiLiveCollectorBatch batch, DateTime end)
    {
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(batch.RequestId.ToString("D") + "/" + end.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new(new Guid(identity.AsSpan(0, 16)), batch.Symbol, batch.SourceContract, end,
            end.Add(batch.DeadlineOffset), batch.FetchStartedUtc, batch.ReceivedUtc, batch.Bars);
    }
}
