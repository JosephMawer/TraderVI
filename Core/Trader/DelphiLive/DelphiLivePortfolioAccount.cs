#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>Read-only account identity, including queued and ended generations.</summary>
public sealed record DelphiLivePortfolioAccount(DelphiLivePortfolioSnapshot Snapshot,
    DateTime EffectiveOpenUtc, DateOnly? EndExclusiveSession, DateTime? CancelledUtc)
{
    public bool IsCurrentOrQueued(DateOnly date) => CancelledUtc is null &&
        (EndExclusiveSession is null || EndExclusiveSession > date);

    public string StatusAt(DateTime nowUtc)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")));
        if (CancelledUtc.HasValue) return "Cancelled";
        if (EndExclusiveSession <= date) return "Ended";
        if (nowUtc < EffectiveOpenUtc) return $"Queued · {Snapshot.EffectiveSession:MMM d}";
        if (Snapshot.PendingActions.Any(a => a.Intent.Side == DelphiLiveActionSide.Sell)) return "Exit pending";
        if (Snapshot.Guards.CapitalReviewRequired) return "Capital review required";
        if (Snapshot.Guards.DailyBuyingPaused && Snapshot.CurrentSession == date) return "New buys paused";
        return Snapshot.CurrentSession is null ? "Awaiting first session" : "Enabled";
    }

    public string DisplayName => $"Delphi Live — {Snapshot.Role switch
    {
        "OperationalChampion" => "Main paper account",
        "ActiveShadowChallenger" => "Challenger",
        "ChampionControl" => "Comparison control",
        "ShadowBaseline" => "Baseline",
        _ => Snapshot.Role
    }} · {Snapshot.PortfolioId.ToString("N")[..8]}";
}

public sealed record DelphiLiveAccountValuation(decimal? NetAssetValue, decimal RealizedProfitLoss,
    decimal? UnrealizedProfitLoss, decimal? TotalReturn, decimal? DailyReturn, decimal? Drawdown,
    DateTime? MarkUtc, IReadOnlyDictionary<Guid, decimal> PositionPrices, string Explanation)
{
    /// <summary>Display only. Never combine current holdings/cash with a superseded portfolio mark.</summary>
    public static DelphiLiveAccountValuation From(DelphiLivePortfolioSnapshot portfolio, DateOnly today)
    {
        var open = portfolio.OpenPositions.ToArray();
        var latest = portfolio.Marks.LastOrDefault();
        decimal realized = portfolio.Positions.Where(p => p.ClosedUtc.HasValue).Sum(p =>
        {
            var sale = portfolio.Fills.Single(f => f.ActionId == p.ExitActionId && f.Side == DelphiLiveActionSide.Sell);
            return sale.Quantity * (sale.Price - p.AveragePurchasePrice);
        });
        var prices = new Dictionary<Guid, decimal>();
        bool matching = latest is { Complete: true, Nav: not null } && latest.Reason != "CorporateActionUnsupported" &&
            open.All(p => latest.Positions.Count(m => m.PositionId == p.PositionId && m.Symbol == p.Symbol &&
                m.Quantity == p.Quantity && m.BarEndUtc == latest.BarEndUtc) == 1) &&
            // A mark may retain evidence for a position sold earlier in the same cycle.
            latest.Nav == portfolio.Cash + open.Sum(p => latest.Positions.Single(m => m.PositionId == p.PositionId).Price * p.Quantity);
        decimal? nav = null;
        DateTime? markUtc = null;
        string explanation;
        if (latest?.Reason == "CorporateActionUnsupported")
            explanation = "Valuation unavailable: a corporate action requires review.";
        else if (open.Length == 0)
        {
            nav = portfolio.Cash;
            explanation = "Cash-only account; no holding prices are needed.";
        }
        else if (matching)
        {
            nav = latest!.Nav;
            markUtc = latest.BarEndUtc;
            foreach (var position in open)
                prices.Add(position.PositionId, latest.Positions.Single(m => m.PositionId == position.PositionId).Price);
            explanation = "Value uses the latest saved exact checkpoint; it is not a current quote.";
        }
        else explanation = "Valuation unavailable: waiting for an exact checkpoint matching the current holdings and cash.";
        decimal? unrealized = nav - portfolio.Cash - open.Sum(p => p.Quantity * p.AveragePurchasePrice);
        return new(nav, realized, unrealized, nav / portfolio.StartingCapital - 1m,
            portfolio.CurrentSession == today && portfolio.OpeningNav is > 0m ? nav / portfolio.OpeningNav - 1m : null,
            nav.HasValue && portfolio.Guards.HighestClosingNav > 0m
                ? System.Math.Min(0m, nav.Value / portfolio.Guards.HighestClosingNav - 1m) : null,
            markUtc, prices, explanation);
    }
}
