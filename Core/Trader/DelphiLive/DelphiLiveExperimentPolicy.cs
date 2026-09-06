#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveHypothesisFamily { RawMoveThreshold, RelativeDeadband, VolatilityRuler }
public enum DelphiLiveExperimentPhase { EngineeringShakedown, Discovery, UntouchedConfirmation, PromotionScheduled, ShadowBaseline, Completed, Invalidated }

public sealed record DelphiLiveExperimentDefinition(
    Guid ExperimentId, Guid ChampionPolicyVersionId,
    ImmutableArray<Guid> ChallengerPolicyVersionIds, DelphiLiveHypothesisFamily HypothesisFamily,
    decimal StartingCapital, string Currency, string CodeIdentity);

public sealed record DelphiLiveCohortEvidence(
    DateOnly SessionDate, int CanonicalSessionOrdinal, string Regime,
    int ExpectedOperationalSlots, int UsableOperationalSlots,
    bool HasHostGap, bool HasOverlappingCycle, bool StablePolicyIdentities,
    bool ReconstructibleDecisionsAndFills, bool FiveSessionResearchMature,
    bool CorporateActionUnsupported, bool CapitalChanged,
    ImmutableDictionary<Guid, decimal?> DailyPortfolioReturns,
    ImmutableDictionary<Guid, decimal?> MaximumCheckpointDrawdowns)
{
    public bool EvidenceConflict { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClean => ExpectedOperationalSlots > 0 && UsableOperationalSlots == ExpectedOperationalSlots &&
        !HasHostGap && !HasOverlappingCycle && StablePolicyIdentities && ReconstructibleDecisionsAndFills &&
        !CorporateActionUnsupported && !CapitalChanged && !EvidenceConflict;
}

public sealed record DelphiLivePromotionScore(
    string Status, bool EligibleForHumanReview, int DiscoveryCohorts, int UntouchedCohorts,
    ImmutableArray<DateOnly> EligibleUntouchedDates, uint Seed, int BlockLength, int Resamples,
    decimal? MeanDailyImprovement, decimal? Lower95, decimal? Upper95,
    decimal? ChampionMaximumDrawdown, decimal? ChallengerMaximumDrawdown,
    decimal? ChampionWorstDecileAverage, decimal? ChallengerWorstDecileAverage,
    ImmutableDictionary<string, int> UntouchedRegimeCounts,
    ImmutableArray<string> FailureReasons, string CodeIdentity);

public static class DelphiLiveExperimentPolicy
{
    public static void ValidateDefinition(DelphiLiveExperimentDefinition definition,
        IReadOnlyDictionary<Guid, DelphiLivePolicyDefinition> policies)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.ExperimentId == Guid.Empty || definition.ChampionPolicyVersionId == Guid.Empty ||
            definition.ChallengerPolicyVersionIds.IsDefaultOrEmpty || definition.ChallengerPolicyVersionIds.Length > 2 ||
            definition.ChallengerPolicyVersionIds.Distinct().Count() != definition.ChallengerPolicyVersionIds.Length ||
            definition.ChallengerPolicyVersionIds.Contains(definition.ChampionPolicyVersionId) ||
            definition.StartingCapital <= 0m || definition.Currency.Length != 3 ||
            definition.Currency.Any(c => c is < 'A' or > 'Z') || string.IsNullOrWhiteSpace(definition.CodeIdentity) ||
            !Enum.IsDefined(definition.HypothesisFamily))
            throw new ArgumentException("Experiment requires one champion, one or two distinct contenders, explicit equal capital and immutable code identity.");
        if (!policies.TryGetValue(definition.ChampionPolicyVersionId, out var champion))
            throw new ArgumentException("Champion policy definition is missing.");
        champion.Validate();
        foreach (Guid contender in definition.ChallengerPolicyVersionIds)
        {
            if (!policies.TryGetValue(contender, out var challenger))
                throw new ArgumentException("Challenger policy definition is missing.");
            challenger.Validate();
            ValidateOneFamily(champion, challenger, definition.HypothesisFamily);
        }
        if (definition.ChallengerPolicyVersionIds.Select(id => definition.HypothesisFamily switch
            {
                DelphiLiveHypothesisFamily.RawMoveThreshold => policies[id].SelectedRawMoveThreshold,
                DelphiLiveHypothesisFamily.RelativeDeadband => policies[id].SelectedExcessMoveThreshold,
                _ => policies[id].SelectedRulerSessions
            }).Distinct().Count() != definition.ChallengerPolicyVersionIds.Length)
            throw new ArgumentException("Each predeclared contender must use a distinct value from its named family.");
    }

    public static void ValidateOneFamily(DelphiLivePolicyDefinition champion,
        DelphiLivePolicyDefinition challenger, DelphiLiveHypothesisFamily family)
    {
        champion.Validate(); challenger.Validate();
        bool varies = family switch
        {
            DelphiLiveHypothesisFamily.RawMoveThreshold => champion.SelectedRawMoveThreshold != challenger.SelectedRawMoveThreshold,
            DelphiLiveHypothesisFamily.RelativeDeadband => champion.SelectedExcessMoveThreshold != challenger.SelectedExcessMoveThreshold,
            DelphiLiveHypothesisFamily.VolatilityRuler => champion.SelectedRulerSessions != challenger.SelectedRulerSessions,
            _ => false
        };
        var normalized = challenger with
        {
            PolicyVersionId = champion.PolicyVersionId,
            SelectedRawMoveThreshold = family == DelphiLiveHypothesisFamily.RawMoveThreshold ? champion.SelectedRawMoveThreshold : challenger.SelectedRawMoveThreshold,
            SelectedExcessMoveThreshold = family == DelphiLiveHypothesisFamily.RelativeDeadband ? champion.SelectedExcessMoveThreshold : challenger.SelectedExcessMoveThreshold,
            SelectedRulerSessions = family == DelphiLiveHypothesisFamily.VolatilityRuler ? champion.SelectedRulerSessions : challenger.SelectedRulerSessions
        };
        // Structural JSON comparison covers every current and future setting;
        // reference equality of ImmutableArray would not validate stored copies.
        if (!varies || JsonSerializer.Serialize(champion) != JsonSerializer.Serialize(normalized))
            throw new ArgumentException("Exactly the named predeclared threshold family may differ; safety and all other settings remain identical.");
    }

    public static void ValidateRoleCapacity(Guid champion, IReadOnlyCollection<(Guid PolicyId, string Role)> roles,
        DelphiLiveHypothesisFamily? activeBaselineFamily, DelphiLiveHypothesisFamily? requestedFamily)
    {
        if (champion == Guid.Empty || roles.Count(role => role.Role == "OperationalChampion") != 1 ||
            !roles.Any(role => role.PolicyId == champion && role.Role == "OperationalChampion") ||
            roles.Any(role => role.Role is not ("OperationalChampion" or "ActiveShadowChallenger" or "ShadowBaseline")) ||
            roles.Count(role => role.Role != "OperationalChampion") > 2 ||
            roles.Count(role => role.Role == "ShadowBaseline") > 1 ||
            roles.Select(role => role.PolicyId).Distinct().Count() != roles.Count ||
            (activeBaselineFamily.HasValue && requestedFamily.HasValue && activeBaselineFamily != requestedFamily))
            throw new ArgumentException("One champion and at most two non-champion roles are permitted; an active baseline keeps its hypothesis family exclusive.");
    }

    public static DelphiLivePromotionScore Score(DelphiLiveExperimentDefinition definition, Guid challenger,
        IReadOnlyCollection<DelphiLiveCohortEvidence> discovery,
        IReadOnlyCollection<DelphiLiveCohortEvidence> untouched,
        DelphiLivePolicyDefinition policy)
    {
        policy.Validate();
        if (!definition.ChallengerPolicyVersionIds.Contains(challenger))
            throw new ArgumentException("The selected challenger was not predeclared.");
        ValidateCohorts(discovery); ValidateCohorts(untouched);
        if (discovery.Any(d => untouched.Any(u => u.SessionDate == d.SessionDate)))
            throw new ArgumentException("Discovery and untouched evidence cannot share a market-session cohort.");
        var discovered = discovery.Where(c => IsPairedDiscovery(c, definition)).ToArray();
        var confirmed = untouched.Where(c => IsPaired(c, definition.ChampionPolicyVersionId, challenger))
            .OrderBy(c => c.CanonicalSessionOrdinal).ToArray();
        var regimes = confirmed.GroupBy(c => c.Regime).ToImmutableDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var failures = ImmutableArray.CreateBuilder<string>();
        if (discovered.Length < policy.DiscoverySessionCount || discovered.Length + confirmed.Length < 60)
            failures.Add("InsufficientPerformanceCohorts");
        if (confirmed.Length < policy.UntouchedConfirmationSessionCount)
            failures.Add("InsufficientUntouchedCohorts");
        if (regimes.Count(pair => pair.Value >= 10) < 2)
            failures.Add("InsufficientUntouchedRegimeCoverage");
        uint seed = ExperimentSeed(definition.ExperimentId);
        decimal? mean = null, lower = null, upper = null, championDrawdown = null, challengerDrawdown = null,
            championTail = null, challengerTail = null;
        if (confirmed.Length > 0)
        {
            decimal[] champions = confirmed.Select(c => c.DailyPortfolioReturns[definition.ChampionPolicyVersionId]!.Value).ToArray();
            decimal[] challengers = confirmed.Select(c => c.DailyPortfolioReturns[challenger]!.Value).ToArray();
            decimal[] differences = challengers.Zip(champions, (candidate, control) => candidate - control).ToArray();
            mean = differences.Average();
            championDrawdown = confirmed.Max(c => c.MaximumCheckpointDrawdowns[definition.ChampionPolicyVersionId]!.Value);
            challengerDrawdown = confirmed.Max(c => c.MaximumCheckpointDrawdowns[challenger]!.Value);
            int tailCount = System.Math.Max(1, (int)System.Math.Ceiling(0.10m * confirmed.Length));
            championTail = champions.Order().Take(tailCount).Average();
            challengerTail = challengers.Order().Take(tailCount).Average();
            if (challengerDrawdown > championDrawdown) failures.Add("WorseMaximumCheckpointDrawdown");
            if (challengerTail < championTail) failures.Add("WorseOwnWorstDecileReturn");

            int blockLength = policy.PromotionBootstrapBlockSessionCount;
            int[] starts = Enumerable.Range(0, System.Math.Max(0, confirmed.Length - blockLength + 1))
                .Where(start => Enumerable.Range(1, blockLength - 1).All(offset =>
                    confirmed[start + offset].CanonicalSessionOrdinal == confirmed[start].CanonicalSessionOrdinal + offset))
                .ToArray();
            if (starts.Length == 0) failures.Add("InsufficientConsecutiveBootstrapBlocks");
            else
            {
                decimal[] resamples = new decimal[policy.PromotionBootstrapResampleCount];
                uint random = seed;
                for (int sample = 0; sample < resamples.Length; sample++)
                {
                    decimal sum = 0m;
                    int count = 0;
                    while (count < differences.Length)
                    {
                        int start = starts[(int)(Next(ref random) % (uint)starts.Length)];
                        for (int offset = 0; offset < blockLength && count < differences.Length; offset++, count++)
                            sum += differences[start + offset];
                    }
                    resamples[sample] = sum / differences.Length;
                }
                Array.Sort(resamples);
                decimal tail = (1m - policy.PromotionConfidenceLevel) / 2m;
                lower = Percentile(resamples, tail); upper = Percentile(resamples, 1m - tail);
                if (lower <= 0m) failures.Add("ImprovementIntervalNotAboveZero");
            }
        }
        else failures.Add("NoPairedUntouchedEvidence");
        return new(failures.Count == 0 ? "EligibleForHumanReview" : "NotProvenRetainV1", failures.Count == 0,
            discovered.Length, confirmed.Length, confirmed.Select(c => c.SessionDate).ToImmutableArray(), seed,
            policy.PromotionBootstrapBlockSessionCount, policy.PromotionBootstrapResampleCount,
            mean, lower, upper, championDrawdown, challengerDrawdown, championTail, challengerTail,
            regimes, failures.ToImmutable(), definition.CodeIdentity);
    }

    public static bool IsPaired(DelphiLiveCohortEvidence cohort, Guid champion, Guid challenger) =>
        cohort.IsClean && cohort.FiveSessionResearchMature &&
        cohort.Regime is "Bullish" or "Mixed" or "Bearish" &&
        cohort.DailyPortfolioReturns.TryGetValue(champion, out var control) && control.HasValue &&
        cohort.DailyPortfolioReturns.TryGetValue(challenger, out var contender) && contender.HasValue &&
        cohort.MaximumCheckpointDrawdowns.TryGetValue(champion, out var controlDrawdown) && controlDrawdown.HasValue &&
        cohort.MaximumCheckpointDrawdowns.TryGetValue(challenger, out var contenderDrawdown) && contenderDrawdown.HasValue;

    public static bool IsPairedDiscovery(DelphiLiveCohortEvidence cohort, DelphiLiveExperimentDefinition definition) =>
        definition.ChallengerPolicyVersionIds.All(challenger => IsPaired(cohort, definition.ChampionPolicyVersionId, challenger));

    public static void ValidateCohorts(IEnumerable<DelphiLiveCohortEvidence> cohorts)
    {
        var values = cohorts.ToArray();
        if (values.Select(c => c.SessionDate).Distinct().Count() != values.Length ||
            values.Select(c => c.CanonicalSessionOrdinal).Distinct().Count() != values.Length ||
            values.Any(c => c.CanonicalSessionOrdinal < 0 || c.Regime is not ("Bullish" or "Mixed" or "Bearish" or "Unavailable") ||
                c.ExpectedOperationalSlots < 0 || c.UsableOperationalSlots < 0 || c.UsableOperationalSlots > c.ExpectedOperationalSlots ||
                c.DailyPortfolioReturns.Values.Any(value => value is < -1m) ||
                c.MaximumCheckpointDrawdowns.Values.Any(value => value is < 0m or > 1m)))
            throw new ArgumentException("Cohorts require distinct canonical dates, frozen Delphi regimes, exact coverage and valid return/drawdown fractions.");
    }

    public static uint ExperimentSeed(Guid experimentId)
    {
        if (experimentId == Guid.Empty) throw new ArgumentException("Experiment identity is required.");
        byte[] hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(experimentId.ToString("D")));
        uint seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash);
        return seed == 0 ? 0x9E3779B9u : seed;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state;
    }

    private static decimal Percentile(decimal[] values, decimal fraction)
    {
        decimal index = (values.Length - 1) * fraction;
        int left = (int)decimal.Floor(index), right = (int)decimal.Ceiling(index);
        return values[left] + (values[right] - values[left]) * (index - left);
    }
}
