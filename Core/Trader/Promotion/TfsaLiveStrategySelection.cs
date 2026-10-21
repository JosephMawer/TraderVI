#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.Promotion;

public sealed record TfsaLiveStrategyCandidate(DelphiLivePortfolioSnapshot Snapshot, PortfolioDailyResult Day)
{
    public bool SupportsCurrentPromotion => Snapshot.ComparisonAccount == null && Snapshot.Role != "GateComparison";
    public bool HasPortfolioRulesOnlyLiveComparisonIdentity => Snapshot is
    {
        Role: "GateComparison", ComparisonAccount: { Profile: PaperGateProfile.PortfolioRulesOnlyV1, StrategyCode: "DelphiLive",
            ShadowStrategyVersionId: null, ShadowSettingsJson: null } comparison
    } && comparison.AccountId == Snapshot.PortfolioId && comparison.LivePolicyVersionId is { } original && original != Guid.Empty;
    // A source can be inspected without being eligible for recommendation approval or execution.
    // Discovery only: the native definition review must verify any assigned policy successor.
    public bool HasNormalLiveComparisonIdentity => Snapshot is
    {
        Role: "GateComparison", ComparisonAccount: { Profile: PaperGateProfile.NormalV1, StrategyCode: "DelphiLive",
            ShadowStrategyVersionId: null, ShadowSettingsJson: null } comparison
    } && comparison.AccountId == Snapshot.PortfolioId && comparison.LivePolicyVersionId is { } original && original != Guid.Empty;
    public bool SupportsTransitionReview => SupportsCurrentPromotion || HasNormalLiveComparisonIdentity &&
        Snapshot.ComparisonAccount!.LivePolicyVersionId == Snapshot.PolicyVersionId;
    public string DisplayName => Snapshot.ComparisonAccount is { } comparison
        ? $"{comparison.DisplayName} · Delphi Live"
        : $"Delphi Live · {(Snapshot.Role == "OperationalChampion" ? "Operational champion" : Snapshot.Role)} · {Snapshot.PolicyVersionId.ToString("N")[..8]}";
    public string DailySummary => $"{Day.Date:yyyy-MM-dd} · {(Day.Return.HasValue ? Day.Return.Value.ToString("+0.00%;-0.00%;0.00%") : "Unavailable")} · {(Day.IsClosing ? "Close" : "Partial")}";
    public override string ToString() => $"{DisplayName} · {DailySummary}";
}

public static class TfsaLiveStrategySelection
{
    public static IReadOnlyList<TfsaLiveStrategyCandidate> Rank(IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios)
    {
        var histories = portfolios.Select(p => (Snapshot: p, Days: PortfolioDailyPerformance.FromLiveMarks(p.Marks))).ToArray();
        DateOnly? latest = histories.SelectMany(h => h.Days).Select(d => (DateOnly?)d.Date).Max();
        // Compare the same saved date. Missing latest valuations do not fall back to earlier winners.
        return histories.Select(h => new TfsaLiveStrategyCandidate(h.Snapshot, PortfolioDailyPerformance.Calculate(h.Days, latest)))
            .OrderByDescending(c => c.Day.Return.HasValue).ThenByDescending(c => c.Day.Return)
            .ThenByDescending(c => c.SupportsCurrentPromotion)
            .ThenByDescending(c => c.Snapshot.Role == "OperationalChampion")
            .ThenBy(c => c.Snapshot.PortfolioId).ToArray();
    }
}
