#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;
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
                : a.ShadowStrategyVersionId != strategy.PolicyVersionId || a.ShadowSettingsJson is null ||
                  a.StrategyCode != strategy.Selection[CompleteTradingStrategy.ShadowSelectionPrefix.Length..] + "Top" + strategy.MaximumHoldings ||
                  EngineStrategySettings.Read(EngineStrategySettings.Shadow, a.ShadowSettingsJson) is not SystemShadowPolicyConfig config || config != strategy.ShadowPolicy))
            throw new InvalidDataException("A catalogue variant must retain its exact fixed gates and policy.");
    }
}

public sealed record AiPortfolioCatalog(DateTimeOffset AsOfUtc, DateTimeOffset ExpiresUtc,
    ImmutableArray<AiPortfolioCatalogEntry> Entries);

public interface IAiPortfolioCatalogReader
{
    Task<AiPortfolioCatalog> ReadAsync(CancellationToken cancellationToken = default);
}
