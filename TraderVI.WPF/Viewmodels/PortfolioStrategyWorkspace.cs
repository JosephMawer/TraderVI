#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Read-only presentation of independent account evidence. Never evaluates a strategy.</summary>
public sealed record PortfolioSymbolEvidence(string Symbol, string Confirmation, string Checks,
    string Outcome, string Next, string AsOf);
public sealed record PortfolioDecisionStage(string Stage, string Paper, string Real);

public static class PortfolioStrategyWorkspace
{
    public const string Unavailable = "Unavailable";
    public static string Words(string? value) => string.IsNullOrWhiteSpace(value) ? Unavailable
        : Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
    public static string At(DateTime? utc) => utc.HasValue
        ? TradingAccountOverview.Local(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)) : "Time unavailable";

    public static BrokerManagedSession? MatchSession(WealthsimpleTfsaSnapshot? snapshot, BrokerManagedSession? session)
    {
        if (snapshot is null || session is null) return null;
        try { BrokerManagedSessionComposition.MatchAccount(session, [snapshot.Account]); return session; }
        catch (InvalidOperationException) { return null; }
    }

    public static string Relationship(Guid? paperId, Guid? policyId, PaperGateProfile? profile, BrokerManagedSession? session)
    {
        if (session is null) return "Real assignment unavailable. Paper continues under its own controls; no shared version is assumed.";
        if (!paperId.HasValue) return "Select a Paper account to inspect its relationship to Real.";
        if (paperId != session.Strategy.PaperAccountId)
            return "Real follows a strategy reviewed from another Paper account. These accounts are not a matched strategy comparison.";
        if (policyId.HasValue && policyId != session.Strategy.Strategy.PolicyVersionId ||
            profile.HasValue && profile != session.Strategy.GateProfile)
            return "Real was approved from this Paper account, but the displayed policy or gate profile differs. Real retains its approved rules.";
        return "Real was approved from this Paper account. Paper keeps running independently. Matching source identity alone does not prove every current rule/input version matches.";
    }

    public static string Approval(BrokerManagedSession? session, DateTimeOffset now)
    {
        if (session is null) return "Unavailable — load the linked account in Trading.";
        var review = session.EffectiveReview;
        var expiry = review.NoTransferScope?.PurchaseExpiresAt;
        string state = session.ReviewInvalidated ? "Review required" : expiry.HasValue && now >= expiry.Value ? "Expired" : "Saved approval";
        return $"{state} · session {review.Session:yyyy-MM-dd}" +
            (expiry.HasValue ? $" · expires {At(expiry.Value.UtcDateTime)}" : " · applicable session/risk checks still required") +
            (session.NewBuysStopped ? " · new buys stopped" : "") + ". Execution enable is separate.";
    }

    public static PortfolioSymbolEvidence Paper(DelphiLivePortfolioSnapshot paper, string symbol)
    {
        paper.CandidateStates.TryGetValue(symbol, out var candidate);
        var action = paper.Actions.Where(a => a.Intent.Symbol == symbol).OrderByDescending(a => a.Intent.DecisionUtc).FirstOrDefault();
        var fill = paper.Fills.Where(f => f.Symbol == symbol).OrderByDescending(f => f.FilledUtc).FirstOrDefault();
        var held = paper.OpenPositions.Where(p => p.Symbol == symbol).Sum(p => p.Quantity);
        return new(symbol,
            candidate is null ? "No saved Paper confirmation" : $"{Words(candidate.Lifecycle.State.ToString())} · {Words(candidate.Lifecycle.ReasonCode)}",
            action is null ? "No action in loaded Paper activity" : $"{Words(action.Status)} · {Words(action.TerminalReason ?? action.PrimaryReason)} · {At(action.Intent.DecisionUtc)}",
            fill is null ? "No fill in loaded Paper activity" : $"Assumed {fill.Side.ToString().ToLowerInvariant()} fill · {fill.Quantity} × {fill.Price:N4} {paper.Currency} · {At(fill.FilledUtc)}",
            held > 0 ? $"{held} simulated shares · evaluate protection and exits under Paper controls"
                : "No simulated holding · evaluate entries under Paper controls",
            At(candidate?.EvaluatedUtc));
    }

    public static PortfolioSymbolEvidence Real(WealthsimpleTfsaSnapshot? snapshot, BrokerManagedSession? session, string symbol)
    {
        session = MatchSession(snapshot, session);
        var state = session?.Memory.Symbols.GetValueOrDefault(symbol);
        var trace = session?.LatestDecision;
        bool selected = string.Equals(trace?.SelectedSymbol, symbol, StringComparison.OrdinalIgnoreCase);
        var entry = trace?.Entries.LastOrDefault(e => string.Equals(e.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        string checks = entry is null ? "No symbol check in the latest saved evaluation" : Words(entry.Outcome);
        if (selected && trace?.FailedOrderCheck is { } failed)
        {
            checks = Words(failed.ToString());
            if (failed == BrokerOrderCheck.AskAboveDecisionReference)
                checks = trace.RejectedBuyPrices is { } prices
                    ? $"Buy stopped · reference {prices.ReferencePrice:N4}, broker ask {prices.BrokerAsk:N4} · {At(prices.ComparedAt.UtcDateTime)}"
                    : "Buy stopped · ask above decision reference. Historical price pair unavailable.";
        }
        if (trace is not null && (selected || entry is not null)) checks += $" · evaluation {At(trace.EvaluatedUtc)}";
        var order = session?.Orders.Where(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        string outcome = order is null ? "No order in the loaded management record"
            : order.Latest is { FilledQuantity: > 0, FillQuantityWasNull: false } fact
                ? $"Broker-reported {fact.Side.ToLowerInvariant()} fill · {fact.FilledQuantity} shares · {Words(fact.Status)} · {At(fact.LastFilledAt?.UtcDateTime)}"
                : $"{order.Side} intent · {Words(order.Submission?.ToString() ?? "Submission unconfirmed")} · fill unconfirmed · {At(order.CreatedAt.UtcDateTime)}";
        if (session is null) outcome = "Real order evidence unavailable";
        var held = snapshot?.Holdings.Where(h => string.Equals(h.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).Sum(h => h.Quantity);
        string next = !held.HasValue ? "Load broker holdings before interpreting the next action"
            : held > 0 ? $"{held} broker shares · consult Real protection/exit decisions and outstanding orders"
            : order is { Reconciled: false } ? "No broker holding · resolve the outstanding order before considering another entry"
            : "No broker holding · entries require Real confirmation, permission, risk and price checks";
        return new(symbol,
            state?.LiveEvaluation is { } evaluation ? $"{Words(evaluation.Lifecycle.State.ToString())} · {Words(evaluation.Lifecycle.ReasonCode)}"
                : "Real confirmation unavailable; Paper confirmation is not inherited",
            session is null ? "Real evaluation unavailable" : checks, outcome,
            next, At(state?.LastLiveBarUtc ?? (selected || entry is not null ? trace?.EvaluatedUtc : null)));
    }

    public static IReadOnlyList<PortfolioDecisionStage> Compare(PortfolioSymbolEvidence? paper, PortfolioSymbolEvidence? real) =>
    [new("Confirmation / lifecycle", paper?.Confirmation ?? Unavailable, real?.Confirmation ?? Unavailable),
     new("Execution checks", paper?.Checks ?? Unavailable, real?.Checks ?? Unavailable),
     new("Latest recorded outcome", paper?.Outcome ?? Unavailable, real?.Outcome ?? Unavailable),
     new("Next observation / action", paper?.Next ?? Unavailable, real?.Next ?? Unavailable),
     new("Observation / evaluation time", paper?.AsOf ?? "Time unavailable", real?.AsOf ?? "Time unavailable")];
}
