#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Trader.Promotion;

/// <summary>Explicit permission for one selected regular session. Links, rather than embeds, earlier renewals.</summary>
public sealed record BrokerSessionContinuation(Guid SessionId, Guid PreviousRevisionId, string PreviousSessionHash,
    string PreviousRiskHash, Guid PreviousScopeId, DateOnly PreviousSession, string PreviousScopeHash,
    DateTimeOffset ExpiryObservedAt, BrokerAccountRiskState RiskAnchor, BrokerNoTransferSessionScope OriginScope,
    DateOnly Session, DateTimeOffset PurchaseExpiresAt, DateTimeOffset ConfirmedAt, string ConfirmedBy, bool NoOutsideChangesConfirmed, bool PreOpenApproved)
{
    public const string Contract = "ReviewedSessionContinuationV1";
    public static Guid RequestId(Guid baseline, DateOnly session) =>
        BrokerManagedStrategy.StableId($"{baseline:D}:{Contract}:{session:yyyy-MM-dd}");
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    internal static BrokerAccountRiskState Anchor(BrokerAccountRiskState risk) => risk with
        { TrialCarry = null, Continuation = null, October22 = null, SessionContinuation = null };
    // A new contract uses a canonical state fingerprint; legacy process-dependent approval hashes stay unchanged.
    internal static string StateHash(BrokerManagedSession state) => DelphiLiveSnapshotCodec.Fingerprint(JsonSerializer.Serialize(state));
    public DateTimeOffset PurchaseExpiry(ITsxSessionCalendar calendar) => calendar.GetSessionBounds(Session).CloseUtc;
    public static bool IsPreOpenReview(ITsxSessionCalendar calendar, DateTimeOffset now) =>
        calendar.IsRegularSession(DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime))) &&
        now < calendar.GetSessionBounds(DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime))).OpenUtc;

    public static BrokerSessionContinuation Create(BrokerManagedSession prior, DateOnly session,
        DateTimeOffset now, string reviewer, bool noOutsideChanges, ITsxSessionCalendar calendar)
    {
        var previous = prior.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the original supervised scope.");
        var value = new BrokerSessionContinuation(prior.SessionId, prior.RevisionId, StateHash(prior),
            BrokerAccountImport.Hash(prior.Account.Risk), previous.ScopeId, previous.Session, BrokerAccountImport.Hash(previous),
            now, Anchor(prior.Account.Risk), previous.SessionContinuation?.OriginScope ?? previous,
            session, calendar.GetSessionBounds(session).CloseUtc, now, reviewer, noOutsideChanges, IsPreOpenReview(calendar, now));
        value.RequirePrior(prior, calendar); return value;
    }
    public void Validate(ITsxSessionCalendar calendar)
    {
        if (OriginScope.SessionContinuation is not null)
            throw new InvalidOperationException("The continuation anchor cannot embed earlier session renewals.");
        OriginScope.Validate(calendar);
        var bounds = calendar.GetSessionBounds(Session);
        if (SessionId == Guid.Empty || PreviousRevisionId == Guid.Empty || PreviousScopeId == Guid.Empty ||
            !CompleteTradingStrategy.IsHash(PreviousSessionHash) || !CompleteTradingStrategy.IsHash(PreviousRiskHash) ||
            !CompleteTradingStrategy.IsHash(PreviousScopeHash) || !NoOutsideChangesConfirmed ||
            string.IsNullOrWhiteSpace(ConfirmedBy) || ConfirmedBy.Length > 128 ||
            Session <= PreviousSession || PreviousSession < OriginScope.Session ||
            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(ConfirmedAt.UtcDateTime)) != Session ||
            ConfirmedAt >= bounds.CloseUtc || PurchaseExpiresAt != bounds.CloseUtc || PreOpenApproved != (ConfirmedAt < bounds.OpenUtc) ||
            ExpiryObservedAt < calendar.GetSessionBounds(PreviousSession).CloseUtc || ExpiryObservedAt > ConfirmedAt ||
            OriginScope.Continuation is null || OriginScope.TrialCarry is null ||
            !OriginScope.LimitedFirstSessionTrial || !OriginScope.FullFillOnlyNativeTrial ||
            RiskAnchor.TrialCarry is not null || RiskAnchor.Continuation is not null || RiskAnchor.October22 is not null ||
            RiskAnchor.SessionContinuation is not null || RiskAnchor.TradingDate != PreviousSession ||
            RiskAnchor.BindingId != OriginScope.BindingId || RiskAnchor.BaselineId != OriginScope.BaselineId ||
            RiskAnchor.DailyLossHold || RiskAnchor.CapitalReviewRequired ||
            RiskAnchor.SessionReference != OriginScope.TrialCarry.Baseline.SessionReference ||
            RiskAnchor.DrawdownReference != OriginScope.TrialCarry.Baseline.DrawdownReference)
            throw new InvalidOperationException("Review a later regular session on its date before close, preserving original risk references and confirming no outside account changes across the entire gap.");
    }
    public void RequirePrior(BrokerManagedSession prior, ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        var previous = prior.EffectiveReview.NoTransferScope;
        if (prior.SessionId != SessionId || prior.RevisionId != PreviousRevisionId || StateHash(prior) != PreviousSessionHash ||
            prior.SavedAt > ExpiryObservedAt || !prior.IsOngoing || prior.ActivatedAt is null ||
            prior.Continuation is null || prior.SourceSwitch is null || prior.Strategy.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 ||
            prior.Phase is not (BrokerManagedPhase.Active or BrokerManagedPhase.Paused) || prior.ReviewInvalidated ||
            prior.TrialOrderReviewRequired || prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired ||
            !prior.Orders.IsEmpty || !prior.Protection.IsEmpty || !prior.Account.Positions.IsEmpty ||
            previous?.ScopeId != PreviousScopeId || previous.Session != PreviousSession ||
            BrokerAccountImport.Hash(previous) != PreviousScopeHash ||
            BrokerAccountImport.Hash(previous.SessionContinuation?.OriginScope ?? previous) != BrokerAccountImport.Hash(OriginScope) ||
            BrokerAccountImport.Hash(prior.Account.Risk) != PreviousRiskHash ||
            BrokerAccountImport.Hash(Anchor(prior.Account.Risk)) != BrokerAccountImport.Hash(RiskAnchor))
            throw new InvalidOperationException("Recover the exact existing cash-only Portfolio rules only assignment; changed orders, positions, holds, source or risk need separate recovery.");
        BrokerTrialCarryForward.RequireUnchangedBook(OriginScope.CashOnlyAnchor, prior.Account.Import.Plan.Observation);
    }
    public BrokerNoTransferSessionScope Scope(ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        return OriginScope with { ScopeId = RequestId(RiskAnchor.BaselineId, Session), Session = Session,
            ApprovedAt = ConfirmedAt, ApprovedBy = ConfirmedBy, PurchaseExpiresAt = PurchaseExpiry(calendar), SessionContinuation = this };
    }
    public BrokerRiskContinuationRequest RiskRequest() => new(RequestId(RiskAnchor.BaselineId, Session), RiskAnchor.BaselineId,
        RiskAnchor.RevisionId, BrokerRiskContinuationKind.ReviewedSessionContinuation, null, null, null) { SessionContinuation = this };
    public BrokerAccountRiskState ExpectedRisk(ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        return RiskAnchor with { RevisionId = RequestId(RiskAnchor.BaselineId, Session), TradingDate = Session,
            TrialCarry = OriginScope.TrialCarry, Continuation = OriginScope.Continuation, October22 = OriginScope.October22,
            SessionContinuation = this };
    }
    public BrokerAccountRiskState Apply(BrokerAccountRiskState prior, ITsxSessionCalendar calendar)
    {
        if (BrokerAccountImport.Hash(prior) != PreviousRiskHash ||
            BrokerAccountImport.Hash(Anchor(prior)) != BrokerAccountImport.Hash(RiskAnchor))
            throw new InvalidOperationException("Risk history advanced; preserve the original request and financial references.");
        return ExpectedRisk(calendar); // Through/LastCompletedSession and financial references never advance here.
    }
    public static void RequireSameDefinition(BrokerManagedStrategy previous, BrokerManagedStrategy current) =>
        BrokerOctober21Continuation.RequireSameDefinition(previous, current);
    public static bool SourceReady(BrokerSessionContinuation review, DateTimeOffset updated, ITsxSessionCalendar calendar,
        DateTimeOffset now, bool validPreOpenDailySource) => DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) == review.Session &&
        now < review.PurchaseExpiresAt && updated <= now &&
        (review.PreOpenApproved && IsPreOpenReview(calendar, now)
            ? validPreOpenDailySource && updated >= calendar.GetSessionBounds(review.OriginScope.Session).OpenUtc
            : now - updated <= TimeSpan.FromMinutes(2));
    public override string ToString() => "Selected-session review (financial data omitted)";
}
