#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Core.Oracle;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Trader.Promotion;

namespace Core.Trader.Risk;

public sealed record ShadowPaperRiskPrice(string Symbol, TradingPriceBar Bar)
{
    public override string ToString() => "Shadow price evidence (financial data omitted)";
}

/// <summary>
/// A producer must read the complete generation/portfolio/session/position book coherently at At,
/// including closed positions, and attach the actual source bars. Independent UI queries and their
/// FreshestPriceEventUtc summary do not establish this contract.
/// </summary>
public sealed record ShadowPaperRiskObservation(Guid EvidenceId, DateOnly TradingDate, DateTime At,
    DateTime ObservedUtc, SystemShadowGenerationInfo Generation, SystemShadowRuntimePortfolio Portfolio,
    SystemShadowRuntimeSession Session, ImmutableArray<SystemShadowPositionInfo> Positions,
    ImmutableArray<ShadowPaperRiskPrice> Prices)
{
    public override string ToString() => "Shadow paper risk observation (financial data omitted)";
}

/// <summary>Offline complete-book adapter. Saved source guards remain independent new-buy blocks.</summary>
public sealed class SystemShadowAiPortfolioRiskMonitor
{
    public const string ValuationContract = "ShadowPaperExactFiveMinuteBookV1";
    private readonly AiRiskJournalScope scope;
    private readonly AiPortfolioRiskObservationWriter writer;
    private readonly TimeSpan maximumEvidenceAge;
    internal Guid PortfolioId => scope.PortfolioId;

    public SystemShadowAiPortfolioRiskMonitor(AiPortfolioRiskJournal journal, AiPortfolioRiskPolicy policy, TimeSpan maximumEvidenceAge)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(policy);
        scope = journal.Scope;
        if (scope.Mode != AiRiskAccountMode.Paper || scope.AccountScopeId != scope.PortfolioId || maximumEvidenceAge <= TimeSpan.Zero)
            throw new ArgumentException("A paper-account journal and explicitly qualified evidence age are required.");
        writer = new(journal, policy);
        this.maximumEvidenceAge = maximumEvidenceAge;
    }

    public AiRiskObservationResult Start(ShadowPaperRiskObservation? observation, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        writer.Apply(previous => Qualify(scope.PortfolioId, observation, previous, maximumEvidenceAge, now), now, hasFreshAiReview,
            WithSavedHolds(observation, independentBlocks), initialize: true);

    public AiRiskObservationResult Observe(ShadowPaperRiskObservation? observation, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        writer.Apply(previous => Qualify(scope.PortfolioId, observation, previous, maximumEvidenceAge, now), now, hasFreshAiReview,
            WithSavedHolds(observation, independentBlocks), initialize: false);

    private static ImmutableArray<string> WithSavedHolds(ShadowPaperRiskObservation? o, ImmutableArray<string> blocks)
    {
        if (blocks.IsDefault) throw new ArgumentException("Independent checks must be supplied.", nameof(blocks));
        if (o?.Session?.DailyLossGuardActive == true) blocks = blocks.Add("DailyBuyingPaused");
        if (o?.Portfolio is { } p && p.Status != "Active") blocks = blocks.Add("PaperPortfolioNotActive");
        if (o?.Generation is { } g && g.Status != SystemShadowGenerationStatus.Active) blocks = blocks.Add("PaperGenerationNotActive");
        if (o?.Portfolio?.Status == "CapitalReviewRequired" || o?.Generation?.Status == SystemShadowGenerationStatus.CapitalReviewRequired)
            blocks = blocks.Add("CapitalReviewRequired");
        return blocks;
    }

    private static bool MatchesLegacyComparison(PaperComparisonAccount account, SystemShadowPolicyConfig settings)
    {
        try { return AiShadowPolicyIdentity.Read(null, null, null, null, account).Policy == settings; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or JsonException)
        { return false; }
    }

    internal static PortfolioRiskValuation Qualify(Guid portfolioId, ShadowPaperRiskObservation? o,
        PortfolioRiskValuation? previous, TimeSpan maximumEvidenceAge, DateTimeOffset now)
    {
        if (o is null || o.Generation is not { } g || o.Portfolio is not { } p || o.Session is not { } s ||
            o.EvidenceId == Guid.Empty || o.At.Kind != DateTimeKind.Utc || o.ObservedUtc.Kind != DateTimeKind.Utc ||
            o.At > o.ObservedUtc || o.ObservedUtc > now.UtcDateTime || o.Positions.IsDefault || o.Prices.IsDefault ||
            p.PortfolioId != portfolioId || g.GenerationId == Guid.Empty || p.GenerationId != g.GenerationId ||
            s.PortfolioId != p.PortfolioId || s.SessionId == Guid.Empty || DateOnly.FromDateTime(s.TradingDate) != o.TradingDate ||
            g.TotalAccountValue <= 0m || p.CashBalance < 0m || !Enum.IsDefined(g.Status) ||
            g.ActivatedUtc is not { } activated || activated.Kind != DateTimeKind.Utc || activated > o.At ||
            g.UpdatedUtc.Kind != DateTimeKind.Utc || g.UpdatedUtc > o.ObservedUtc ||
            p.Status is not ("Active" or "Paused" or "CapitalReviewRequired" or "Stopped") ||
            p.ActivatedUtc.Kind != DateTimeKind.Utc || p.ActivatedUtc > o.At ||
            string.IsNullOrWhiteSpace(p.PortfolioCode) || string.IsNullOrWhiteSpace(g.PolicyVersion) || p.Settings is null ||
            p.StrategyVersionId == Guid.Empty || p.SettingsAssignedUtc is { } assigned &&
                (assigned.Kind != DateTimeKind.Utc || assigned > o.At))
            throw new InvalidOperationException("A complete owned, dated paper book is required.");
        if (p.ComparisonAccount is { } a && (a.AccountId != p.PortfolioId ||
            (a.ShadowStrategyVersionId ?? AiShadowPolicyIdentity.LegacyId) != p.StrategyVersionId ||
            a.ShadowStrategyVersionId is null && !MatchesLegacyComparison(a, p.Settings) ||
            a.StrategyCode != p.PortfolioCode || !Enum.IsDefined(a.Profile) || a.BatchId == Guid.Empty ||
            a.PairId == Guid.Empty || a.DailyStrategyVersionId == Guid.Empty))
            throw new InvalidOperationException("The gate variant must retain its exact account and strategy identity.");
        if (o.Positions.Any(h => h is null || h.PortfolioId != p.PortfolioId || h.PositionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(h.Symbol) || h.Shares <= 0 || h.CostBasis <= 0m || h.AverageCost <= 0m ||
                h.EntryUtc.Kind != DateTimeKind.Utc || h.EntryUtc > o.At ||
                h.Status is not ("Open" or "Closed") ||
                (h.Status == "Open" ? h.ExitUtc is not null || h.RealizedProfitLoss != 0m :
                    h.ExitUtc is not { } exit || exit.Kind != DateTimeKind.Utc || exit < h.EntryUtc || exit > o.At)) ||
            o.Positions.Select(h => h.PositionId).Distinct().Count() != o.Positions.Length)
            throw new InvalidOperationException("Paper position ownership and lifecycle must be complete.");
        var open = o.Positions.Where(h => h.Status == "Open").ToArray();
        if (open.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != open.Length ||
            o.Prices.Length != open.Length || o.Prices.Any(x => x is null || x.Bar is null || string.IsNullOrWhiteSpace(x.Symbol)) ||
            o.Prices.Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != open.Length ||
            o.Prices.Select(x => x.Bar.EvidenceId).Distinct().Count() != open.Length)
            throw new InvalidOperationException("Every held symbol needs unambiguous source price evidence.");
        decimal value = p.CashBalance;
        foreach (var h in open)
        {
            var price = o.Prices.SingleOrDefault(x => string.Equals(x.Symbol, h.Symbol, StringComparison.OrdinalIgnoreCase));
            if (price is null) throw new InvalidOperationException("A held symbol has no price evidence.");
            var b = price.Bar;
            // Shadow's LastPriceEventUtc is a bar START for monitor updates, but a fill timestamp
            // during fills. Require the actual matching completed bar before interpreting it.
            if (b.EvidenceId == Guid.Empty || b.Minutes != 5 || b.StartUtc.Kind != DateTimeKind.Utc ||
                b.StartUtc.Ticks % TimeSpan.FromMinutes(5).Ticks != 0 || b.StartUtc.AddMinutes(5) != o.At ||
                b.ReceivedUtc.Kind != DateTimeKind.Utc || b.ReceivedUtc < o.At || b.ReceivedUtc > o.ObservedUtc ||
                h.LastPriceEventUtc.Kind != DateTimeKind.Utc || h.LastPriceEventUtc != b.StartUtc || h.LastPrice != b.Close ||
                b.Low <= 0m || b.High < b.Low || b.Open < b.Low || b.Open > b.High || b.Close < b.Low || b.Close > b.High)
                throw new InvalidOperationException("Stored marks must match exact completed five-minute source bars.");
            value += h.Shares * b.Close;
        }
        // Fixed paper capital; original costs already include the engine's friction. Do not infer
        // real-account flows from this equation or re-price historical acquisition costs.
        if (p.CashBalance != g.TotalAccountValue + o.Positions.Where(h => h.Status == "Closed").Sum(h => h.RealizedProfitLoss)
                - open.Sum(h => h.CostBasis))
            throw new InvalidOperationException("Cash must reconcile to fixed capital and the complete position history.");

        if (previous is not null)
        {
            if (previous.Source?.Contract != ValuationContract)
                throw new InvalidOperationException("Changing valuation families requires reviewed handover evidence.");
            ShadowPaperRiskObservation prior;
            try { prior = JsonSerializer.Deserialize<ShadowPaperRiskObservation>(previous.Source.EvidenceJson) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("Saved paper source evidence is invalid."); }
            if (prior.Generation is null || prior.Portfolio is null || prior.Positions.IsDefault ||
                prior.Positions.Any(h => h is null) ||
                prior.Generation.GenerationId != g.GenerationId || prior.Generation.TotalAccountValue != g.TotalAccountValue ||
                o.ObservedUtc < prior.ObservedUtc || prior.Portfolio.ComparisonAccount != p.ComparisonAccount ||
                prior.Positions.Any(old => !o.Positions.Any(h => h.PositionId == old.PositionId &&
                    (old.Status == "Closed" ? h == old : h.Symbol == old.Symbol && h.EntryUtc == old.EntryUtc && h.Shares >= old.Shares))))
                throw new InvalidOperationException("Paper capital, generation and completed history cannot be replaced.");
        }
        var canonical = o with { Positions = o.Positions.OrderBy(h => h.PositionId).ToImmutableArray(),
            Prices = o.Prices.OrderBy(x => x.Symbol, StringComparer.Ordinal).ToImmutableArray() };
        string json = JsonSerializer.Serialize(canonical);
        var at = new DateTimeOffset(o.At);
        return new(p.PortfolioId, o.EvidenceId, o.TradingDate, at, at.Add(maximumEvidenceAge), value, true, true)
        {
            Source = new(ValuationContract, json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))))
        };
    }
}
