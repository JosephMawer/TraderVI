#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Text.Json.Serialization;

namespace Core.Trader.Promotion;

/// <summary>One dated follow-on review. Expiry observation is not a qualified closing valuation.</summary>
public sealed record BrokerOctober22Continuation(Guid SessionId, Guid PreviousRevisionId,
    string PreviousSessionHash, DateTimeOffset ExpiryObservedAt, BrokerAccountRiskState RiskAnchor,
    BrokerNoTransferSessionScope PreviousScope, DateTimeOffset ConfirmedAt, string ConfirmedBy,
    bool NoOutsideChangesConfirmed, bool PreOpenApproved)
{
    public const string Contract = "October22PortfolioRulesOnlyContinuationV1";
    public static DateOnly ExecutionDate => new(2026, 10, 22);
    public static Guid RequestId(Guid baseline) => BrokerManagedStrategy.StableId($"{baseline:D}:{Contract}");
    public static void RequireSameDefinition(BrokerManagedStrategy previous, BrokerManagedStrategy current) =>
        BrokerOctober21Continuation.RequireSameDefinition(previous, current);
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    [JsonIgnore] public BrokerAccountRiskState PreviousRisk => RiskAnchor with
        { TrialCarry = PreviousScope.TrialCarry, Continuation = PreviousScope.Continuation };
    [JsonIgnore] public DateTimeOffset PurchaseExpiresAt => ReviewedTsxSessionCalendar.At(ExecutionDate, new TimeOnly(16, 0));

    public static bool IsPreOpenReview(ITsxSessionCalendar calendar, DateTimeOffset now) =>
        DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) == ExecutionDate &&
        now < calendar.GetSessionBounds(ExecutionDate).OpenUtc;

    public void Validate(ITsxSessionCalendar calendar)
    {
        PreviousScope.Validate(calendar);
        var bounds = calendar.GetSessionBounds(ExecutionDate);
        if (SessionId == Guid.Empty || PreviousRevisionId == Guid.Empty || !CompleteTradingStrategy.IsHash(PreviousSessionHash) ||
            !NoOutsideChangesConfirmed || string.IsNullOrWhiteSpace(ConfirmedBy) || ConfirmedBy.Length > 128 ||
            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(ConfirmedAt.UtcDateTime)) != ExecutionDate ||
            ConfirmedAt >= bounds.CloseUtc || PreOpenApproved != (ConfirmedAt < bounds.OpenUtc) ||
            PurchaseExpiresAt != bounds.CloseUtc ||
            ExpiryObservedAt < calendar.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate).CloseUtc ||
            ExpiryObservedAt > ConfirmedAt || PreviousScope.Session != BrokerOctober21Continuation.ExecutionDate ||
            calendar.GetNextSession(PreviousScope.Session) != ExecutionDate || PreviousScope.October22 is not null ||
            PreviousScope.Continuation is null || PreviousScope.TrialCarry is null ||
            !PreviousScope.LimitedFirstSessionTrial || !PreviousScope.FullFillOnlyNativeTrial ||
            RiskAnchor.TrialCarry is not null || RiskAnchor.Continuation is not null || RiskAnchor.October22 is not null ||
            PreviousRisk.TradingDate != PreviousScope.Session || PreviousRisk.BindingId != PreviousScope.BindingId ||
            PreviousRisk.BaselineId != PreviousScope.BaselineId || PreviousRisk.DailyLossHold || PreviousRisk.CapitalReviewRequired ||
            PreviousRisk.SessionReference != PreviousScope.TrialCarry.Baseline.SessionReference ||
            PreviousRisk.DrawdownReference != PreviousScope.TrialCarry.Baseline.DrawdownReference)
            throw new InvalidOperationException("October 22 review requires the expired October 21 scope, unchanged original risk references, no holds and explicit no-outside-change confirmation on October 22 before 4 PM.");
    }

    public void RequirePrior(BrokerManagedSession prior, ITsxSessionCalendar calendar)
        => RequirePrior(prior, calendar, null);
    internal void RequirePrior(BrokerManagedSession prior, ITsxSessionCalendar calendar, BrokerHistoricalStateHash? historical)
    {
        Validate(calendar);
        if (prior.October22 is not null || prior.SessionId != SessionId || prior.RevisionId != PreviousRevisionId ||
            !BrokerHistoricalStateHash.Matches(prior, PreviousSessionHash, historical) || prior.SavedAt > ExpiryObservedAt ||
            prior.Event != "StrategyEvaluated" || !prior.IsOngoing || prior.ActivatedAt is null ||
            prior.Continuation is null || prior.SourceSwitch is null ||
            prior.Strategy.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 ||
            prior.Phase is not (BrokerManagedPhase.Active or BrokerManagedPhase.Paused) ||
            prior.ReviewInvalidated || prior.TrialOrderReviewRequired || prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired ||
            !prior.Orders.IsEmpty || !prior.Protection.IsEmpty || !prior.Account.Positions.IsEmpty ||
            BrokerAccountImport.Hash(prior.EffectiveReview.NoTransferScope) != BrokerAccountImport.Hash(PreviousScope) ||
            BrokerAccountImport.Hash(prior.Account.Risk) != BrokerAccountImport.Hash(PreviousRisk))
            throw new InvalidOperationException("Recover the exact existing cash-only Portfolio rules only evaluation with no automatic orders, positions, holds or changed risk history. Other states require separate recovery.");
        BrokerTrialCarryForward.RequireUnchangedBook(PreviousScope.CashOnlyAnchor, prior.Account.Import.Plan.Observation);
    }

    public BrokerNoTransferSessionScope Scope(ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        return PreviousScope with { ScopeId = RequestId(PreviousRisk.BaselineId), Session = ExecutionDate,
            ApprovedAt = ConfirmedAt, ApprovedBy = ConfirmedBy, PurchaseExpiresAt = PurchaseExpiresAt, October22 = this };
    }
    public BrokerRiskContinuationRequest RiskRequest() => new(RequestId(PreviousRisk.BaselineId), PreviousRisk.BaselineId,
        PreviousRisk.RevisionId, BrokerRiskContinuationKind.October22Continuation, null, null, null) { October22 = this };
    public BrokerAccountRiskState Apply(BrokerAccountRiskState prior, ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        if (BrokerAccountImport.Hash(prior) != BrokerAccountImport.Hash(PreviousRisk))
            throw new InvalidOperationException("Risk history advanced. Preserve original references and holds for recovery.");
        // Neither Through nor LastCompletedSession advances; no financial close/open is invented.
        return prior with { RevisionId = RequestId(prior.BaselineId), TradingDate = ExecutionDate, October22 = this };
    }
    public static bool SourceReady(BrokerOctober22Continuation review, DateTimeOffset updated,
        ITsxSessionCalendar calendar, DateTimeOffset now, bool validPreOpenDailySource) => updated <= now &&
        (review.PreOpenApproved && IsPreOpenReview(calendar, now)
            ? validPreOpenDailySource && updated >= calendar.GetSessionBounds(review.PreviousScope.Session).OpenUtc
            : now - updated <= TimeSpan.FromMinutes(2));
    public override string ToString() => "October 22 review (financial data omitted)";
}
