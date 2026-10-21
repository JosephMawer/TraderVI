#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

public sealed record BrokerPositionCycle(Guid PositionId, string SecurityId, string Symbol, int Quantity,
    decimal CostBasis, DateTimeOffset EntryCompletedAt, string FirstBuyOrderId, Guid? LegacyPositionId);

/// <summary>Only a qualified cash-event source may issue this interval, never a net-deposit delta.</summary>
public sealed record BrokerCashCoverage(Guid BindingId, DateTimeOffset From, DateTimeOffset Through,
    DateTimeOffset ObservedAt, string Contract, string EvidenceHash);

public sealed record BrokerAccountProjection(BrokerImportReceipt Import, BrokerAccountRiskState Risk,
    ImmutableArray<BrokerPositionCycle> Positions, ReportedTradingAccount Account,
    ImmutableArray<TradingActualFill> Fills, ImmutableArray<string> NewRiskBlocks)
{
    public string Fingerprint => BrokerAccountImport.Hash(new { Import.BindingId,
        Facts = BrokerAccountImport.FinancialFingerprint(Import.Plan.Observation), Risk, Positions });
}

public static class BrokerAccountProjector
{
    public static BrokerAccountProjection Project(BrokerImportBinding binding, BrokerImportReceipt import,
        BrokerAccountRiskState risk, BrokerCashCoverage? cashCoverage, DateTimeOffset now)
    {
        var observed = import.Plan.Observation;
        if (binding.BindingId != import.BindingId || binding.AccountLabel != "TFSA" || risk.BindingId != binding.BindingId ||
            binding.BrokerKey != BrokerAccountImport.Key(observed) || import.Plan.BrokerKey != binding.BrokerKey || !import.Plan.CanImport || !observed.HoldingsMatch ||
            observed.CheckedAt > now || observed.SnapshotAt > now || observed.SnapshotAt < observed.CheckedAt.AddMinutes(-2) ||
            now - observed.SnapshotAt > TimeSpan.FromSeconds(30) || now - observed.CheckedAt > TimeSpan.FromSeconds(30) ||
            observed.Cash is null or < 0 || observed.BuyingPower is null or < 0 || observed.AccountValue is null or <= 0 ||
            observed.Orders.Any(o => !o.IsTerminal || !WealthsimpleReconciliation.Valid(o, observed.CheckedAt)) ||
            observed.Orders.Select(o => o.ExternalId).Distinct().Count() != observed.Orders.Count)
            throw new InvalidOperationException("Fresh, complete, matching broker holdings/history and the same permanent binding are required.");
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        var blocks = ImmutableArray.CreateBuilder<string>();
        if (risk.TrialCarry is not null && cashCoverage?.Contract !=
            (risk.Continuation is null ? BrokerTrialCarryForward.Contract : BrokerOctober21Continuation.Contract))
            blocks.Add("Trial-carried references require their exact next-session trial scope; they cannot qualify production or older scopes.");
        if (risk.TradingDate != date || risk.Through > observed.SnapshotAt)
            blocks.Add("Continue the saved account risk session before new buys.");
        if (cashCoverage is null || cashCoverage.BindingId != binding.BindingId || cashCoverage.From > risk.Through ||
            cashCoverage.Through < observed.SnapshotAt || cashCoverage.ObservedAt < cashCoverage.Through ||
            cashCoverage.ObservedAt > now || !CompleteTradingStrategy.IsHash(cashCoverage.EvidenceHash) ||
            string.IsNullOrWhiteSpace(cashCoverage.Contract))
            blocks.Add("Complete effective cash-event coverage through this account observation is unverified.");
        var positions = ImmutableArray.CreateBuilder<BrokerPositionCycle>();
        var fills = ImmutableArray.CreateBuilder<TradingActualFill>();
        foreach (var group in observed.Orders.Where(o => o.FilledQuantity > 0).GroupBy(o => o.SecurityId))
        {
            decimal quantity = 0, cost = 0; string? first = null; Guid cycle = default; DateTimeOffset entry = default;
            foreach (var order in group.OrderBy(o => o.LastFilledAt).ThenBy(o => o.ExternalId, StringComparer.Ordinal))
            {
                if (order.FilledQuantity != decimal.Truncate(order.FilledQuantity) || order.FilledQuantity > int.MaxValue ||
                    order.LastFilledAt is null || order.NetValue is null or <= 0 || order.GrossValue is null or <= 0)
                    throw new InvalidOperationException("Complete whole-share cumulative fill facts are required for the real account projection.");
                if (order.Side == "BUY")
                {
                    if (quantity == 0)
                    {
                        first = order.ExternalId; entry = order.LastFilledAt.Value;
                        cycle = BrokerManagedStrategy.StableId($"{binding.BindingId:D}:{order.SecurityId}:{first}");
                        // The cumulative endpoint is not the first fill. Different session dates need individual fill evidence.
                        if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(order.CreatedAt.UtcDateTime)) !=
                            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(entry.UtcDateTime)))
                            blocks.Add("An entry spans sessions; individual fill timing is needed for strategy age rules.");
                    }
                    quantity += order.FilledQuantity; cost += order.NetValue.Value;
                }
                else if (order.Side == "SELL" && quantity >= order.FilledQuantity && quantity > 0)
                { cost *= (quantity - order.FilledQuantity) / quantity; quantity -= order.FilledQuantity; }
                else throw new InvalidOperationException("Order history cannot establish a continuous real position cycle.");
                fills.Add(new(BrokerManagedStrategy.StableId($"{binding.BindingId:D}:{order.ExternalId}"), cycle,
                    order.Symbol, order.Side, order.LastFilledAt.Value.UtcDateTime,
                    DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(order.LastFilledAt.Value.UtcDateTime)),
                    (int)order.FilledQuantity, order.GrossValue.Value / order.FilledQuantity, null, null, null));
            }
            if (quantity == 0) continue;
            var holding = observed.Holdings.SingleOrDefault(h => h.SecurityId == group.Key);
            if (holding is null || holding.BrokerQuantity != quantity || !holding.Matches ||
                decimal.Abs(holding.ReconstructedBookValue - cost) > 0.01m || quantity > int.MaxValue)
                throw new InvalidOperationException("Current holdings changed or their cost does not match the projected cycle.");
            Guid? legacy = import.Plan.Rows.SingleOrDefault(r => r.ExternalId == first)?.LegacyPositionId;
            positions.Add(new(cycle, group.Key, holding.Symbol, (int)quantity, holding.BrokerBookValue!.Value, entry, first!, legacy));
        }
        if (positions.Count != observed.Holdings.Count(h => h.BrokerQuantity > 0) ||
            positions.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != positions.Count)
            throw new InvalidOperationException("Every current holding must have exactly one unambiguous real position cycle.");
        string fingerprint = BrokerAccountImport.FinancialFingerprint(observed);
        var account = new ReportedTradingAccount(import.RequestId, "TFSA", "CAD", date, observed.SnapshotAt.UtcDateTime,
            "Verified broker projection", fingerprint, System.Math.Min(observed.Cash.Value, observed.BuyingPower.Value), observed.AccountValue,
            risk.SessionReference.Value, risk.DrawdownReference.Value, 0, risk.DailyLossHold, risk.CapitalReviewRequired,
            positions.Select(p => new ReportedTradingHolding(p.PositionId, p.Symbol, p.Quantity, p.CostBasis,
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(p.EntryCompletedAt.UtcDateTime)))).ToImmutableArray())
            { ValuationCash = observed.Cash.Value };
        account.Validate();
        return new(import, risk, positions.ToImmutable(), account, fills.ToImmutable(), blocks.Distinct().ToImmutableArray());
    }
}
