#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Text.Json.Serialization;

namespace Core.Trader.Promotion;

/// <summary>A single explicit continuation, never recurring authority or a reconstructed opening value.</summary>
public sealed record BrokerOctober21Continuation(Guid SessionId, Guid PreviousRevisionId,
    string PreviousSessionHash, DateTimeOffset CloseObservedAt, BrokerAccountRiskState RiskAnchor,
    BrokerNoTransferSessionScope PreviousScope, DateTimeOffset ConfirmedAt, string ConfirmedBy,
    bool NoOutsideChangesConfirmed)
{
    public const string Contract = "October21CashOnlyContinuationV1";
    public DateTimeOffset PurchaseExpiresAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PreOpenApproved { get; init; }
    public static DateOnly ExecutionDate => new(2026, 10, 21);
    public static Guid RequestId(Guid baselineId) => BrokerManagedStrategy.StableId($"{baselineId:D}:{Contract}");
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    // The immutable carry already belongs to PreviousScope. Store it once, then reconstruct the
    // exact prior state for hash comparisons; never enlarge the existing risk-event size limit.
    [JsonIgnore] public BrokerAccountRiskState PreviousRisk => RiskAnchor with { TrialCarry = PreviousScope.TrialCarry };

    public void Validate(ITsxSessionCalendar calendar)
    {
        PreviousScope.Validate(calendar);
        var bounds = calendar.GetSessionBounds(ExecutionDate);
        if (SessionId == Guid.Empty || PreviousRevisionId == Guid.Empty ||
            !CompleteTradingStrategy.IsHash(PreviousSessionHash) || !NoOutsideChangesConfirmed ||
            string.IsNullOrWhiteSpace(ConfirmedBy) || ConfirmedAt >= bounds.CloseUtc ||
            (PreOpenApproved
                ? DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(ConfirmedAt.UtcDateTime)) != ExecutionDate || ConfirmedAt >= bounds.OpenUtc
                : ConfirmedAt < bounds.OpenUtc) ||
            PurchaseExpiresAt != bounds.CloseUtc ||
            RiskAnchor.TrialCarry is not null || RiskAnchor.Continuation is not null ||
            CloseObservedAt < calendar.GetSessionBounds(BrokerTrialCarryForward.ExecutionDate).CloseUtc || CloseObservedAt > ConfirmedAt ||
            calendar.GetNextSession(PreviousScope.Session) != ExecutionDate ||
            PreviousScope.Session != BrokerTrialCarryForward.ExecutionDate || PreviousScope.Continuation is not null ||
            !PreviousScope.LimitedFirstSessionTrial || !PreviousScope.FullFillOnlyNativeTrial ||
            PreviousScope.TrialCarry is null || PreviousRisk.Continuation is not null ||
            PreviousRisk.TradingDate != PreviousScope.Session || PreviousRisk.BindingId != PreviousScope.BindingId ||
            PreviousRisk.BaselineId != PreviousScope.BaselineId ||
            PreviousRisk.TrialCarry?.Fingerprint != PreviousScope.TrialCarry.Fingerprint ||
            PreviousRisk.DailyLossHold || PreviousRisk.CapitalReviewRequired ||
            PreviousRisk.SessionReference != PreviousScope.TrialCarry.Baseline.SessionReference ||
            PreviousRisk.DrawdownReference != PreviousScope.TrialCarry.Baseline.DrawdownReference)
            throw new InvalidOperationException("October 21 continuation requires the original unchanged trial references, no holds, explicit no-outside-change confirmation and saved October 20 close evidence.");
    }

    public void RequirePrior(BrokerManagedSession prior, ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        if (prior.SessionId != SessionId || prior.RevisionId != PreviousRevisionId ||
            BrokerAccountImport.Hash(prior) != PreviousSessionHash || prior.SavedAt != CloseObservedAt ||
            prior.Event != "StrategyEvaluated" || prior.SavedAt < PreviousScope.PurchaseExpiresAt ||
            BrokerAccountImport.Hash(prior.EffectiveReview.NoTransferScope) != BrokerAccountImport.Hash(PreviousScope) ||
            BrokerAccountImport.Hash(prior.Account.Risk) != BrokerAccountImport.Hash(PreviousRisk) ||
            prior.Continuation is not null || !prior.IsOngoing || prior.ActivatedAt is null ||
            prior.Phase != BrokerManagedPhase.Active || prior.ReviewInvalidated || prior.TrialOrderReviewRequired ||
            !prior.Orders.IsEmpty || !prior.Protection.IsEmpty || !prior.Account.Positions.IsEmpty ||
            prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired ||
            prior.Account.Risk.SessionReference != PreviousRisk.SessionReference ||
            prior.Account.Risk.DrawdownReference != PreviousRisk.DrawdownReference ||
            prior.Account.Risk.TrialCarry?.Fingerprint != PreviousRisk.TrialCarry?.Fingerprint)
            throw new InvalidOperationException("Recover the exact saved, expired October 20 evaluation. Changed revisions, orders, positions, references or holds require separate review.");
        BrokerTrialCarryForward.RequireUnchangedBook(PreviousScope.CashOnlyAnchor, prior.Account.Import.Plan.Observation);
    }

    public BrokerNoTransferSessionScope Scope(ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        return PreviousScope with
        {
            ScopeId = RequestId(PreviousRisk.BaselineId), Session = ExecutionDate, ApprovedAt = ConfirmedAt,
            ApprovedBy = ConfirmedBy, PurchaseExpiresAt = PurchaseExpiresAt, Continuation = this
        };
    }

    public BrokerRiskContinuationRequest RiskRequest() => new(RequestId(PreviousRisk.BaselineId),
        PreviousRisk.BaselineId, PreviousRisk.RevisionId, BrokerRiskContinuationKind.SupervisedContinuation, null, null, null)
        { Continuation = this };

    public BrokerAccountRiskState Apply(BrokerAccountRiskState prior, ITsxSessionCalendar calendar)
    {
        Validate(calendar);
        if (BrokerAccountImport.Hash(prior) != BrokerAccountImport.Hash(PreviousRisk))
            throw new InvalidOperationException("Risk history advanced. Continuation cannot reset references, memory or holds.");
        // Through and LastCompletedSession retain their true meanings. The close revision is a permission
        // expiry observation, not a newly qualified financial closing/opening valuation.
        return prior with { RevisionId = RequestId(prior.BaselineId), TradingDate = ExecutionDate, Continuation = this };
    }

    public static void RequireSameDefinition(BrokerManagedStrategy original, BrokerManagedStrategy current)
    {
        original.Validate(); current.Validate();
        var sameBuild = original with { Strategy = original.Strategy with { ImplementationIdentity = current.Strategy.ImplementationIdentity } };
        if (sameBuild.Fingerprint != current.Fingerprint)
            throw new InvalidOperationException("The Normal profile, policy, daily/model definition or source identity changed. This continuation cannot silently replace the original strategy; separate review is required.");
    }
    public override string ToString() => "October 21 continuation (financial data omitted)";

    public static bool IsPreOpenReview(ITsxSessionCalendar calendar, DateTimeOffset now) =>
        DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) == ExecutionDate &&
        now < calendar.GetSessionBounds(ExecutionDate).OpenUtc;

    public static bool SourceReady(BrokerOctober21Continuation continuation, DateTimeOffset updated,
        ITsxSessionCalendar calendar, DateTimeOffset now, bool validPreOpenDailySource)
    {
        if (updated > now) return false;
        if (continuation.PreOpenApproved && IsPreOpenReview(calendar, now))
            return validPreOpenDailySource && updated >= calendar.GetSessionBounds(continuation.PreviousScope.Session).OpenUtc;
        return now - updated <= TimeSpan.FromMinutes(2);
    }
}

public sealed record BrokerManagedContinuation(BrokerExecutionApproval Approval, BrokerRiskContinuationRequest RiskRequest)
{
    // Added for the separately dated October 22 review; absent historical records keep their hashes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ApprovedAt { get; init; }
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
}
