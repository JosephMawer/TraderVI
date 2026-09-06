#nullable enable

using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveOfficialRunSource(
    Guid RunId,
    Guid StrategyVersionId,
    string Purpose,
    string AuditState,
    DateOnly RecommendationDate,
    DateOnly MarketDataAsOf,
    DateTime StartedUtc,
    DateTime CreatedUtc);

public sealed record DelphiLiveCandidateSource(
    Guid CandidateId,
    Guid RunId,
    string Symbol,
    decimal CommonComposite,
    string CandidateSnapshotJson);

public sealed record DelphiLiveLensSource(
    Guid LensEvaluationId,
    Guid CandidateId,
    string Lens,
    bool Eligible,
    bool Published,
    int? Rank,
    decimal? RankingKey,
    string? FirstFailure,
    string GateTraceJson);

public sealed record DelphiLiveFrozenCandidate(
    Guid CandidateId,
    string Symbol,
    decimal CommonComposite,
    string CandidateSnapshotJson,
    IReadOnlyList<DelphiLiveLensSource> SourceLenses)
{
    public int BestSourceRank => SourceLenses.Min(x => x.Rank!.Value);
}

public sealed record DelphiLiveFrozenWatchlist(
    DelphiLiveOfficialRunSource? Run,
    string Status,
    IReadOnlyList<DelphiLiveFrozenCandidate> Candidates)
{
    public bool AllowsNewRisk => Run is not null && Status == "Frozen";
}

public static class DelphiLiveFrozenSourceSelector
{
    private static readonly HashSet<string> AcceptedLenses =
        new(StringComparer.OrdinalIgnoreCase) { "Continuation", "Breakout" };

    public static DelphiLiveFrozenWatchlist Freeze(
        DateOnly tradingDate,
        DateOnly expectedImmediatelyPrecedingXiuSession,
        DateTime freezeBoundaryUtc,
        IEnumerable<DelphiLiveOfficialRunSource> runs,
        IEnumerable<DelphiLiveCandidateSource> candidates,
        IEnumerable<DelphiLiveLensSource> lenses)
    {
        RequireUtc(freezeBoundaryUtc, nameof(freezeBoundaryUtc));
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(lenses);

        List<DelphiLiveOfficialRunSource> materializedRuns = runs.ToList();
        foreach (DelphiLiveOfficialRunSource run in materializedRuns)
            ValidateRun(run);
        if (materializedRuns.Select(x => x.RunId).Distinct().Count() != materializedRuns.Count)
            throw new ArgumentException("Official-run source contains duplicate identities.", nameof(runs));

        DelphiLiveOfficialRunSource? selected = materializedRuns
            .Where(run =>
                run.Purpose == "OfficialPaper" &&
                run.AuditState == "Valid" &&
                run.RecommendationDate == tradingDate &&
                run.MarketDataAsOf == expectedImmediatelyPrecedingXiuSession &&
                run.CreatedUtc <= freezeBoundaryUtc)
            .OrderByDescending(run => run.CreatedUtc)
            .ThenByDescending(run => run.StartedUtc)
            .ThenBy(run => new SqlGuid(run.RunId))
            .FirstOrDefault();
        if (selected is null)
            return new(null, "NoValidDelphiRun", Array.Empty<DelphiLiveFrozenCandidate>());

        List<DelphiLiveCandidateSource> selectedCandidates = candidates
            .Where(candidate => candidate.RunId == selected.RunId)
            .ToList();
        foreach (DelphiLiveCandidateSource candidate in selectedCandidates)
            ValidateCandidate(candidate);
        if (selectedCandidates.Select(x => x.CandidateId).Distinct().Count() != selectedCandidates.Count ||
            selectedCandidates.Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                selectedCandidates.Count)
            throw new ArgumentException("Selected run contains duplicate candidate identity or symbol.", nameof(candidates));

        Dictionary<Guid, DelphiLiveCandidateSource> candidateById =
            selectedCandidates.ToDictionary(candidate => candidate.CandidateId);
        List<DelphiLiveLensSource> selectedLenses = lenses
            .Where(lens => candidateById.ContainsKey(lens.CandidateId))
            .ToList();
        foreach (DelphiLiveLensSource lens in selectedLenses)
            ValidateLens(lens);
        if (selectedLenses.Select(x => x.LensEvaluationId).Distinct().Count() != selectedLenses.Count ||
            selectedLenses.GroupBy(x => (x.CandidateId, x.Lens.ToUpperInvariant())).Any(group => group.Count() != 1))
            throw new ArgumentException("Selected source contains duplicate lens evaluation.", nameof(lenses));

        var frozen = new List<DelphiLiveFrozenCandidate>();
        foreach (DelphiLiveCandidateSource candidate in selectedCandidates)
        {
            List<DelphiLiveLensSource> published = selectedLenses
                .Where(lens =>
                    lens.CandidateId == candidate.CandidateId &&
                    lens.Eligible &&
                    lens.Published &&
                    lens.Rank is >= 1 and <= 25 &&
                    AcceptedLenses.Contains(lens.Lens))
                .OrderBy(lens => lens.Lens, StringComparer.Ordinal)
                .ToList();
            if (published.Count == 0)
                continue;
            frozen.Add(new(
                candidate.CandidateId,
                candidate.Symbol,
                candidate.CommonComposite,
                candidate.CandidateSnapshotJson,
                published.AsReadOnly()));
        }

        frozen.Sort((left, right) =>
        {
            int rank = left.BestSourceRank.CompareTo(right.BestSourceRank);
            return rank != 0
                ? rank
                : StringComparer.OrdinalIgnoreCase.Compare(left.Symbol, right.Symbol);
        });
        if (frozen.Count > 50)
            throw new InvalidOperationException("The deduplicated Top-25 lens union cannot exceed fifty symbols.");
        return new(selected, "Frozen", frozen.AsReadOnly());
    }

    private static void ValidateRun(DelphiLiveOfficialRunSource run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunId == Guid.Empty || run.StrategyVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(run.Purpose) || string.IsNullOrWhiteSpace(run.AuditState))
            throw new ArgumentException("Run identity, purpose, and audit state are required.", nameof(run));
        RequireUtc(run.StartedUtc, nameof(run.StartedUtc));
        RequireUtc(run.CreatedUtc, nameof(run.CreatedUtc));
        if (run.CreatedUtc < run.StartedUtc)
            throw new ArgumentException("Run creation cannot precede its start.", nameof(run));
    }

    private static void ValidateCandidate(DelphiLiveCandidateSource candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.CandidateId == Guid.Empty || candidate.RunId == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.Symbol) || candidate.Symbol.Length > 20 ||
            string.IsNullOrWhiteSpace(candidate.CandidateSnapshotJson))
            throw new ArgumentException("Candidate identity, symbol, and snapshot are required.", nameof(candidate));
    }

    private static void ValidateLens(DelphiLiveLensSource lens)
    {
        ArgumentNullException.ThrowIfNull(lens);
        if (lens.LensEvaluationId == Guid.Empty || lens.CandidateId == Guid.Empty ||
            string.IsNullOrWhiteSpace(lens.Lens) || string.IsNullOrWhiteSpace(lens.GateTraceJson))
            throw new ArgumentException("Lens identity and gate trace are required.", nameof(lens));
        if (lens.Rank.HasValue && lens.Rank.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(lens.Rank));
        if (lens.Published && (!lens.Eligible || !lens.Rank.HasValue || !lens.RankingKey.HasValue))
            throw new ArgumentException("Published lens evidence must be eligible and ranked.", nameof(lens));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
