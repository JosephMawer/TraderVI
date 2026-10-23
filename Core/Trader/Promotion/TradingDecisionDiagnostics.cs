#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

// Private, account-scoped SQL evidence. No credentials, provider text, balances or quote prices.
// These observations never grant permission or participate in trading decisions.
public sealed record TradingEntryDecision(string Symbol, DateTime? SignalBarUtc, string Outcome)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionSelection { get; init; }
}
public sealed record TradingQuoteAttempt(string Symbol, DateTime? RequestedUtc, DateTime? ReceivedUtc,
    string Outcome, string? ErrorType = null);
public sealed record TradingQuoteReadResult(ImmutableArray<TradingQuote> Quotes,
    ImmutableArray<TradingQuoteAttempt> Attempts, ImmutableArray<string> NewRiskBlocks);
// Counts describe consumed inputs, not permission, signal qualification or a broker fill.
public sealed record TradingMarketInputTrace(int SuppliedObservations, int EvaluatedSymbols,
    int FreshSymbols, DateTime? LatestBarUtc)
{
    public static TradingMarketInputTrace? Capture(TradingRecommendationInput input, TradingRecommendationResult result)
    {
        if (input.Assignment.Strategy.Family != TradingStrategyFamily.DelphiLive) return null;
        var policy = input.Assignment.Strategy.ReadLivePolicy();
        // Reads are incremental. A poll with no new rows may still have fresh evaluated memory.
        var consumed = result.Memory.Symbols.Values.Where(s => s.LastLiveResult is not null &&
            s.LastLiveBarUtc >= input.SessionOpenUtc && s.LastLiveBarUtc <= input.SessionCloseUtc &&
            s.LastLiveBarUtc <= input.EvaluatedUtc).ToArray();
        return new(input.LiveInputs.Length, consumed.Length,
            consumed.Count(s => input.EvaluatedUtc - s.LastLiveBarUtc!.Value <= policy.BarInterval + policy.CollectionOffset),
            consumed.Select(s => s.LastLiveBarUtc).DefaultIfEmpty().Max());
    }
}
public sealed record BrokerDecisionTrace(DateTime EvaluatedUtc, DateTimeOffset RecordedAt,
    ImmutableArray<TradingEntryDecision> Entries, ImmutableArray<TradingQuoteAttempt> QuoteAttempts,
    ImmutableArray<string> NewRiskBlocks, string SelectionOutcome, string? SelectedSymbol, string? SelectedAction)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TradingMarketInputTrace? MarketInputs { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureType { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BrokerOrderCheck? FailedOrderCheck { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalCheckExplanation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? FailureSqlNumber { get; init; }
    public ImmutableArray<string> QuoteReadBlocks { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => SelectionOutcome switch
    {
        "SubmissionAcknowledged" => "Last decision received broker acknowledgement; consult order status for confirmed fills.",
        "SubmissionOutcomeUnknown" => "Order outcome is uncertain. Recover the original order before further execution.",
        "IntentReserved" => "Original order intent saved; transmission is not confirmed.",
        "PermissionExpiredAfterReservationNoSend" => "Permission expired after the original intent was saved. No transmission attempted.",
        "PermissionExpiredBeforeReservation" => "Purchase permission expired before order reservation.",
        "QuoteObservationPending" => "Confirmed signals are awaiting quote checks.",
        _ => EntrySummary
    };
    [System.Text.Json.Serialization.JsonIgnore]
    private string EntrySummary => Entries.IsEmpty ? MarketInputs switch
        {
            { EvaluatedSymbols: 0 } => "Waiting for verified shared market inputs; no current-session market input has been evaluated.",
            { FreshSymbols: 0 } => "Waiting for fresh shared market inputs; previously evaluated inputs are stale.",
            { FreshSymbols: > 0 } => "Fresh shared market inputs evaluated; no confirmed entry signal at this evaluation.",
            _ => "No confirmed entry signal at this evaluation."
        }
        : string.Join("; ", Entries.GroupBy(e => e.Outcome).Select(g => $"{Label(g.Key)}: {g.Count()}")) + ".";

    private static string Label(string outcome) => outcome switch
    {
        "Recommended" => "Buy recommendations",
        "SignalTooOld" => "Confirmed signals waiting for fresh evidence",
        "SignalOutsideSession" => "Signals outside this session",
        "NewRiskBlocked" => "Confirmed signals blocked by account/risk readiness",
        "QuoteNotReturned" => "Confirmed signals without a returned quote",
        "QuoteUnavailable" => "Confirmed signals with failed quotes",
        "EntryCutoffPassed" => "Confirmed signals after the entry cutoff",
        "BeforeEntryWindow" => "Confirmed signals before the entry window",
        "InsufficientCashForOneShare" => "Confirmed signals below one-share allocation",
        "PortfolioFull" => "Confirmed signals with no position slot",
        _ => "Confirmed signals waiting for another entry check (" + outcome + ")"
    };
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum BrokerOrderCheck
{
    AccountOperationBusy, OriginalOrderRecoveryRequired, SharedSafetyBlocked, RiskChanged,
    BrokerBuyContextUnavailable, ManualBuyValidation, AccountOrInstrumentChanged,
    AskAboveDecisionReference, AvailableCashExceeded, AccountRiskBlocked,
    BrokerSellContextUnavailable, ManualSellValidation, LoginChanged, PurchasePermissionExpired,
    AccountIdentityChanged, InstrumentChanged, CurrencyChanged, CashChanged, BuyingPowerChanged, HoldingsChanged
}

internal sealed record BrokerOrderFailure(BrokerOrderCheck Check, string? LocalExplanation);
internal static class BrokerOrderFailureEvidence
{
    private const string Key = "TraderVI.FixedOrderCheck";
    // Preserve existing exception types/messages and financial behavior. Only known local checks
    // supply explanations; exception/provider messages are never copied to the durable trace.
    public static InvalidOperationException Tag(string message, BrokerOrderCheck check, string? localExplanation = null)
    {
        var error = new InvalidOperationException(message);
        error.Data[Key] = new BrokerOrderFailure(check, localExplanation);
        return error;
    }
    public static BrokerOrderFailure? Read(Exception error) => error.Data[Key] as BrokerOrderFailure;
}
