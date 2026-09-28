#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Trader.DelphiLive;

namespace Core.Trader.Risk;

/// <summary>
/// Offline paper-ledger adapter. Only the managed paper account's own snapshot may supply value;
/// candidate portfolios are strategy choices, never substitutes for that account's holdings/cash.
/// The producer must load a complete authoritative snapshot. No ledger writes, collection or orders.
/// </summary>
public sealed class DelphiLiveAiPortfolioRiskMonitor
{
    public const string ValuationContract = "DelphiLivePaperExactCheckpointV1";
    private sealed record Held(Guid PositionId, string Symbol, int Quantity, decimal Cost,
        DateTime OpenedUtc, Guid EntryActionId, DateTime? ClosedUtc, Guid? ExitActionId);
    private sealed record Source(Guid GenerationId, Guid PolicyVersionId, string Role, PaperComparisonAccount? ComparisonAccount,
        long Revision, decimal StartingCapital, DateTime UpdatedUtc, decimal Cash,
        ImmutableArray<Held> Positions, ImmutableArray<DelphiLiveLedgerFill> Fills, DelphiLiveLedgerMark Mark);
    private readonly AiRiskJournalScope scope;
    private readonly AiPortfolioRiskObservationWriter writer;
    private readonly TimeSpan maximumEvidenceAge;
    internal Guid PortfolioId => scope.PortfolioId;

    public DelphiLiveAiPortfolioRiskMonitor(AiPortfolioRiskJournal journal, AiPortfolioRiskPolicy policy,
        TimeSpan maximumEvidenceAge)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(policy);
        scope = journal.Scope;
        if (scope.Mode != AiRiskAccountMode.Paper || scope.AccountScopeId != scope.PortfolioId || maximumEvidenceAge <= TimeSpan.Zero)
            throw new ArgumentException("A paper-account journal and explicitly qualified evidence age are required.");
        writer = new(journal, policy);
        this.maximumEvidenceAge = maximumEvidenceAge;
    }

    public AiRiskObservationResult Start(DelphiLivePortfolioSnapshot? snapshot, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        writer.Apply(previous => Qualify(scope.PortfolioId, snapshot, previous, maximumEvidenceAge, now), now, hasFreshAiReview,
            WithSavedHolds(snapshot, independentBlocks), initialize: true);

    public AiRiskObservationResult Observe(DelphiLivePortfolioSnapshot? snapshot, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        writer.Apply(previous => Qualify(scope.PortfolioId, snapshot, previous, maximumEvidenceAge, now), now, hasFreshAiReview,
            WithSavedHolds(snapshot, independentBlocks), initialize: false);

    private static ImmutableArray<string> WithSavedHolds(DelphiLivePortfolioSnapshot? snapshot, ImmutableArray<string> blocks)
    {
        if (blocks.IsDefault) throw new ArgumentException("Independent checks must be supplied.", nameof(blocks));
        // Preserve saved engine holds until the explicit AI/ordinary engine composition is implemented.
        if (snapshot?.Guards?.DailyBuyingPaused == true) blocks = blocks.Add("DailyBuyingPaused");
        if (snapshot?.Guards?.CapitalReviewRequired == true) blocks = blocks.Add("CapitalReviewRequired");
        return blocks;
    }

    internal static PortfolioRiskValuation Qualify(Guid portfolioId, DelphiLivePortfolioSnapshot? p,
        PortfolioRiskValuation? previous, TimeSpan maximumEvidenceAge, DateTimeOffset now)
    {
        if (p is null || p.PortfolioId != portfolioId || p.GenerationId == Guid.Empty || p.PolicyVersionId == Guid.Empty ||
            p.Currency != "CAD" || p.StartingCapital <= 0m || p.Cash < 0m || p.Revision < 0 || p.Guards is null ||
            p.UpdatedUtc.Kind != DateTimeKind.Utc || p.UpdatedUtc > now.UtcDateTime ||
            p.Positions.IsDefault || p.Fills.IsDefault || p.Marks.IsDefaultOrEmpty)
            throw new InvalidOperationException("A complete owned paper ledger and latest checkpoint are required.");
        if (p.Role is not ("OperationalChampion" or "ActiveShadowChallenger" or "ShadowBaseline" or "ChampionControl" or "GateComparison") ||
            (p.Role == PaperGateComparison.LiveRole ? p.ComparisonAccount is not { } account ||
                account.AccountId != p.PortfolioId || account.LivePolicyVersionId != p.PolicyVersionId ||
                account.StrategyCode != "DelphiLive" || !Enum.IsDefined(account.Profile) ||
                account.BatchId == Guid.Empty || account.PairId == Guid.Empty || account.DailyStrategyVersionId == Guid.Empty :
                p.ComparisonAccount is not null))
            throw new InvalidOperationException("The paper strategy and gate variant must match their account identity.");
        var mark = p.Marks[^1];
        if (mark is null || mark.MarkId == Guid.Empty || !mark.Complete || mark.Nav is null ||
            mark.Kind is not (DelphiLivePortfolioMarkKind.Checkpoint or DelphiLivePortfolioMarkKind.Closing) ||
            mark.Reason == "CorporateActionUnsupported" || mark.Positions.IsDefault ||
            mark.BarEndUtc.Kind != DateTimeKind.Utc || mark.BarEndUtc > p.UpdatedUtc ||
            mark.BarEndUtc.Ticks % TimeSpan.FromMinutes(5).Ticks != 0 ||
            p.CurrentSession != mark.TradingDate || p.EffectiveSession > mark.TradingDate)
            throw new InvalidOperationException("The latest complete regular checkpoint must describe this session.");

        ValidateLedger(p, mark.BarEndUtc);
        var open = p.OpenPositions.OrderBy(x => x.PositionId).ToArray();
        if (mark.Positions.Length != open.Length || mark.Positions.Any(m => m is null || m.Price <= 0m ||
                m.BarEndUtc != mark.BarEndUtc || !open.Any(h => h.PositionId == m.PositionId && h.Quantity == m.Quantity &&
                    string.Equals(h.Symbol, m.Symbol, StringComparison.OrdinalIgnoreCase))) ||
            mark.Positions.Select(m => m.PositionId).Distinct().Count() != open.Length)
            throw new InvalidOperationException("Checkpoint prices must match every current holding exactly.");
        var nav = DelphiLivePortfolioPolicy.CalculateExactNav(p.Cash,
            open.Select(h => (h.PositionId, h.Symbol, h.Quantity)).ToArray(), mark.Positions, mark.BarEndUtc);
        if (!nav.IsComplete || nav.NetAssetValue != mark.Nav)
            throw new InvalidOperationException("The checkpoint must reconcile to the current cash and holdings.");

        var source = new Source(p.GenerationId, p.PolicyVersionId, p.Role, p.ComparisonAccount, p.Revision, p.StartingCapital,
            p.UpdatedUtc, p.Cash, p.Positions.OrderBy(x => x.PositionId).Select(h => new Held(h.PositionId, h.Symbol,
                h.Quantity, h.AveragePurchasePrice, h.OpenedUtc, h.EntryActionId, h.ClosedUtc, h.ExitActionId)).ToImmutableArray(),
            p.Fills.OrderBy(f => f.FillId).ToImmutableArray(),
            mark with { Positions = mark.Positions.OrderBy(m => m.PositionId).ToImmutableArray() });
        if (previous is not null)
        {
            if (previous.Source?.Contract != ValuationContract)
                throw new InvalidOperationException("Changing valuation families requires reviewed handover evidence.");
            Source prior;
            try { prior = JsonSerializer.Deserialize<Source>(previous.Source.EvidenceJson) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("Saved paper source evidence is invalid."); }
            if (prior.GenerationId != p.GenerationId || prior.StartingCapital != p.StartingCapital ||
                p.Revision < prior.Revision || p.UpdatedUtc < prior.UpdatedUtc || prior.Fills.IsDefault ||
                prior.Fills.Any(f => !p.Fills.Contains(f)) || prior.ComparisonAccount != p.ComparisonAccount || prior.Role != p.Role)
                throw new InvalidOperationException("Paper capital, generation and completed fills cannot be replaced.");
            // A revision identifies one immutable source snapshot, even if the resulting NAV is equal.
            if (prior.Revision == p.Revision && JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(source))
                throw new InvalidOperationException("An immutable paper revision cannot have changed source evidence.");
        }

        string json = JsonSerializer.Serialize(source);
        var at = new DateTimeOffset(mark.BarEndUtc);
        return new(p.PortfolioId, mark.MarkId, mark.TradingDate, at, at.Add(maximumEvidenceAge), mark.Nav!.Value, true, true)
        {
            Source = new(ValuationContract, json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))))
        };
    }

    private static void ValidateLedger(DelphiLivePortfolioSnapshot p, DateTime checkpoint)
    {
        if (p.Positions.Any(h => h is null || h.PositionId == Guid.Empty || string.IsNullOrWhiteSpace(h.Symbol) ||
                h.Quantity <= 0 || h.AveragePurchasePrice <= 0m || h.OpenedUtc.Kind != DateTimeKind.Utc || h.OpenedUtc > checkpoint ||
                h.ClosedUtc is { } closed && (closed.Kind != DateTimeKind.Utc || closed < h.OpenedUtc || closed > checkpoint)) ||
            p.Positions.Select(h => h.PositionId).Distinct().Count() != p.Positions.Length ||
            p.OpenPositions.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.OpenPositions.Count() ||
            p.Fills.Any(f => f is null || f.FillId == Guid.Empty || f.ActionId == Guid.Empty || f.QuoteObservationId == Guid.Empty ||
                f.Quantity <= 0 || f.Price <= 0m || !Enum.IsDefined(f.Side) || f.FilledUtc.Kind != DateTimeKind.Utc ||
                f.FilledUtc > checkpoint || !p.Positions.Any(h => h.PositionId == f.PositionId && h.Symbol == f.Symbol)) ||
            p.Fills.Select(f => f.FillId).Distinct().Count() != p.Fills.Length ||
            p.Fills.Select(f => f.ActionId).Distinct().Count() != p.Fills.Length)
            throw new InvalidOperationException("Paper positions and fills must describe the checkpoint book.");
        foreach (var h in p.Positions)
        {
            var buys = p.Fills.Where(f => f.PositionId == h.PositionId && f.Side == DelphiLiveActionSide.Buy).ToArray();
            var sells = p.Fills.Where(f => f.PositionId == h.PositionId && f.Side == DelphiLiveActionSide.Sell).ToArray();
            if (buys.Length != 1 || buys[0].Quantity != h.Quantity || buys[0].Price != h.AveragePurchasePrice ||
                buys[0].ActionId != h.EntryActionId || buys[0].FilledUtc != h.OpenedUtc ||
                (h.ClosedUtc is null ? sells.Length != 0 || h.ExitActionId is not null :
                    sells.Length != 1 || sells[0].Quantity != h.Quantity || sells[0].ActionId != h.ExitActionId || sells[0].FilledUtc != h.ClosedUtc))
                throw new InvalidOperationException("Each paper position must reconcile to its original whole-share fills.");
        }
        // This ledger has fixed original capital and no external cash-flow operation. Opposing broker
        // deposits cannot be inferred away here: broker books are rejected by the paper journal scope.
        if (p.Cash != p.StartingCapital + p.Fills.Sum(f => (f.Side == DelphiLiveActionSide.Buy ? -1m : 1m) * f.Quantity * f.Price))
            throw new InvalidOperationException("Paper cash must reconcile to fixed capital and all recorded fills.");
    }
}
