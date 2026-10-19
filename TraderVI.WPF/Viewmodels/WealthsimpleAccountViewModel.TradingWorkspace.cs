#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.ComponentModel;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class WealthsimpleAccountViewModel
{
    private bool overallPerformance;
    private WealthsimpleHoldingRow? selectedHolding;
    public bool ShowOverallPerformance
    {
        get => overallPerformance;
        set { if (overallPerformance == value) return; overallPerformance = value; Notify(); }
    }
    public string PerformanceLabel => overallPerformance ? "Overall account performance · CAD" : "Today's performance · CAD";
    public string PerformanceAmount => overallPerformance ? ReturnText : "Unavailable";
    public string PerformanceRate => overallPerformance && Snapshot?.ReturnRate is { } rate
        ? rate.ToString("+0.00%;-0.00%;0.00%") : "";
    public string PerformanceContext => overallPerformance ? $"{ReturnPeriod} · {ReturnAsOf}"
        : "A verified daily account return is not available. Overall uses the broker's supplied period.";

    public WealthsimpleHoldingRow? SelectedHolding
    {
        get
        {
            var current = Holdings;
            if (selectedHolding != null && !current.Contains(selectedHolding))
                selectedHolding = current.FirstOrDefault(h => h.SellSelection?.SecurityId == selectedHolding.SellSelection?.SecurityId &&
                    string.Equals(h.Symbol, selectedHolding.Symbol, StringComparison.OrdinalIgnoreCase));
            return selectedHolding ??= current.FirstOrDefault();
        }
        set
        {
            if (value == null || !Holdings.Contains(value) || ReferenceEquals(value, selectedHolding)) return;
            selectedHolding = value; Notify();
        }
    }
    public TradingHoldingDecision SelectedDecision => TradingHoldingDecision.Describe(ManagedStrategy?.VerifiedSession, SelectedHolding?.SellSelection);
    public TradingHoldingDecision DecisionFor(WealthsimpleHoldingRow row) => TradingHoldingDecision.Describe(ManagedStrategy?.VerifiedSession, row.SellSelection);
    private void StrategyChanged(object? sender, PropertyChangedEventArgs args) { rowSnapshot = null; Notify(); }
}

/// <summary>Projects recorded intent only. Never infers Hold from a missing decision or evaluates prices.</summary>
public sealed record TradingHoldingDecision(string Status, string Reason, string NextStep, string Context)
{
    public static TradingHoldingDecision Describe(BrokerManagedSession? session, ManualSellSelection? selection)
    {
        if (session == null || selection == null)
            return new("Await decision", "No verified strategy decision is linked to this broker holding.",
                "Refresh the linked account and review its strategy. Existing holdings are not automatically assigned.", "No decision time available");
        var matches = session.Account.Positions.Where(p => p.SecurityId == selection.SecurityId &&
            string.Equals(p.Symbol, selection.Symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity == selection.Quantity).ToArray();
        if (matches.Length != 1)
            return new("Reconcile holding", "The strategy's recorded position does not match this broker holding.",
                "Reconcile actual shares before relying on strategy instructions.", "No matching position decision");
        var position = matches[0];
        string context = $"Management record · {TradingAccountOverview.Local(session.SavedAt.UtcDateTime)}";
        var sell = session.Orders.LastOrDefault(o => o.PositionId == position.PositionId && o.Side == "SELL" && !o.Reconciled);
        if (sell != null)
            return new(sell.Submission == null ? "Sell queued" : sell.Submission == WealthsimpleSubmissionStatus.Acknowledged ? "Sell pending" : "Check sale",
                $"A sale intent is recorded for this holding: {sell.Kind}. " +
                (sell.Submission == null ? "Submission has not been confirmed." : $"Submission: {sell.Submission}."),
                "Check the original order and reconcile confirmed fills before replacement purchases. Do not submit a duplicate sale.", context);
        if (session.Phase == BrokerManagedPhase.Transition && session.Approval.Review.RequiredExits.Contains(position.PositionId))
            return new("Exit planned", "This holding is not retained by the incoming strategy.",
                "Exit when market, data and order checks allow, including at a loss. Keep current protection until confirmed sale; new buys wait for required exits to reconcile.", context);
        return new("Await decision", "The management record does not contain an explicit current Hold or Sell result for this holding.",
            "Keep the existing protection. A current strategy evaluation is required; a missing result does not mean Hold.", context);
    }
}
