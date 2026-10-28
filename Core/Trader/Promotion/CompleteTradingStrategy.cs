#nullable enable
using Core.Runtime;
using Core.ML.Engine.Profit;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace Core.Trader.Promotion;

public enum TradingStrategyFamily { DailyShadow, DelphiLive }

/// <summary>Public identities only. Artifact locations and credentials are not review data.</summary>
public sealed record TradingModelIdentity(Guid ModelId, string TaskType, string ArtifactSha256);

public sealed record TradingDailyInputs(Guid StrategyVersionId, StrategyConfig Configuration,
    string ModelSetSha256, string InputContract, ImmutableArray<TradingModelIdentity> Models)
{
    public bool Matches(StrategyConfig configuration, ImmutableArray<TradingModelIdentity> observedModels) =>
        configuration == Configuration && !observedModels.IsDefault &&
        observedModels.OrderBy(m => m.TaskType, StringComparer.Ordinal)
            .SequenceEqual(Models.OrderBy(m => m.TaskType, StringComparer.Ordinal));
}

/// <summary>
/// A complete behavior snapshot, separate from the account that tested it and the account that follows it.
/// The existing daily StrategyVersion and engine policy identifiers retain their historical meanings.
/// </summary>
public sealed record CompleteTradingStrategy(Guid StrategyId, Guid VersionId, string Name,
    TradingStrategyFamily Family, TradingDailyInputs Daily, Guid PolicyVersionId,
    SystemShadowPolicyConfig? ShadowPolicy, DelphiLivePolicyDefinition? LivePolicy,
    string Selection, int MaximumHoldings, string ImplementationIdentity, string RecommendationContract)
{
    public const string Contract = "HumanReportedAccountRecommendationsV1";
    public const string ShadowSelectionPrefix = "FrozenDaily:";
    public const string LiveSelection = "DelphiLiveConfirmedRanking";
    public const string MinuteSelection = "DelphiLiveRecoveredMinuteRankingV1";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DelphiLiveMinuteStrategy? MinuteEntry { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string EntryReviewDescription => MinuteEntry is { } minute
        ? $"Two consecutive qualifying one-minute observations; validated backfill can satisfy both without an extra wait. " +
          $"Three of four families; unchanged 20/60/120/180-minute horizons, complete-path VWAP and five-minute protection. " +
          $"Source: {minute.SourceContract}; original signal deadline: bar end + {minute.DeadlineOffset.TotalSeconds:0.###} seconds. " +
          "Collector/source qualification and explicit updated-strategy adoption are required; an old five-minute approval does not authorize this behavior."
        : Family == TradingStrategyFamily.DelphiLive ? "Approved five-minute confirmation and existing recovery rules." : "Approved daily Shadow entry rules.";

    private string LegacyFingerprint => EngineStrategySettings.Hash(JsonSerializer.Serialize(new
    {
        StrategyId, Family, Daily, PolicyVersionId, ShadowPolicy, LivePolicy, Selection, MaximumHoldings,
        ImplementationIdentity, RecommendationContract
    }));
    public string Fingerprint => MinuteEntry is null ? LegacyFingerprint : EngineStrategySettings.Hash(JsonSerializer.Serialize(new
    { Contract = MinuteSelection, Base = LegacyFingerprint, MinuteEntry }));

    public void Validate()
    {
        if (StrategyId == Guid.Empty || VersionId == Guid.Empty || PolicyVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 160 ||
            string.IsNullOrWhiteSpace(ImplementationIdentity) || RecommendationContract != Contract ||
            Daily is null || Daily.Configuration is null || Daily.StrategyVersionId == Guid.Empty ||
            !IsHash(Daily.ModelSetSha256) || string.IsNullOrWhiteSpace(Daily.InputContract) ||
            Daily.Models.IsDefaultOrEmpty || Daily.Models.Length != 4 ||
            Daily.Models.Select(m => m.ModelId).Distinct().Count() != 4 ||
            Daily.Models.Select(m => m.TaskType).Distinct(StringComparer.Ordinal).Count() != 4 ||
            Daily.Models.Any(m => m.ModelId == Guid.Empty || !IsHash(m.ArtifactSha256) ||
                !ProfitModelRegistry.All.Any(definition => definition.TaskType == m.TaskType)))
            throw new ArgumentException("A complete strategy requires exact daily, model, policy and implementation identities.");
        switch (Family)
        {
            case TradingStrategyFamily.DailyShadow:
                if (Selection is not (ShadowSelectionPrefix + "Continuation" or ShadowSelectionPrefix + "Breakout") ||
                    MaximumHoldings is not (3 or 5) || ShadowPolicy is null || LivePolicy is not null || MinuteEntry is not null)
                    throw new ArgumentException("Daily Shadow retains its reviewed lens and Top 3 / Top 5 selections.");
                SystemShadowPolicy.ValidateConfig(ReadShadowPolicy());
                break;
            case TradingStrategyFamily.DelphiLive:
                if (LivePolicy is null || ShadowPolicy is not null)
                    throw new ArgumentException("Delphi Live requires its typed policy without daily Shadow settings.");
                var policy = ReadLivePolicy().Validate();
                MinuteEntry?.Validate();
                if (Selection != (MinuteEntry is null ? LiveSelection : MinuteSelection) ||
                    MinuteEntry is not null && JsonSerializer.Serialize(MinuteEntry.Rules) != JsonSerializer.Serialize(policy) ||
                    MaximumHoldings != policy.MaximumHoldings || PolicyVersionId != policy.PolicyVersionId)
                    throw new ArgumentException("The complete strategy must match its exact Delphi Live policy.");
                break;
            default: throw new ArgumentException("Unsupported complete strategy family.");
        }
    }

    public SystemShadowPolicyConfig ReadShadowPolicy() => Family == TradingStrategyFamily.DailyShadow && ShadowPolicy is not null
        ? ShadowPolicy
        : throw new InvalidOperationException("This strategy does not use daily Shadow policy.");

    public DelphiLivePolicyDefinition ReadLivePolicy() => Family == TradingStrategyFamily.DelphiLive && LivePolicy is not null
        ? LivePolicy
        : throw new InvalidOperationException("This strategy does not use Delphi Live policy.");

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record TradingStrategyAssignment(Guid PromotionId, string AccountLabel,
    CompleteTradingStrategy Strategy, Guid PaperAccountId, Guid EvidenceReviewId,
    Guid? PriorPromotionId, DateTime EffectiveUtc, string Reviewer, string Reason);

/// <summary>Assignment resolution never falls back to a paper champion or the active daily model set.</summary>
public static class TradingStrategyResolution
{
    public static TradingStrategyAssignment? AtBoundary(
        ImmutableArray<TradingStrategyAssignment> history, string accountLabel, DateTime evaluationUtc)
    {
        if (evaluationUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(accountLabel))
            throw new ArgumentException("Resolution requires a real account and UTC evaluation boundary.");
        var applicable = history.Where(a => string.Equals(a.AccountLabel, accountLabel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.EffectiveUtc).ToArray();
        Guid? prior = null;
        DateTime? boundary = null;
        foreach (var assignment in applicable)
        {
            assignment.Strategy.Validate();
            if (assignment.PromotionId == Guid.Empty || assignment.PaperAccountId == Guid.Empty ||
                assignment.EvidenceReviewId == Guid.Empty || assignment.EffectiveUtc.Kind != DateTimeKind.Utc ||
                assignment.PriorPromotionId != prior || assignment.EffectiveUtc <= boundary ||
                string.IsNullOrWhiteSpace(assignment.Reviewer) || string.IsNullOrWhiteSpace(assignment.Reason))
                throw new InvalidOperationException("Trading assignment history is ambiguous or incomplete.");
            prior = assignment.PromotionId;
            boundary = assignment.EffectiveUtc;
        }
        return applicable.LastOrDefault(a => a.EffectiveUtc <= evaluationUtc);
    }
}
