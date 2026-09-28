#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Runtime;
using Core.Oracle;

namespace Core.Trader.Risk;

/// <summary>A coherent book; only a verified current cash-only read can advance time without new prices.</summary>
internal sealed record SystemShadowSavedRiskBook(SystemShadowGenerationInfo Generation,
    SystemShadowRuntimePortfolio Portfolio, SystemShadowRuntimeSession Session,
    ImmutableArray<SystemShadowPositionInfo> Positions, ImmutableArray<ShadowPaperRiskPrice> Prices,
    DateTime UpdatedUtc, DateTime? LastFillUtc, bool IsGateComparison,
    Guid? AssignedVersion, string? SettingsJson, string? SettingsHash, DateTime? AssignedUtc,
    PaperComparisonAccount? Comparison, string? AssignedFamily = null, DateTime? VerifiedReadUtc = null)
{
    private SystemShadowRuntimePortfolio? ResolvePortfolio(Guid requestedPortfolio, DateOnly date)
    {
        if (Portfolio.PortfolioId != requestedPortfolio || DateOnly.FromDateTime(Session.TradingDate) != date ||
            Positions.IsDefault || UpdatedUtc.Kind != DateTimeKind.Utc ||
            IsGateComparison != (Comparison is not null)) return null;
        bool hasAssignment = AssignedVersion is not null || AssignedFamily is not null ||
            SettingsJson is not null || SettingsHash is not null || AssignedUtc is not null;
        if (Comparison is null && hasAssignment && (AssignedVersion is null || AssignedUtc is null)) return null;
        SystemShadowPolicyConfig settings;
        Guid version;
        try
        {
            (version, settings) = AiShadowPolicyIdentity.Read(AssignedVersion, AssignedFamily,
                SettingsJson, SettingsHash, Comparison);
            EngineStrategySettings.Validate(EngineStrategySettings.Shadow, settings);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or InvalidDataException) { return null; }
        return Portfolio with { Settings = settings, StrategyVersionId = version,
            SettingsAssignedUtc = Comparison?.EffectiveUtc ?? AssignedUtc ?? Portfolio.ActivatedUtc, ComparisonAccount = Comparison };
    }

    internal ShadowPaperAccountRead? BuildAccount(Guid requestedPortfolio, DateOnly date)
    {
        var portfolio = ResolvePortfolio(requestedPortfolio, date);
        if (portfolio is null || VerifiedReadUtc is not { Kind: DateTimeKind.Utc } verified ||
            verified < UpdatedUtc || verified < Generation.UpdatedUtc ||
            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(verified)) != date ||
            LastFillUtc is { } fill && (fill.Kind != DateTimeKind.Utc || fill > verified)) return null;
        var observation = new ShadowPaperRiskObservation(Guid.Empty, date, verified, verified, Generation, portfolio, Session,
            Positions.OrderBy(p => p.PositionId).ToImmutableArray(), []);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(observation)));
        return new(observation with { EvidenceId = new Guid(hash.AsSpan(0, 16)) }, UpdatedUtc, LastFillUtc);
    }

    internal ShadowPaperRiskObservation? Build(Guid requestedPortfolio, DateOnly date)
    {
        var portfolio = ResolvePortfolio(requestedPortfolio, date);
        if (portfolio is null || Prices.IsDefault) return null;
        var open = Positions.Where(p => p.Status == "Open").ToArray();
        // A fill timestamp is not a bar start. The monitor also checks every source price against its holding.
        if (open.Length != Prices.Length || open.Any(p => !Prices.Any(b => b.Symbol == p.Symbol))) return null;
        DateTime at = open.Length == 0 ? UpdatedUtc : open.Min(p => p.LastPriceEventUtc).AddMinutes(5);
        DateTime? cashVerified = null;
        if (open.Length == 0 && VerifiedReadUtc is { } verified)
        {
            if (verified.Kind != DateTimeKind.Utc || verified < UpdatedUtc || verified < Generation.UpdatedUtc)
                return null;
            // A present-tense check cannot recreate a historical session value.
            if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(verified)) == date)
            {
                at = verified;
                cashVerified = verified;
            }
        }
        if (open.Any(p => p.LastPriceEventUtc.AddMinutes(5) != at) ||
            LastFillUtc is { } fill && (fill.Kind != DateTimeKind.Utc || fill > at)) return null;
        var observed = new[] { UpdatedUtc, Generation.UpdatedUtc, at }
            .Concat(Prices.Select(p => p.Bar.ReceivedUtc)).Max();
        var result = new ShadowPaperRiskObservation(Guid.Empty, date, at, observed, Generation, portfolio, Session,
            Positions.OrderBy(p => p.PositionId).ToImmutableArray(), Prices.OrderBy(p => p.Symbol, StringComparer.Ordinal).ToImmutableArray())
            { CashVerifiedUtc = cashVerified };
        // Replaying the same receipt is idempotent. Only a new verified cash read advances cash freshness;
        // invested books retain their original bar timestamps and content identity.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result)));
        return result with { EvidenceId = new Guid(hash.AsSpan(0, 16)) };
    }
}
