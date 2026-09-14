#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Orders;

/// <summary>One operator-reviewed manual order at a time. No timer/startup path can submit.</summary>
public sealed class ManualMarketBuyWorkflow(IManualBuyBroker broker, IManualBuyJournal journal,
    IManualBuySafetySource safety, TimeProvider? time = null, SemaphoreSlim? orderGate = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = orderGate ?? new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> usedReviews = new();
    public IReadOnlyList<ManualBuyRecord> ReadOrders() => journal.ReadAll();
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadAccountsAsync(CancellationToken ct) => broker.ReadTfsaAccountsAsync(ct);
    public bool IsConnected => broker.IsConnected;
    public long SessionRevision => broker.SessionRevision;

    public async Task<ManualBuyPreparation> PrepareAsync(WealthsimpleTfsaAccount account, ManualBuyOrigin origin, CancellationToken ct = default)
    {
        origin.Validate();
        await gate.WaitAsync(ct);
        try
        {
            if (!broker.IsConnected) return new(null, "Sign in with Wealthsimple, then refresh this ticket.");
            if (journal.HasUnresolvedOrders()) return new(null, "A saved manual buy or sell is unresolved. Open its order history and check status first.");
            var revision = broker.SessionRevision;
            await using var checks = await safety.AcquireAsync(ct);
            if (checks.BlockReason != null) return new(null, checks.BlockReason);
            var result = await broker.ReadManualBuyContextAsync(account, origin.Symbol, ct);
            if (revision != broker.SessionRevision || !broker.IsConnected) return new(null, "The connection changed. Refresh the ticket.");
            if (result.Status != WealthsimpleReadStatus.Success || result.Data == null) return new(null, ReadFailure(result.Status, result.Stage));
            var context = result.Data;
            if (origin.ExpectedBrokerSecurityId != null && origin.ExpectedBrokerSecurityId != context.Security.Id)
                return new(null, "The Canadian search result is not the selected broker holding. This instrument cannot be bought through this ticket.");
            var error = ManualBuyChecks.Validate(context, clock.GetUtcNow());
            // Keep verified facts visible even if the market is closed or cash/orders block readiness.
            return new(new(context, origin, revision, clock.GetUtcNow()), error ?? "Review your share quantity and estimated cost, then submit when ready.");
        }
        finally { gate.Release(); }
    }

    public string? ValidateReview(PreparedManualBuy? review, int? quantity)
    {
        if (review == null) return "Refresh the ticket to check the account and market.";
        if (usedReviews.ContainsKey(review.ReviewId)) return "This review has already been submitted. Check Orders before starting a new review.";
        if (!broker.IsConnected || review.SessionRevision != broker.SessionRevision) return "The connection changed. Refresh the ticket.";
        if (clock.GetUtcNow() < review.PreparedAt || clock.GetUtcNow() - review.PreparedAt > ManualBuyChecks.ReviewLifetime)
            return "The review expired. Refresh the ticket before submitting.";
        if (!quantity.HasValue || quantity < 1) return "Enter a positive whole number of shares.";
        return ManualBuyChecks.Validate(review.Context, clock.GetUtcNow(), quantity);
    }

    public async Task<ManualBuySubmissionResult> SubmitAsync(PreparedManualBuy review, int quantity, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var error = ValidateReview(review, quantity);
            if (error != null) return new(null, null, error);
            if (journal.HasUnresolvedOrders()) return new(null, null, "A saved buy or sell is unresolved. Check its status before another purchase.");
            // The SQL shared fences remain held until submission returns, so a completed pause or
            // financial hold cannot be silently overtaken by this in-flight approval.
            await using var checks = await safety.AcquireAsync(ct);
            if (checks.BlockReason != null) return new(null, null, checks.BlockReason);
            var fresh = await broker.ReadManualBuyContextAsync(review.Context.Account, review.Origin.Symbol, ct);
            if (fresh.Status != WealthsimpleReadStatus.Success || fresh.Data == null)
                return new(null, null, ReadFailure(fresh.Status, fresh.Stage));
            if (review.SessionRevision != broker.SessionRevision || !broker.IsConnected ||
                (review.Origin.ExpectedBrokerSecurityId != null && review.Origin.ExpectedBrokerSecurityId != fresh.Data.Security.Id) ||
                fresh.Data.Security.Id != review.Context.Security.Id || fresh.Data.Security.SourceIdentity != review.Context.Security.SourceIdentity ||
                fresh.Data.Account.Id != review.Context.Account.Id)
                return new(null, null, "The account, connection or instrument changed. Refresh and review again.");
            var current = new PreparedManualBuy(fresh.Data, review.Origin, broker.SessionRevision, clock.GetUtcNow());
            error = ManualBuyChecks.Validate(fresh.Data, clock.GetUtcNow(), quantity);
            if (error != null) return new(null, current, error);
            if (fresh.Data.Quote.Ask > review.Context.Quote.Ask)
                return new(null, current, "The reference ask increased. Review the updated estimated cost and click Buy again if you still want this order.");
            ct.ThrowIfCancellationRequested();
            var intent = new ManualBuyIntent(Guid.NewGuid(), fresh.Data.Security.SourceIdentity!, fresh.Data.Account.Id,
                fresh.Data.Security.Id, fresh.Data.Account.DisplayName, fresh.Data.Security.Symbol, fresh.Data.Security.Exchange,
                quantity, review.Origin, review.Context.Quote.Ask, fresh.Data.Quote, fresh.Data.AvailableCash!.Value, clock.GetUtcNow());
            var saved = new ManualBuyRecord(intent, null, []);
            journal.Save(saved); // A failure here prevents all transmission.
            usedReviews.TryAdd(review.ReviewId, 0);
            WealthsimpleMarketBuySubmission result;
            try
            {
                result = await broker.SubmitManualBuyAsync(new(fresh.Data.Account, fresh.Data.Security, quantity, intent.RequestId), ct);
            }
            catch { result = new(WealthsimpleSubmissionStatus.OutcomeUnknown); }
            saved = saved with { Submission = result.Status };
            try { journal.Save(saved); }
            catch { return new(saved, null, "The order outcome could not be saved. Its original intent is retained; check this order's status. Do not submit a replacement."); }
            return new(saved, null, saved.Status + (saved.IsResolved ? "." : ". Check the broker result below; closing this ticket does not cancel the order."));
        }
        finally { gate.Release(); }
    }

    public async Task<(ManualBuyRecord Order, string Message)> CheckOrderAsync(Guid requestId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var saved = journal.ReadAll().Single(o => o.Intent.RequestId == requestId);
            var result = await broker.ReadManualBuyOrderAsync(saved.Intent, ct);
            if (result.Status != WealthsimpleReadStatus.Success || result.Data == null)
                return (saved, "Order status could not be verified. Keep this saved order and check again; do not send a replacement.");
            var observation = result.Data;
            var previous = saved.Latest;
            bool conflict = observation.SubmittedQuantity != saved.Intent.Quantity ||
                observation.RetrievedAt > clock.GetUtcNow() || observation.CreatedAt < saved.Intent.CreatedAt.AddMinutes(-2) ||
                observation.CreatedAt > observation.RetrievedAt || observation.LastFilledAt > observation.RetrievedAt ||
                observation.LastFilledAt < observation.CreatedAt ||
                (saved.Submission == WealthsimpleSubmissionStatus.Rejected && observation.FilledQuantity > 0) ||
                (previous?.FilledQuantity is { } previousFilled && (observation.FilledQuantity == null || observation.FilledQuantity < previousFilled)) ||
                (previous?.IsFilled == true && !observation.IsFilled);
            // Retain changes to cumulative facts as observations, never incremental local fills.
            var observations = saved.Observations.ToList();
            if (previous == null || observation with { RetrievedAt = previous.RetrievedAt } != previous) observations.Add(observation);
            saved = saved with { Observations = observations, LastCheckedAt = clock.GetUtcNow(), ConflictingObservation = saved.ConflictingObservation || conflict };
            journal.Save(saved);
            return (saved, saved.ConflictingObservation ? "Broker observations conflict. Review this order at Wealthsimple before further purchases."
                : saved.IsResolved ? "Broker result saved. This is a manual holding; manage any sale yourself."
                : "The order is still pending or has incomplete fill details. Check again; do not submit a replacement.");
        }
        finally { gate.Release(); }
    }
    private static string ReadFailure(WealthsimpleReadStatus status, string? stage) => status switch
    {
        WealthsimpleReadStatus.NotAuthenticated => "Sign in with Wealthsimple, then refresh the ticket.",
        WealthsimpleReadStatus.AccountUnavailable => "The selected TFSA is no longer available. Reload accounts.",
        WealthsimpleReadStatus.RateLimited => "Wealthsimple has limited requests. Wait before refreshing again.",
        _ => "Could not verify " + (stage ?? "the broker checks") + ". No order was submitted."
    };
}
