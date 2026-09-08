#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Reported financial facts. Derived strategy protection belongs to the evaluator, not this record.</summary>
public sealed record ReportedTradingHolding(Guid PositionId, string Symbol, int Quantity,
    decimal CostBasis, DateOnly EntryDate);

public sealed record ReportedTradingAccount(Guid SnapshotId, string AccountLabel, string Currency,
    DateOnly TradingDate, DateTime ReportedUtc, string ReportedBy, string LedgerFingerprint,
    decimal? AvailableCash, decimal? NetAssetValue, decimal? SessionOpeningValue,
    decimal? HighestClosingValue, int? PendingOrderCount,
    bool DailyLossHold, bool CapitalReviewRequired, ImmutableArray<ReportedTradingHolding> Holdings)
{
    public string? CapitalReviewReason { get; init; }
    public void Validate()
    {
        if (SnapshotId == Guid.Empty || string.IsNullOrWhiteSpace(AccountLabel) || AccountLabel.Length > 64 ||
            Currency is not { Length: 3 } || Currency.Any(c => c is < 'A' or > 'Z') ||
            ReportedUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(ReportedBy) ||
            !CompleteTradingStrategy.IsHash(LedgerFingerprint) || Holdings.IsDefault ||
            AvailableCash is < 0m || NetAssetValue is < 0m || SessionOpeningValue is <= 0m ||
            HighestClosingValue is <= 0m || PendingOrderCount is < 0 ||
            Holdings.Select(h => h.PositionId).Distinct().Count() != Holdings.Length ||
            Holdings.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Holdings.Length ||
            Holdings.Any(h => h.PositionId == Guid.Empty || string.IsNullOrWhiteSpace(h.Symbol) ||
                h.Quantity < 1 || h.CostBasis <= 0m || h.EntryDate > TradingDate))
            throw new ArgumentException("Reported account identity, financial facts or ledger reference are invalid.");
    }
}

public sealed record TradingAccountReadiness(ImmutableArray<string> NewRiskBlocks)
{
    public bool CanConsiderNewRisk => NewRiskBlocks.IsEmpty;
}

/// <summary>
/// A missing balance is not zero cash or simulated capital. These checks govern new risk only;
/// protective evaluation can continue from individually available reported holdings and market evidence.
/// </summary>
public static class TradingAccountReadinessPolicy
{
    public static TradingAccountReadiness Evaluate(ReportedTradingAccount? reported, string targetAccount,
        DateOnly tradingDate, DateTime asOfUtc, string currentLedgerFingerprint, bool entriesPaused)
    {
        if (string.IsNullOrWhiteSpace(targetAccount) || asOfUtc.Kind != DateTimeKind.Utc ||
            !CompleteTradingStrategy.IsHash(currentLedgerFingerprint))
            throw new ArgumentException("Readiness requires the target account, current ledger identity and UTC evaluation time.");
        var blocks = ImmutableArray.CreateBuilder<string>();
        if (entriesPaused) blocks.Add("Trading entries are paused.");
        if (reported is null)
        {
            blocks.Add("Report the target account's current financial state before considering new risk.");
            return new(blocks.ToImmutable());
        }
        reported.Validate();
        if (!string.Equals(reported.AccountLabel, targetAccount, StringComparison.OrdinalIgnoreCase))
            blocks.Add("Reported financial state belongs to another account.");
        if (reported.Currency != "CAD") blocks.Add("The reviewed strategy families require a CAD account.");
        if (reported.TradingDate != tradingDate || reported.ReportedUtc > asOfUtc)
            blocks.Add("A current-session, already-reported account snapshot is required.");
        if (!string.Equals(reported.LedgerFingerprint, currentLedgerFingerprint, StringComparison.OrdinalIgnoreCase))
            blocks.Add("Reported holdings or fills changed. Reconcile available account state again.");
        if (!reported.AvailableCash.HasValue || reported.NetAssetValue is not > 0m ||
            !reported.SessionOpeningValue.HasValue || !reported.HighestClosingValue.HasValue)
            blocks.Add("Cash, account value, session opening value and historical closing high must be available.");
        if (reported.PendingOrderCount is null or > 0)
            blocks.Add("Report and resolve outstanding real orders before considering new risk.");
        if (reported.DailyLossHold) blocks.Add("The account's daily loss hold remains active.");
        if (reported.CapitalReviewRequired) blocks.Add("The account still requires capital review.");
        return new(blocks.ToImmutable());
    }
}
