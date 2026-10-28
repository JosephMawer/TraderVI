#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

public sealed record TradingMinuteDecisionTrace(DelphiLive.DelphiLiveRecoveredJudgment Previous,
    DelphiLive.DelphiLiveRecoveredJudgment Latest, DelphiLive.DelphiLiveRecoveredConfirmationResult Confirmation,
    string InputSha256, ImmutableArray<DelphiLive.DelphiLiveFamilyJudgment> Families,
    DateTime VwapThroughUtc, DelphiLive.DelphiLiveMeasurementAvailability VwapAvailability);

// Private, account-scoped SQL evidence. No credentials, provider text or balances. The explicitly
// approved rejected-buy comparison is the only diagnostic that retains quote prices (ADR-0100).
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
        if (input.Assignment.Strategy.MinuteEntry is not null)
        {
            var minutes = result.MinuteEvidence ?? [];
            return new(input.MinuteInputs?.Length ?? 0, minutes.Length,
                minutes.Count(m => m.Latest.Valid && m.Confirmation.Status is not DelphiLive.DelphiLiveRecoveredConfirmationStatus.ExpiredLatest),
                minutes.Select(m => (DateTime?)m.Latest.EndUtc).DefaultIfEmpty().Max());
        }
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
    public ImmutableArray<TradingMinuteDecisionTrace>? MinuteEvidence { get; init; }
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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BrokerBuyPriceComparison? RejectedBuyPrices { get; init; }
    public ImmutableArray<string> QuoteReadBlocks { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => SelectionOutcome switch
    {
        "SubmissionAcknowledged" => "Last decision received broker acknowledgement; consult order status for confirmed fills.",
        "SubmissionOutcomeUnknown" => "Order outcome is uncertain. Recover the original order before further execution.",
        "IntentReserved" => "Original order intent saved; transmission is not confirmed.",
        "PermissionExpiredAfterReservationNoSend" => "Permission expired after the original intent was saved. No transmission attempted.",
        "PermissionExpiredBeforeReservation" => "Purchase permission expired before order reservation.",
        "MinuteSignalExpiredBeforeReservation" => "Minute signal expired before order reservation; no order sent.",
        "MinuteSignalExpiredAfterReservationNoSend" => "Minute signal expired after saving its original intent. No order sent; intent recovery is required.",
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
        "MinuteNotQualifying" => "Minute observations that do not meet confirmation rules",
        "MinuteExpiredLatest" => "Minute observations waiting for current evidence",
        "MinuteInvalidEvidence" => "Minute observations blocked by missing or conflicted evidence",
        "MinuteContractMismatch" => "Minute source or policy mismatch",
        "MinuteSessionMismatch" => "Minute evidence from another session",
        "MinuteExpiredDuringQuote" => "Signals that expired during quote checks",
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
    AccountIdentityChanged, InstrumentChanged, CurrencyChanged, CashChanged, BuyingPowerChanged, HoldingsChanged,
    ExecutionQuoteRejected
}

// These facts explain a failed comparison; they never authorize or retry an order. BrokerQuotedAsOf
// is broker-reported, not a verified exchange timestamp. TMX currently supplies no source quote time.
public sealed record BrokerBuyPriceComparison(decimal ReferencePrice, decimal BrokerAsk,
    DateTimeOffset BrokerQuotedAsOf, DateTimeOffset BrokerRetrievedAt, DateTimeOffset ComparedAt)
{
    public BrokerDecisionReferenceQuote? ReferenceQuote { get; init; }
    public override string ToString() => "Rejected buy price comparison (private values omitted)";
}
public sealed record BrokerDecisionReferenceQuote(string SourceContract, string PriceField,
    DateTime RequestedUtc, DateTime ReceivedUtc, DateTime? SourceQuotedUtc);

internal sealed record BrokerOrderFailure(BrokerOrderCheck Check, string? LocalExplanation,
    BrokerBuyPriceComparison? PriceComparison = null);
internal static class BrokerOrderFailureEvidence
{
    private const string Key = "TraderVI.FixedOrderCheck";
    // Preserve existing exception types/messages and financial behavior. Only known local checks
    // supply explanations; exception/provider messages are never copied to the durable trace.
    public static InvalidOperationException Tag(string message, BrokerOrderCheck check, string? localExplanation = null,
        BrokerBuyPriceComparison? priceComparison = null)
    {
        var error = new InvalidOperationException(message);
        error.Data[Key] = new BrokerOrderFailure(check, localExplanation,
            check == BrokerOrderCheck.AskAboveDecisionReference ? priceComparison : null);
        return error;
    }
    public static BrokerOrderFailure? Read(Exception error) => error.Data[Key] as BrokerOrderFailure;
}
