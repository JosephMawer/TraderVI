#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>Immutable admission choices. Neither option changes portfolio or data-safety rules.</summary>
public enum PaperGateProfile { NormalV1, PortfolioRulesOnlyV1 }

public sealed record PaperComparisonAccount(Guid AccountId, Guid BatchId, Guid PairId,
    string StrategyCode, PaperGateProfile Profile, DateTime EffectiveUtc, Guid DailyStrategyVersionId,
    Guid? LivePolicyVersionId, Guid? ShadowStrategyVersionId, string? ShadowSettingsJson)
{
    public string ProfileName => PaperGateComparison.ProfileName(Profile);
    public string DisplayName => $"{StrategyCode} · {ProfileName} · {PairId.ToString("N")[..8]}";
}

public sealed record PaperComparisonSource(Guid RevisionId, Guid BatchId, Guid SessionId,
    Guid RunId, Guid DailyStrategyVersionId, DateTime PublishedUtc, DateTime AdoptedUtc, DateTime EffectiveUtc,
    IReadOnlyList<DelphiLiveFrozenCandidate> Candidates,
    IReadOnlyDictionary<string, DelphiLiveFrozenBaseline> Baselines);

/// <summary>Persisted with a decision and original entry, separate from the official gate facts.</summary>
public sealed record PaperGateAdmission(Guid AccountId, Guid PairId, Guid BatchId, PaperGateProfile Profile,
    Guid SourceRevisionId, Guid RunId, Guid DailyStrategyVersionId, Guid PolicyVersionId, DateTime EffectiveUtc)
{
    public void Validate(Guid? run, Guid? strategy, Guid policy, DateTime decisionUtc)
    {
        if (AccountId == Guid.Empty || PairId == Guid.Empty || BatchId == Guid.Empty || SourceRevisionId == Guid.Empty ||
            RunId == Guid.Empty || DailyStrategyVersionId == Guid.Empty || PolicyVersionId == Guid.Empty ||
            !Enum.IsDefined(Profile) || RunId != run || DailyStrategyVersionId != strategy || PolicyVersionId != policy ||
            EffectiveUtc.Kind != DateTimeKind.Utc || decisionUtc.Kind != DateTimeKind.Utc || decisionUtc < EffectiveUtc)
            throw new ArgumentException("Comparison admission requires matching immutable account, policy and prospective source evidence.");
    }
}

public static class PaperGateComparison
{
    public const decimal InitialCapital = 1000m;
    public const string LiveRole = "GateComparison";
    public static IReadOnlyList<string> StrategyCodes { get; } =
        ["ContinuationTop3", "ContinuationTop5", "BreakoutTop3", "BreakoutTop5", "DelphiLive"];

    public static string ProfileName(PaperGateProfile profile) => profile switch
    {
        PaperGateProfile.NormalV1 => "Normal",
        PaperGateProfile.PortfolioRulesOnlyV1 => "Portfolio rules only",
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public static bool Admits(PaperGateProfile profile, bool published, int? rank, bool normallyEligible, int maximumRank)
    {
        if (!Enum.IsDefined(profile) || maximumRank is < 1 or > 25)
            throw new ArgumentOutOfRangeException(nameof(profile));
        return published && rank >= 1 && rank <= maximumRank &&
            (normallyEligible || profile == PaperGateProfile.PortfolioRulesOnlyV1);
    }

    public static IReadOnlyDictionary<string, DelphiLiveFrozenCandidate> SelectLive(
        PaperComparisonSource source, PaperGateProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        var selected = source.Candidates.Select(c => c with
        {
            SourceLenses = c.SourceLenses.Where(l => Admits(profile, l.Published, l.Rank, l.Eligible, 25)).ToArray()
        }).Where(c => c.SourceLenses.Count > 0).ToArray();
        if (selected.Length > 50 || selected.Any(c => c.CandidateId == Guid.Empty || c.Symbol == "XIU") ||
            selected.Select(c => c.Symbol).Distinct(StringComparer.Ordinal).Count() != selected.Length ||
            selected.SelectMany(c => c.SourceLenses).GroupBy(l => (l.Lens, l.Rank)).Any(g => g.Count() > 1))
            throw new ArgumentException("Comparison source contains invalid or duplicate published identities.");
        return selected.ToDictionary(c => c.Symbol, StringComparer.Ordinal);
    }

    public static PaperGateAdmission Admission(PaperComparisonAccount account, PaperComparisonSource source, Guid policy)
    {
        if (source.BatchId != account.BatchId || source.DailyStrategyVersionId != account.DailyStrategyVersionId ||
            account.LivePolicyVersionId != policy)
            throw new ArgumentException("Comparison source and account assignments differ.");
        return new(account.AccountId, account.PairId, account.BatchId, account.Profile, source.RevisionId,
            source.RunId, source.DailyStrategyVersionId, policy,
            source.EffectiveUtc > account.EffectiveUtc ? source.EffectiveUtc : account.EffectiveUtc);
    }
}
