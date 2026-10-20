#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Operator-reviewed October 20 trial carry. Never an historical closing/opening valuation.</summary>
public sealed record BrokerTrialCarryForward(BrokerAccountRiskBaseline Baseline,
    BrokerAccountReconciliation OriginalAnchor, BrokerAccountReconciliation ReviewedAccount,
    DateOnly Session, DateTimeOffset ReviewedAt, string ReviewedBy, bool NoOutsideChangesConfirmed)
{
    public const string Contract = "October20CashOnlyFirstSessionCarryV1";
    public static DateOnly VerificationDate => new(2026, 10, 19);
    public static DateOnly ExecutionDate => new(2026, 10, 20);
    public static Guid RequestId(Guid baseline) => BrokerManagedStrategy.StableId($"{baseline:D}:{Contract}");
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => BrokerAccountImport.Hash(this);
    public void Validate(ITsxSessionCalendar calendar)
    {
        var bounds = calendar.GetSessionBounds(Session);
        if (Session != ExecutionDate || Baseline.TradingDate != VerificationDate ||
            calendar.GetNextSession(Baseline.TradingDate) != Session ||
            Baseline.BaselineId == Guid.Empty || Baseline.BindingId == Guid.Empty || Baseline.AccountLabel != "TFSA" ||
            Baseline.ObservationAt != OriginalAnchor.SnapshotAt ||
            !NoOutsideChangesConfirmed || string.IsNullOrWhiteSpace(ReviewedBy) ||
            ReviewedAt < bounds.OpenUtc || ReviewedAt >= bounds.CloseUtc ||
            ReviewedAccount.SnapshotAt > ReviewedAt || ReviewedAccount.CheckedAt > ReviewedAt ||
            ReviewedAt - ReviewedAccount.SnapshotAt > TimeSpan.FromSeconds(30) ||
            ReviewedAccount.SnapshotAt < bounds.OpenUtc ||
            OriginalAnchor.SnapshotAt < calendar.GetSessionBounds(VerificationDate).OpenUtc ||
            OriginalAnchor.SnapshotAt >= calendar.GetSessionBounds(VerificationDate).CloseUtc ||
            Baseline.SessionReference.Value <= 0 || Baseline.DrawdownReference.Value <= 0)
            throw new InvalidOperationException("Review the unchanged original October 19 cash-only baseline for the October 20 session, with explicit no-outside-change confirmation during regular trading.");
        RequireUnchangedBook(OriginalAnchor, ReviewedAccount);
    }
    public static void RequireUnchangedBook(BrokerAccountReconciliation anchor, BrokerAccountReconciliation current)
    {
        if (string.IsNullOrWhiteSpace(anchor.IdentityId) || string.IsNullOrWhiteSpace(anchor.AccountId) ||
            current.IdentityId != anchor.IdentityId || current.AccountId != anchor.AccountId ||
            !anchor.HoldingsMatch || !current.HoldingsMatch || anchor.Blocks.Count != 0 || current.Blocks.Count != 0 ||
            anchor.HistoryConflicts.Count != 0 || current.HistoryConflicts.Count != 0 ||
            anchor.Holdings.Any(h => h.BrokerQuantity != 0) || current.Holdings.Any(h => h.BrokerQuantity != 0) ||
            anchor.Cash is not > 0 || current.Cash != anchor.Cash || anchor.AccountValue != anchor.Cash ||
            current.AccountValue != current.Cash || current.BuyingPower is not > 0 ||
            current.CheckedAt < current.SnapshotAt || current.SnapshotAt < anchor.SnapshotAt ||
            anchor.Orders.Any(o => !o.IsTerminal) || current.Orders.Any(o => !o.IsTerminal) ||
            anchor.Orders.Select(o => o.ExternalId).Distinct().Count() != anchor.Orders.Count ||
            current.Orders.Count != anchor.Orders.Count ||
            current.Orders.Select(o => o.ExternalId).Distinct().Count() != current.Orders.Count ||
            anchor.Orders.Any(o => !current.Orders.Any(n => n.ExternalId == o.ExternalId && n.IsSameOrPostedSuccessor(o))))
            throw new InvalidOperationException("Preserve the original cash-only book and all order identities/fill facts. Changed cash, inventory, pending, added or missing orders block the trial carry.");
    }
    public static BrokerRiskContinuationRequest Prepare(BrokerAccountRiskBaseline baseline,
        BrokerImportReceipt originalImport, BrokerAccountRiskState state, BrokerAccountReconciliation current,
        string reviewedBy, bool confirmed, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        if (originalImport.RequestId != baseline.ImportRequestId || originalImport.BindingId != baseline.BindingId ||
            !originalImport.Plan.CanImport)
            throw new InvalidOperationException("Recover the original immutable import used by the saved baseline.");
        var evidence = new BrokerTrialCarryForward(baseline, originalImport.Plan.Observation, current,
            ExecutionDate, now, reviewedBy, confirmed);
        var request = new BrokerRiskContinuationRequest(RequestId(baseline.BaselineId), baseline.BaselineId,
            state.RevisionId, BrokerRiskContinuationKind.TrialCarryForward, null, null, null) { TrialCarry = evidence };
        _ = BrokerAccountRiskContinuation.Apply(state, request, calendar, now);
        return request;
    }
    internal static BrokerAccountRiskState Apply(BrokerAccountRiskState state, BrokerRiskContinuationRequest request,
        ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var carry = request.TrialCarry!;
        carry.Validate(calendar);
        if (request.RequestId != RequestId(carry.Baseline.BaselineId) || request.BaselineId != carry.Baseline.BaselineId ||
            carry.ReviewedAt > now || now - carry.ReviewedAt > TimeSpan.FromSeconds(30) ||
            BrokerAccountImport.Hash(state) != BrokerAccountImport.Hash(BrokerAccountRiskContinuation.Start(carry.Baseline)) ||
            state.DailyLossHold || state.CapitalReviewRequired || state.TrialCarry is not null)
            throw new InvalidOperationException("The trial carry preserves the original unadvanced risk state and all holds. Changed, held or already-carried history requires separate recovery.");
        return state with { RevisionId = request.RequestId, TradingDate = carry.Session,
            Through = carry.ReviewedAccount.SnapshotAt, TrialCarry = carry };
    }
    public override string ToString() => "Reviewed trial-only cash carry (financial data omitted)";
}
