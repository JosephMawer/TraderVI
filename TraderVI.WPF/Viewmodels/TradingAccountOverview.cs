#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

// Pure presentation. A display refresh cannot assign a strategy or manufacture a financial fact.
public sealed record TradingAccountOverview(string Account, decimal? ReportedValue, decimal? ReportedCash,
    string ReportContext, string Strategy, string StrategySince, string TotalGainExplanation,
    decimal? OpenGain, decimal? HoldingsValue, IReadOnlyList<TradingOverviewHolding> Holdings, decimal? RecordedSaleGain, string SaleGainContext)
{
    public static TradingAccountOverview Create(TradingAccountView account, IEnumerable<ActivePositionInfo> marks, DateTime utcNow, IEnumerable<TradeLogInfo>? trades = null)
    {
        var localNow = PaperTradingMonitor.ToToronto(utcNow);
        var from = localNow.Date.AddDays(-29);
        var sales = trades?.Where(t => t.ExecutionMode == TrackedExecutionMode.Real &&
            string.Equals(t.AccountLabel, account.AccountLabel, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.TradeType, "SELL", StringComparison.OrdinalIgnoreCase) && t.TradeDate >= from && t.TradeDate <= localNow).ToArray();
        var owned = marks.Where(p => p.ExecutionMode == TrackedExecutionMode.Real && p.IsActive &&
            string.Equals(p.AccountLabel, account.AccountLabel, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.PositionId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.LastUpdatedUtc).First());
        var batch = account.Latest;
        bool currentBatch = account.Assignment is not null && batch?.PromotionId == account.Assignment.PromotionId &&
            string.Equals(batch.AccountLabel, account.AccountLabel, StringComparison.OrdinalIgnoreCase) &&
            MatchesLedger(account);
        var holdings = account.Holdings.Select(h =>
        {
            owned.TryGetValue(h.PositionId, out var mark);
            bool matches = mark is not null && mark.Shares == h.Quantity && mark.CostBasis == h.CostBasis &&
                string.Equals(mark.Symbol, h.Symbol, StringComparison.OrdinalIgnoreCase) && mark.CurrentPrice is > 0m &&
                mark.LastUpdatedUtc != default && mark.LastUpdatedUtc <= utcNow;
            decimal? value = matches ? h.Quantity * mark!.CurrentPrice : null;
            var advice = currentBatch ? batch!.Result.Recommendations.FirstOrDefault(r => r.PositionId == h.PositionId &&
                string.Equals(r.Symbol, h.Symbol, StringComparison.OrdinalIgnoreCase)) : null;
            return new TradingOverviewHolding(h.PositionId, h.Symbol, h.Quantity, h.CostBasis, value, value - h.CostBasis,
                matches ? Local(mark!.LastUpdatedUtc) : "Valuation unavailable",
                advice is null ? "No current strategy decision" : $"{advice.Action} · {advice.Reason}",
                advice?.EvidenceUtc is DateTime time ? Local(time) : "—");
        }).ToArray();
        var report = account.Report;
        bool ownsReport = report is not null && string.Equals(report.AccountLabel, account.AccountLabel, StringComparison.OrdinalIgnoreCase);
        bool currentLedger = ownsReport && report!.LedgerFingerprint == account.LedgerFingerprint;
        string context = !ownsReport ? "No account report saved. Report your account's current CAD value and cash."
            : $"Reported {Local(report!.ReportedUtc)} · " +
                (report.LedgerFingerprint != account.LedgerFingerprint ? "Holdings or fills changed; reconcile this report."
                : report.TradingDate != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(utcNow)) ? "Prior-session account report."
                : "Operator-reported account facts; not broker synchronized.");
        bool complete = account.Installed && holdings.All(h => h.Value.HasValue);
        return new(account.AccountLabel, currentLedger ? report!.NetAssetValue : null, currentLedger ? report!.AvailableCash : null,
            context, account.Assignment?.Strategy.Name ?? "No main strategy approved",
            account.Assignment is { } assignment ? $"Active since {Local(assignment.EffectiveUtc)}" : "Choose a paper portfolio to review its strategy.",
            "Total account gain is unavailable: complete contribution, withdrawal, income and cost history is not recorded. Open-position gains below cover only this account's recorded holdings.",
            complete ? holdings.Sum(h => h.Gain!.Value) : null, complete ? holdings.Sum(h => h.Value!.Value) : null, holdings,
            account.Installed && sales is not null && sales.All(t => t.RealizedPnL.HasValue) ? sales.Sum(t => t.RealizedPnL!.Value) : null,
            $"Recorded sales · {from:MMM d}–{localNow:MMM d, yyyy} · TraderVI records only; not complete broker earnings.");
    }
    public static string Local(DateTime utc) => $"{PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(utc, DateTimeKind.Utc)):MMM d, yyyy HH:mm:ss} Toronto";
    public static bool MatchesLedger(TradingAccountView account) => account.Latest?.LedgerFingerprint is { } fingerprint
        ? fingerprint == account.LedgerFingerprint
        : account.Report is { } report && report.LedgerFingerprint == account.LedgerFingerprint && account.Latest?.AccountReportId == report.SnapshotId;
}

public sealed record TradingOverviewHolding(Guid PositionId, string Symbol, int Quantity, decimal CostBasis,
    decimal? Value, decimal? Gain, string ValuedAt, string Decision, string DecisionAt);
