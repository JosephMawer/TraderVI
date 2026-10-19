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

public sealed record BrokerLiveComparisonSuccessor(Guid AccountId, Guid AssignmentId,
    Core.Trader.DelphiLive.DelphiLivePolicyDefinition OriginalPolicy);

/// <summary>The admission profile belongs to the reviewed real strategy, never a global gate switch.</summary>
public sealed record BrokerManagedStrategy(CompleteTradingStrategy Strategy, Guid PaperAccountId,
    PaperGateProfile GateProfile, PaperComparisonAccount? Comparison)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerLiveComparisonSuccessor? LiveComparisonSuccessor { get; init; }
    [JsonIgnore] public string Fingerprint => LiveComparisonSuccessor is null
        ? Core.WSTrade.Accounts.BrokerAccountImport.Hash(new
        { Contract = "BrokerManagedStrategyV1", Strategy = Strategy.Fingerprint, PaperAccountId, GateProfile, Comparison })
        : Core.WSTrade.Accounts.BrokerAccountImport.Hash(new
        { Contract = "BrokerManagedStrategyV2", Strategy = Strategy.Fingerprint, PaperAccountId, GateProfile, Comparison, LiveComparisonSuccessor });
    [JsonIgnore] public Guid VersionId => StableId(Fingerprint);
    public void Validate()
    {
        Strategy.Validate();
        if (PaperAccountId == Guid.Empty || !Enum.IsDefined(GateProfile))
            throw new ArgumentException("An exact paper source and admission profile are required.");
        if (Comparison is null)
        {
            if (LiveComparisonSuccessor is not null) throw new ArgumentException("A policy successor requires its original comparison source.");
            if (GateProfile != PaperGateProfile.NormalV1)
                throw new ArgumentException("A bypass requires its immutable comparison source.");
            return;
        }
        if (Comparison.AccountId != PaperAccountId || Comparison.Profile != GateProfile ||
            Comparison.DailyStrategyVersionId != Strategy.Daily.StrategyVersionId)
            throw new ArgumentException("The broker strategy must preserve the source's exact account, daily version and admission profile.");
        // A compatible definition is separate from the execution workflow's native launch evidence.
        // Do not reinterpret a comparison preview as an approved real assignment.
        if (Strategy.Family == TradingStrategyFamily.DelphiLive)
        {
            if (GateProfile != PaperGateProfile.NormalV1 || Comparison.StrategyCode != "DelphiLive" ||
                Comparison.ShadowStrategyVersionId is not null || Comparison.ShadowSettingsJson is not null)
                throw new ArgumentException("Delphi Live definition review requires the exact Normal comparison and live policy; bypass profiles remain unsupported.");
            if (Comparison.LivePolicyVersionId != Strategy.PolicyVersionId)
            {
                var successor = LiveComparisonSuccessor;
                if (successor is null || successor.OriginalPolicy is null || successor.AccountId != PaperAccountId || successor.AssignmentId == Guid.Empty ||
                    successor.OriginalPolicy.PolicyVersionId != Comparison.LivePolicyVersionId ||
                    successor.OriginalPolicy.UseProgressiveProfitProtection)
                    throw new ArgumentException("The current live policy requires its exact assigned progressive successor and original policy.");
                ProgressiveProfitProtectionRollout.ValidateChange(successor.OriginalPolicy, Strategy.ReadLivePolicy());
            }
            else if (LiveComparisonSuccessor is not null)
                throw new ArgumentException("An unchanged comparison policy cannot claim a successor assignment.");
            return;
        }
        if (LiveComparisonSuccessor is not null) throw new ArgumentException("Live successor evidence cannot qualify another strategy family.");
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
