#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>Read-only published daily picks. A preview grants no operational entry authority.</summary>
public sealed record DelphiLiveWatchlistPreview(
    DelphiLiveOfficialRunSource? Run, IReadOnlyList<DelphiLivePreviewPick> Picks)
{
    public static DelphiLiveWatchlistPreview Empty { get; } = new(null, []);
}

public sealed record DelphiLivePreviewPick(
    string Symbol, Guid CandidateId, decimal DailyComposite, decimal PreviousClose,
    IReadOnlyList<DelphiLivePreviewLens> Lenses)
{
    public bool Eligible => Lenses.Any(l => l.Eligible && l.Rank is >= 1 and <= 25);
    public string Source => string.Join(" · ", Lenses.Select(l => $"{l.Lens} #{l.Rank}"));
    public int BestRank => Lenses.Min(l => l.Rank);
    public string EligibilityReason => Eligible ? "Daily candidate" :
        string.Join(" · ", Lenses.Select(l => l.FirstFailure ?? "Daily direction is not Buy").Distinct());

    public DelphiLiveDailySetupQuality? ToSetup(DelphiLiveOfficialRunSource run) => !Eligible ? null :
        new(run.RunId, CandidateId, run.StrategyVersionId, DailyComposite,
            Lenses.Where(l => l.Eligible && l.Rank is >= 1 and <= 25).Select(l =>
                new DelphiLiveSourceLensQuality(Enum.Parse<DelphiLiveSourceLens>(l.Lens), true, true,
                    l.Rank, l.RankingKey, l.FirstFailure ?? "Published eligible daily pick", l.GateTraceJson)).ToImmutableArray());
}

public sealed record DelphiLivePreviewLens(
    string Lens, int Rank, bool Eligible, decimal RankingKey, string? FirstFailure, string GateTraceJson);
