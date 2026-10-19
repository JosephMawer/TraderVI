#nullable enable
using Core.Runtime;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Trader.Promotion;

/// <summary>The admission profile belongs to the reviewed real strategy, never a global gate switch.</summary>
public sealed record BrokerManagedStrategy(CompleteTradingStrategy Strategy, Guid PaperAccountId,
    PaperGateProfile GateProfile, PaperComparisonAccount? Comparison)
{
    [JsonIgnore] public string Fingerprint => Core.WSTrade.Accounts.BrokerAccountImport.Hash(new
        { Contract = "BrokerManagedStrategyV1", Strategy = Strategy.Fingerprint, PaperAccountId, GateProfile, Comparison });
    [JsonIgnore] public Guid VersionId => StableId(Fingerprint);
    public void Validate()
    {
        Strategy.Validate();
        if (PaperAccountId == Guid.Empty || !Enum.IsDefined(GateProfile))
            throw new ArgumentException("An exact paper source and admission profile are required.");
        if (Comparison is null)
        {
            if (GateProfile != PaperGateProfile.NormalV1)
                throw new ArgumentException("A bypass requires its immutable comparison source.");
            return;
        }
        if (Comparison.AccountId != PaperAccountId || Comparison.Profile != GateProfile ||
            Comparison.DailyStrategyVersionId != Strategy.Daily.StrategyVersionId)
            throw new ArgumentException("The broker strategy must preserve the source's exact account, daily version and admission profile.");
        // Definition review only: the execution workflow still rejects Delphi Live until its
        // execution/protection adapter is qualified. Do not reinterpret a comparison as approval.
        if (Strategy.Family == TradingStrategyFamily.DelphiLive)
        {
            if (GateProfile != PaperGateProfile.NormalV1 || Comparison.StrategyCode != "DelphiLive" ||
                Comparison.LivePolicyVersionId != Strategy.PolicyVersionId ||
                Comparison.ShadowStrategyVersionId is not null || Comparison.ShadowSettingsJson is not null)
                throw new ArgumentException("Delphi Live definition review requires the exact Normal comparison and live policy; bypass profiles remain unsupported.");
            return;
        }
        if (Strategy.Family != TradingStrategyFamily.DailyShadow ||
            Strategy.PolicyVersionId != (Comparison.ShadowStrategyVersionId ?? StableId(SystemShadowVersions.Policy)) ||
            Comparison.StrategyCode != Strategy.Selection[CompleteTradingStrategy.ShadowSelectionPrefix.Length..] + "Top" + Strategy.MaximumHoldings ||
            Comparison.ShadowSettingsJson is null ||
            JsonSerializer.Deserialize<SystemShadowPolicyConfig>(Comparison.ShadowSettingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) != Strategy.ShadowPolicy)
            throw new ArgumentException("The broker strategy must preserve the source's exact daily, lens, size and policy identities.");
    }
    public bool Admits(bool published, int? rank, bool normallyEligible) =>
        PaperGateComparison.Admits(GateProfile, published, rank, normallyEligible, Strategy.MaximumHoldings);
    internal static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}

public sealed record BrokerStrategyReview(BrokerManagedStrategy Strategy, TradingPaperEvidence Evidence,
    DateTime PreparedUtc, ImmutableArray<string> Blocks);
