#nullable enable

using Core.Calibration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.TMX.Models.Domain;

public static class IntradayEvidenceVersions
{
    public const int Schema = 1;
    public const string Provider = "TMXMoney";
    public const string SourceContract = "TmxChartIntradayNoFreqV1";
    public const string Collector = "IntradayEvidenceCollectorV1";
    public const string Policy = "DelayedIntradaySwingV1";
}

public enum IntradayPollPurpose
{
    PaperMonitor,
    Backfill,
    Probe
}

public enum IntradayPollAuditState
{
    Valid,
    Degraded,
    Invalid,
    Failed
}

public sealed record IntradayPollContext(
    Guid PollCycleId,
    IntradayPollPurpose Purpose,
    string CollectorVersion,
    string? PolicyVersion,
    CodeProvenance Code);

public sealed record IntradayEvidenceAppendResult(
    Guid ObservationId,
    IntradayPollAuditState AuditState,
    string? AuditCode,
    int CompletedBarCount,
    int PersistedNewBarCount,
    int ConflictCount);

internal sealed record StoredIntradayEvidenceBar(
    DateTime EventUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume)
{
    public static StoredIntradayEvidenceBar From(OhlcvBar bar) =>
        new(
            bar.TimestampUtc,
            NormalizePrice(bar.Open),
            NormalizePrice(bar.High),
            NormalizePrice(bar.Low),
            NormalizePrice(bar.Close),
            bar.Volume);

    private static decimal NormalizePrice(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}

internal sealed record IntradayEvidenceWritePlan(
    IReadOnlyList<OhlcvBar> CompletedBars,
    IReadOnlyList<OhlcvBar> NewBars,
    IReadOnlyList<OhlcvBar> ConflictingBars,
    IntradayPollAuditState AuditState,
    string? AuditCode);

internal static class IntradayEvidencePersistencePlanner
{
    private const int LateEvidenceMinutes = 45;

    public static IntradayEvidenceWritePlan Create(
        IntradayPollContext context,
        TmxIntradayBatch batch,
        IReadOnlyCollection<StoredIntradayEvidenceBar> existingBars)
    {
        Validate(context, batch, existingBars);

        List<OhlcvBar> completed = batch.Bars
            .Where(bar =>
                bar.TimestampUtc.AddMinutes(batch.IntervalMinutes) <= batch.ReceivedUtc)
            .OrderBy(bar => bar.TimestampUtc)
            .ToList();
        IReadOnlyDictionary<DateTime, StoredIntradayEvidenceBar> existing = existingBars
            .ToDictionary(bar => bar.EventUtc);
        var newBars = new List<OhlcvBar>();
        var conflicts = new List<OhlcvBar>();

        foreach (OhlcvBar bar in completed)
        {
            if (!existing.TryGetValue(bar.TimestampUtc, out StoredIntradayEvidenceBar? stored))
            {
                newBars.Add(bar);
                continue;
            }

            if (stored != StoredIntradayEvidenceBar.From(bar))
                conflicts.Add(bar);
        }

        if (conflicts.Count > 0)
        {
            return new IntradayEvidenceWritePlan(
                completed.AsReadOnly(),
                Array.Empty<OhlcvBar>(),
                conflicts.AsReadOnly(),
                IntradayPollAuditState.Invalid,
                "CompletedBarConflict");
        }

        if (completed.Count == 0)
        {
            return new IntradayEvidenceWritePlan(
                completed.AsReadOnly(),
                newBars.AsReadOnly(),
                conflicts.AsReadOnly(),
                IntradayPollAuditState.Degraded,
                "NoCompletedEvidence");
        }

        TimeSpan latestAge = batch.ReceivedUtc -
            completed[^1].TimestampUtc.AddMinutes(batch.IntervalMinutes);
        bool late = latestAge > TimeSpan.FromMinutes(LateEvidenceMinutes);
        return new IntradayEvidenceWritePlan(
            completed.AsReadOnly(),
            newBars.AsReadOnly(),
            conflicts.AsReadOnly(),
            late ? IntradayPollAuditState.Degraded : IntradayPollAuditState.Valid,
            late ? "LateCompletedEvidence" : null);
    }

    public static void Validate(
        IntradayPollContext context,
        TmxIntradayBatch batch,
        IReadOnlyCollection<StoredIntradayEvidenceBar> existingBars)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(existingBars);

        if (context.PollCycleId == Guid.Empty)
            throw new ArgumentException("Poll cycle ID is required.", nameof(context));
        RequireText(context.CollectorVersion, 64, "Collector version");
        if (context.PolicyVersion is not null)
            RequireText(context.PolicyVersion, 64, "Policy version");
        RequireText(context.Code.Commit, 128, "Code commit");
        if (context.Code.WorkingTreeState is not ("Clean" or "Dirty" or "Unknown"))
            throw new ArgumentException("Working-tree state is invalid.", nameof(context));

        RequireText(batch.Symbol, 20, "Symbol");
        if (batch.IntervalMinutes is not (5 or 15))
            throw new ArgumentOutOfRangeException(
                nameof(batch),
                "Only five- and fifteen-minute evidence is supported in schema version 1.");
        RequireUtc(batch.RequestedStartUtc, nameof(batch.RequestedStartUtc));
        RequireUtc(batch.RequestedEndUtc, nameof(batch.RequestedEndUtc));
        RequireUtc(batch.FetchStartedUtc, nameof(batch.FetchStartedUtc));
        RequireUtc(batch.ReceivedUtc, nameof(batch.ReceivedUtc));
        if (batch.RequestedStartUtc > batch.RequestedEndUtc)
            throw new ArgumentException("Requested intraday window is inverted.", nameof(batch));
        if (batch.FetchStartedUtc > batch.ReceivedUtc)
            throw new ArgumentException("Receipt precedes fetch start.", nameof(batch));
        if (batch.AttemptCount < 0 || batch.RequestCount < 0)
            throw new ArgumentException("Transport counts cannot be negative.", nameof(batch));
        if (batch.Bars.GroupBy(bar => bar.TimestampUtc).Any(group => group.Count() > 1))
            throw new ArgumentException("Batch contains duplicate event timestamps.", nameof(batch));
        if (existingBars.GroupBy(bar => bar.EventUtc).Any(group => group.Count() > 1))
            throw new ArgumentException("Stored evidence contains duplicate natural keys.", nameof(existingBars));

        foreach (OhlcvBar bar in batch.Bars)
        {
            RequireUtc(bar.TimestampUtc, nameof(batch.Bars));
            if (bar.TimestampUtc.Second != 0 ||
                bar.TimestampUtc.Ticks % TimeSpan.TicksPerSecond != 0 ||
                bar.TimestampUtc.Minute % batch.IntervalMinutes != 0)
            {
                throw new ArgumentException("Bar event timestamp is misaligned.", nameof(batch));
            }
            if (bar.Open <= 0m || bar.High <= 0m || bar.Low <= 0m || bar.Close <= 0m ||
                bar.Low > bar.High || bar.Low > bar.Open || bar.Low > bar.Close ||
                bar.High < bar.Open || bar.High < bar.Close || bar.Volume < 0)
            {
                throw new ArgumentException("Batch contains invalid OHLCV evidence.", nameof(batch));
            }
        }
    }

    private static void RequireText(string value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException($"{label} is required and cannot exceed {maximumLength} characters.");
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
