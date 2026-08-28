#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>
/// One durably available OfficialPaper run and its same-run Breakout evidence
/// for a monitored symbol. Null probabilities mean the latest run did not
/// contain usable evidence for that symbol.
/// </summary>
public sealed record FreshDelphiBreakoutEvidenceSnapshot(
    Guid RunId,
    DateTime RunStartedUtc,
    DateTime AvailableUtc,
    bool IsValid,
    bool IsBreakoutPublished,
    double? BreakoutProbability,
    double? DirectionEdge,
    double? DownProbability);

/// <summary>
/// Selects the latest valid OfficialPaper evidence that was actually durable
/// before a policy bar began. The newest run wins even when it did not publish
/// the symbol, preventing fallback to an older favorable signal.
/// </summary>
public static class FreshDelphiBreakoutEvidenceResolver
{
    public static DelayedIntradayBreakoutEvidence? Resolve(
        IEnumerable<FreshDelphiBreakoutEvidenceSnapshot> timeline,
        DateTime entryUtc,
        DateTime availableAtUtc)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        RequireUtc(entryUtc, nameof(entryUtc));
        RequireUtc(availableAtUtc, nameof(availableAtUtc));

        FreshDelphiBreakoutEvidenceSnapshot? latest = timeline
            .Where(item =>
                item.IsValid &&
                item.RunStartedUtc.Kind == DateTimeKind.Utc &&
                item.AvailableUtc.Kind == DateTimeKind.Utc &&
                item.RunStartedUtc > entryUtc &&
                item.RunStartedUtc <= item.AvailableUtc &&
                item.AvailableUtc <= availableAtUtc)
            .OrderByDescending(item => item.AvailableUtc)
            .ThenByDescending(item => item.RunStartedUtc)
            .ThenByDescending(item => item.RunId)
            .FirstOrDefault();

        return latest is null
            ? null
            : new DelayedIntradayBreakoutEvidence(
                latest.RunId,
                latest.RunStartedUtc,
                latest.AvailableUtc,
                IsLatestAvailableOfficialRun: true,
                latest.IsValid,
                latest.IsBreakoutPublished,
                latest.BreakoutProbability,
                latest.DirectionEdge,
                latest.DownProbability);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
