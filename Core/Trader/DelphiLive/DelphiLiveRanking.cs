#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveSourceLens
{
    Continuation,
    Breakout
}

public sealed record DelphiLiveSourceLensQuality
{
    public DelphiLiveSourceLensQuality(
        DelphiLiveSourceLens lens,
        bool isEligible,
        bool isPublished,
        int? rank,
        decimal? rankingKey,
        string reasonEvidence,
        string gateEvidence)
    {
        if (!Enum.IsDefined(lens))
            throw new ArgumentOutOfRangeException(nameof(lens));
        if (rank is <= 0)
            throw new ArgumentOutOfRangeException(nameof(rank), "A supplied source-lens rank must be positive.");
        if (isPublished && !rank.HasValue)
            throw new ArgumentException("A published source lens requires its frozen rank.", nameof(rank));
        if (string.IsNullOrWhiteSpace(reasonEvidence))
            throw new ArgumentException("Daily reason evidence is required.", nameof(reasonEvidence));
        if (string.IsNullOrWhiteSpace(gateEvidence))
            throw new ArgumentException("Daily gate evidence is required.", nameof(gateEvidence));

        Lens = lens;
        IsEligible = isEligible;
        IsPublished = isPublished;
        Rank = rank;
        RankingKey = rankingKey;
        ReasonEvidence = reasonEvidence;
        GateEvidence = gateEvidence;
    }

    public DelphiLiveSourceLens Lens { get; }
    public bool IsEligible { get; }
    public bool IsPublished { get; }
    public int? Rank { get; }
    public decimal? RankingKey { get; }
    public string ReasonEvidence { get; }
    public string GateEvidence { get; }

    public bool SelectedSource => IsPublished && Rank.HasValue;
}

public sealed record DelphiLiveDailySetupQuality
{
    public DelphiLiveDailySetupQuality(
        Guid delphiRunId,
        Guid candidateId,
        Guid dailyStrategyVersionId,
        decimal commonDelphiComposite,
        ImmutableArray<DelphiLiveSourceLensQuality> sourceLenses)
    {
        if (delphiRunId == Guid.Empty)
            throw new ArgumentException("The frozen Delphi run identity is required.", nameof(delphiRunId));
        if (candidateId == Guid.Empty)
            throw new ArgumentException("The frozen Delphi candidate identity is required.", nameof(candidateId));
        if (dailyStrategyVersionId == Guid.Empty)
            throw new ArgumentException("The daily strategy identity is required.", nameof(dailyStrategyVersionId));
        if (sourceLenses.IsDefaultOrEmpty)
            throw new ArgumentException("At least one source-lens record is required.", nameof(sourceLenses));

        var seen = new HashSet<DelphiLiveSourceLens>();
        int? bestRank = null;
        foreach (DelphiLiveSourceLensQuality source in sourceLenses)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!seen.Add(source.Lens))
                throw new ArgumentException("A source lens cannot be repeated.", nameof(sourceLenses));
            if (source.SelectedSource)
                bestRank = !bestRank.HasValue ? source.Rank : System.Math.Min(bestRank.Value, source.Rank!.Value);
        }
        if (!bestRank.HasValue)
            throw new ArgumentException("The frozen candidate must be published by at least one source lens.", nameof(sourceLenses));

        DelphiRunId = delphiRunId;
        CandidateId = candidateId;
        DailyStrategyVersionId = dailyStrategyVersionId;
        CommonDelphiComposite = commonDelphiComposite;
        SourceLenses = sourceLenses;
        BestSelectedSourceRank = bestRank.Value;
    }

    public Guid DelphiRunId { get; }
    public Guid CandidateId { get; }
    public Guid DailyStrategyVersionId { get; }
    public decimal CommonDelphiComposite { get; }
    public ImmutableArray<DelphiLiveSourceLensQuality> SourceLenses { get; }
    public int BestSelectedSourceRank { get; }

    public int? RankFor(DelphiLiveSourceLens lens)
    {
        foreach (DelphiLiveSourceLensQuality source in SourceLenses)
        {
            if (source.Lens == lens && source.SelectedSource)
                return source.Rank;
        }
        return null;
    }
}

public sealed record DelphiLiveRankCandidate
{
    public DelphiLiveRankCandidate(
        string symbol,
        DelphiLiveMomentumJudgment momentum,
        int? persistenceScore,
        DelphiLiveDailySetupQuality? dailySetup,
        bool isSessionCarryCandidate)
    {
        Symbol = DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(symbol);
        ArgumentNullException.ThrowIfNull(momentum);
        if (persistenceScore is < -4 or > 4)
            throw new ArgumentOutOfRangeException(nameof(persistenceScore));
        if (isSessionCarryCandidate && dailySetup is not null)
            throw new ArgumentException("A non-reselected carry candidate has no current Daily Setup Quality.", nameof(dailySetup));
        if (!isSessionCarryCandidate && dailySetup is null)
            throw new ArgumentException("A current-session frozen candidate requires Daily Setup Quality.", nameof(dailySetup));

        Momentum = momentum;
        PersistenceScore = persistenceScore;
        DailySetup = dailySetup;
        IsSessionCarryCandidate = isSessionCarryCandidate;
    }

    public string Symbol { get; }
    public DelphiLiveMomentumJudgment Momentum { get; }
    public int? PersistenceScore { get; }
    public DelphiLiveDailySetupQuality? DailySetup { get; }
    public bool IsSessionCarryCandidate { get; }
}

/// <summary>
/// The complete V1 diagnostic order. A negative result means <paramref name="x"/>
/// ranks ahead of <paramref name="y"/>.
/// </summary>
public sealed class DelphiLiveRankingComparer : IComparer<DelphiLiveRankCandidate?>
{
    public static DelphiLiveRankingComparer Instance { get; } = new();

    private DelphiLiveRankingComparer()
    {
    }

    public int Compare(DelphiLiveRankCandidate? x, DelphiLiveRankCandidate? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return 1;
        if (y is null)
            return -1;

        int comparison = CompareLiveKeys(x, y);
        if (comparison != 0)
            return comparison;

        comparison = x.IsSessionCarryCandidate.CompareTo(y.IsSessionCarryCandidate);
        if (comparison != 0)
            return comparison;
        if (!x.IsSessionCarryCandidate)
        {
            comparison = x.DailySetup!.BestSelectedSourceRank.CompareTo(
                y.DailySetup!.BestSelectedSourceRank);
            if (comparison != 0)
                return comparison;
            comparison = y.DailySetup.CommonDelphiComposite.CompareTo(
                x.DailySetup.CommonDelphiComposite);
            if (comparison != 0)
                return comparison;
        }

        return StringComparer.Ordinal.Compare(x.Symbol, y.Symbol);
    }

    internal static int CompareLiveKeys(
        DelphiLiveRankCandidate x,
        DelphiLiveRankCandidate y)
    {
        int comparison = RankBucket(x.Momentum).CompareTo(RankBucket(y.Momentum));
        if (comparison != 0)
            return comparison;
        comparison = y.Momentum.PositiveLeaningCount.CompareTo(x.Momentum.PositiveLeaningCount);
        if (comparison != 0)
            return comparison;
        comparison = x.Momentum.NegativeLeaningCount.CompareTo(y.Momentum.NegativeLeaningCount);
        if (comparison != 0)
            return comparison;
        return CompareNullableScoreDescending(x.PersistenceScore, y.PersistenceScore);
    }

    private static int CompareNullableScoreDescending(int? x, int? y)
    {
        if (x.HasValue && y.HasValue)
            return y.Value.CompareTo(x.Value);
        if (x.HasValue)
            return -1;
        return y.HasValue ? 1 : 0;
    }

    private static int RankBucket(DelphiLiveMomentumJudgment judgment)
    {
        ArgumentNullException.ThrowIfNull(judgment);
        if (judgment.SupportiveVotes is < 0 or > 4 ||
            judgment.WeakeningVotes is < 0 or > 4 ||
            judgment.SupportiveVotes + judgment.WeakeningVotes > 4 ||
            judgment.PositiveLeaningCount is < 0 or > 4 ||
            judgment.NegativeLeaningCount is < 0 or > 4)
            throw new ArgumentException("Momentum counts are outside the four-family contract.", nameof(judgment));

        return judgment.State switch
        {
            DelphiLiveMomentumState.Strong when judgment.StrongTier == DelphiLiveStrongTier.FourOfFour &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.None => 0,
            DelphiLiveMomentumState.Strong when judgment.StrongTier == DelphiLiveStrongTier.CleanThree &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.None => 1,
            DelphiLiveMomentumState.StrongWithConflict when IsPlain(judgment) => 2,
            DelphiLiveMomentumState.PositiveNudge when IsPlain(judgment) => 3,
            DelphiLiveMomentumState.PositiveNudgeWithConflict when IsPlain(judgment) => 4,
            DelphiLiveMomentumState.Neutral when judgment.StrongTier == DelphiLiveStrongTier.None &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.SupportTilt => 5,
            DelphiLiveMomentumState.Neutral when judgment.StrongTier == DelphiLiveStrongTier.None &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.None => 6,
            DelphiLiveMomentumState.Neutral when judgment.StrongTier == DelphiLiveStrongTier.None &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.Conflict => 7,
            DelphiLiveMomentumState.Neutral when judgment.StrongTier == DelphiLiveStrongTier.None &&
                judgment.NeutralDetail == DelphiLiveNeutralDetail.WeakTilt => 8,
            DelphiLiveMomentumState.MixedConflict when IsPlain(judgment) => 9,
            DelphiLiveMomentumState.NegativeNudgeWithConflict when IsPlain(judgment) => 10,
            DelphiLiveMomentumState.NegativeNudge when IsPlain(judgment) => 11,
            DelphiLiveMomentumState.WeakWithConflict when IsPlain(judgment) => 12,
            DelphiLiveMomentumState.Weak when IsPlain(judgment) => 13,
            DelphiLiveMomentumState.VeryWeak when IsPlain(judgment) => 14,
            _ => throw new ArgumentException("Momentum state, tier, and detail contradict each other.", nameof(judgment))
        };
    }

    private static bool IsPlain(DelphiLiveMomentumJudgment judgment) =>
        judgment.StrongTier == DelphiLiveStrongTier.None &&
        judgment.NeutralDetail == DelphiLiveNeutralDetail.None;
}

public sealed class DelphiLiveLensRankingComparer : IComparer<DelphiLiveRankCandidate>
{
    public DelphiLiveLensRankingComparer(DelphiLiveSourceLens lens)
    {
        if (!Enum.IsDefined(lens))
            throw new ArgumentOutOfRangeException(nameof(lens));
        Lens = lens;
    }

    public DelphiLiveSourceLens Lens { get; }

    public int Compare(DelphiLiveRankCandidate? x, DelphiLiveRankCandidate? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return 1;
        if (y is null)
            return -1;
        int? xRank = x.DailySetup?.RankFor(Lens);
        int? yRank = y.DailySetup?.RankFor(Lens);
        if (!xRank.HasValue || !yRank.HasValue)
        {
            throw new ArgumentException(
                "Lens-specific ranking accepts only current candidates published by that lens.");
        }

        int comparison = DelphiLiveRankingComparer.CompareLiveKeys(x, y);
        if (comparison != 0)
            return comparison;
        comparison = xRank.Value.CompareTo(yRank.Value);
        if (comparison != 0)
            return comparison;
        comparison = y.DailySetup!.CommonDelphiComposite.CompareTo(
            x.DailySetup!.CommonDelphiComposite);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(x.Symbol, y.Symbol);
    }
}

public static class DelphiLiveRanking
{
    public static ImmutableArray<DelphiLiveRankCandidate> Order(
        IEnumerable<DelphiLiveRankCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var ordered = new List<DelphiLiveRankCandidate>();
        foreach (DelphiLiveRankCandidate candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ordered.Add(candidate);
        }
        ordered.Sort(DelphiLiveRankingComparer.Instance);
        return ordered.ToImmutableArray();
    }

    public static ImmutableArray<DelphiLiveRankCandidate> OrderForLens(
        IEnumerable<DelphiLiveRankCandidate> candidates,
        DelphiLiveSourceLens lens)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!Enum.IsDefined(lens))
            throw new ArgumentOutOfRangeException(nameof(lens));
        var ordered = new List<DelphiLiveRankCandidate>();
        foreach (DelphiLiveRankCandidate candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (candidate.DailySetup?.RankFor(lens) is not null)
                ordered.Add(candidate);
        }
        ordered.Sort(new DelphiLiveLensRankingComparer(lens));
        return ordered.ToImmutableArray();
    }
}
