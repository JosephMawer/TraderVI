#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;

namespace Core.Oracle;

/// <summary>Strategy choices are paper sources, never the managed account's cash or holdings.</summary>
public sealed record AiPortfolioCatalogEntry(Guid PaperAccountId, string Name, string EngineFamily,
    CompleteTradingStrategy? Strategy, PaperComparisonAccount? Comparison, ImmutableArray<string> UnavailableReasons)
{
    public string Id => PaperAccountId.ToString("D");
    // Include fixed gates and the complete behavior, not display names or random review/version IDs.
    public string Version => Strategy is null ? "unavailable" : EngineStrategySettings.Hash(
        JsonSerializer.Serialize(new { Contract = "AiPortfolioChoiceV1", Strategy = Strategy.Fingerprint, Comparison }));

    public void Validate()
    {
        if (PaperAccountId == Guid.Empty || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(EngineFamily) ||
            UnavailableReasons.IsDefault || UnavailableReasons.Any(string.IsNullOrWhiteSpace) ||
            Strategy is null && UnavailableReasons.IsEmpty)
            throw new InvalidDataException("A catalogue choice needs its identity and an explicit reason when unavailable.");
        if (Strategy is not { } strategy) return;
        strategy.Validate();
        if (EngineFamily != strategy.Family.ToString()) throw new InvalidDataException("Strategy family mismatch.");
        if (Comparison is not { } a) return;
        if (a.AccountId != PaperAccountId || !Enum.IsDefined(a.Profile) || a.BatchId == Guid.Empty || a.PairId == Guid.Empty ||
            a.DailyStrategyVersionId != strategy.Daily.StrategyVersionId ||
            (strategy.Family == TradingStrategyFamily.DelphiLive
                ? a.StrategyCode != "DelphiLive" || a.LivePolicyVersionId != strategy.PolicyVersionId
                : (a.ShadowStrategyVersionId ?? AiShadowPolicyIdentity.LegacyId) != strategy.PolicyVersionId || a.ShadowSettingsJson is null ||
                  a.StrategyCode != strategy.Selection[CompleteTradingStrategy.ShadowSelectionPrefix.Length..] + "Top" + strategy.MaximumHoldings ||
                  EngineStrategySettings.Read(EngineStrategySettings.Shadow, a.ShadowSettingsJson) is not SystemShadowPolicyConfig config || config != strategy.ShadowPolicy ||
                  a.ShadowStrategyVersionId is null && config != SystemShadowPolicyConfig.Version1))
            throw new InvalidDataException("A catalogue variant must retain its exact fixed gates and policy.");
    }
}

/// <summary>Preserves the original built-in policy identity used by existing strategy promotion.</summary>
internal static class AiShadowPolicyIdentity
{
    internal static Guid LegacyId => TradingRecommendationStorage.Id(SystemShadowVersions.Policy);

    internal static (Guid Id, SystemShadowPolicyConfig Policy) Read(Guid? assignedVersion, string? family,
        string? settingsJson, string? settingsHash, PaperComparisonAccount? comparison)
    {
        if (comparison is not null)
        {
            var pinned = (SystemShadowPolicyConfig)EngineStrategySettings.Read(EngineStrategySettings.Shadow,
                comparison.ShadowSettingsJson ?? throw new InvalidDataException("Missing pinned Shadow policy."));
            if (comparison.ShadowStrategyVersionId == Guid.Empty ||
                comparison.ShadowStrategyVersionId is null && pinned != SystemShadowPolicyConfig.Version1)
                throw new InvalidDataException("Unversioned Shadow policy differs from frozen V1.");
            return (comparison.ShadowStrategyVersionId ?? LegacyId, pinned);
        }
        // No assignment is the engine's original V1 contract, not the template for new strategies.
        if (assignedVersion is null && family is null && settingsJson is null && settingsHash is null)
            return (LegacyId, SystemShadowPolicyConfig.Version1);
        if (assignedVersion is null || assignedVersion == Guid.Empty || family != EngineStrategySettings.Shadow ||
            settingsJson is null || settingsHash != EngineStrategySettings.Hash(settingsJson))
            throw new InvalidDataException("Assigned Shadow policy is incomplete or its checksum differs.");
        return (assignedVersion.Value, (SystemShadowPolicyConfig)EngineStrategySettings.Read(EngineStrategySettings.Shadow, settingsJson));
    }
}

public sealed record AiPortfolioCatalog(DateTimeOffset AsOfUtc, DateTimeOffset ExpiresUtc,
    ImmutableArray<AiPortfolioCatalogEntry> Entries);

public interface IAiPortfolioCatalogReader
{
    Task<AiPortfolioCatalog> ReadAsync(CancellationToken cancellationToken = default);
}
