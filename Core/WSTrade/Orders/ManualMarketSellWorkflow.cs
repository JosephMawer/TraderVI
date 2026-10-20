#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.WSTrade.Accounts;

namespace Core.WSTrade.Orders;

/// <summary>Only an explicit row action submits. Recovery and refresh only read broker state.</summary>
public sealed class ManualMarketSellWorkflow(IManualSellBroker broker, IManualSellJournal journal,
    TimeProvider? time = null, SemaphoreSlim? orderGate = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = orderGate ?? new(1, 1);
    private readonly HashSet<Guid> usedSelections = [];
    public bool IsConnected => broker.IsConnected;
    public IReadOnlyList<ManualSellRecord> ReadOrders() => journal.ReadSells();

    public Task<ManualSellResult> SellAllAsync(ManualSellSelection selection, CancellationToken ct = default) =>
        SubmitAsync(selection, Guid.NewGuid(), ct);

    /// <summary>Explicit durable opening-plan authority. A known request is recovered, never resubmitted.</summary>
    public async Task<ManualSellResult> SellReviewedAsync(ManualSellSelection selection, Guid requestId, CancellationToken ct = default)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A reviewed durable request identity is required.");
        var saved = journal.ReadSells().SingleOrDefault(o => o.Intent.RequestId == requestId);
        if (saved is not null)
        {
            if (saved.Intent.AccountId != selection.Account.Id || saved.Intent.SecurityId != selection.SecurityId ||
                saved.Intent.Symbol != selection.Symbol || saved.Intent.Quantity != selection.Quantity)
                throw new InvalidOperationException("The opening request identity was already used for a different holding.");
            return await CheckOrderAsync(requestId, ct);
        }
        return await SubmitAsync(selection, requestId, ct);
    }

    private async Task<ManualSellResult> SubmitAsync(ManualSellSelection selection, Guid requestId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selection);
        // A queued second click must never become a second sale after the first finishes.
        if (!await gate.WaitAsync(0, ct)) return new(null, "Another manual order is being checked. Wait for its result.");
        try
        {
            if (!broker.IsConnected || selection.SessionRevision != broker.SessionRevision)
                return new(null, "The connection changed. Sign in and refresh holdings before selling.");
            if (journal.ReadSells().Any(o => o.Intent.RequestId == requestId))
                return new(null, "This reviewed request already exists. Recover its original broker order.");
            if (!selection.IsWholeHolding) return new(null, "Sell all requires a whole-share holding. Use Wealthsimple for fractional shares.");
            if (usedSelections.Contains(selection.SelectionId)) return new(null, "This holding was already submitted. Check the saved sell and refresh holdings.");
            if (journal.HasUnresolvedOrders()) return new(null, "A saved manual order is unresolved. Check its status before selling.");
            var result = await broker.ReadManualSellContextAsync(selection.Account, selection.Symbol, ct);
            if (!broker.IsConnected || selection.SessionRevision != broker.SessionRevision)
                return new(null, "The connection changed during checks. Refresh holdings before selling.");
            if (result.Status != WealthsimpleReadStatus.Success || result.Data == null)
                return new(null, "The holding, quote or pending orders could not be verified. No sell was submitted.");
            var context = result.Data;
            var error = ManualSellChecks.Validate(selection, context, clock.GetUtcNow());
            if (error != null) return new(null, error);
            ct.ThrowIfCancellationRequested();
            var intent = new ManualSellIntent(requestId, context.Security.SourceIdentity!, context.Account.Id, context.Security.Id,
                context.Account.DisplayName, context.Security.Symbol, context.Security.Exchange, (int)selection.Quantity,
                selection.SnapshotAt, context.Quote, clock.GetUtcNow());
            var record = new ManualSellRecord(intent, null, []);
            journal.SaveSell(record); // Durable authority and reserved shares precede any transmission.
            usedSelections.Add(selection.SelectionId);
            WealthsimpleMarketOrderSubmission submission;
            try { submission = await broker.SubmitManualSellAsync(new(context.Account, context.Security, intent.Quantity, intent.RequestId), ct); }
            catch { submission = new(WealthsimpleSubmissionStatus.OutcomeUnknown); }
            record = record with { Submission = submission.Status };
            try { journal.SaveSell(record); }
            catch { return new(record, "The result could not be saved. The original sell is retained. Check status; do not send a replacement."); }
            return new(record, record.Status + ". Actual execution price and proceeds will appear after a status check.");
        }
        finally { gate.Release(); }
    }

    public async Task<ManualSellResult> CheckOrderAsync(Guid requestId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var saved = journal.ReadSells().Single(o => o.Intent.RequestId == requestId);
            var read = await broker.ReadManualSellOrderAsync(saved.Intent, ct);
            if (read.Status != WealthsimpleReadStatus.Success || read.Data == null)
                return new(saved, "The sell result could not be verified. Keep the saved order and check again; do not send a replacement.");
            var observation = read.Data; var previous = saved.Latest;
            var conflict = observation.SubmittedQuantity != saved.Intent.Quantity || observation.FilledQuantity < 0 ||
                observation.FilledQuantity > saved.Intent.Quantity || (observation.Status == "FILLED" && !observation.IsFilled) ||
                observation.RetrievedAt > clock.GetUtcNow() || observation.CreatedAt < saved.Intent.CreatedAt.AddMinutes(-2) ||
                observation.CreatedAt > observation.RetrievedAt || observation.LastFilledAt > observation.RetrievedAt ||
                observation.LastFilledAt < observation.CreatedAt || (observation.FilledQuantity > 0 && observation.LastFilledAt == null) ||
                (saved.Submission == WealthsimpleSubmissionStatus.Rejected && observation.FilledQuantity > 0) ||
                (saved.HasTerminalBrokerResult && previous != null && (previous.IsFilled
                    ? !observation.IsSameCompletedFillAs(previous)
                    : observation.FilledQuantity != previous.FilledQuantity || observation.Status != previous.Status)) ||
                (previous?.FilledQuantity is { } filled && (observation.FilledQuantity == null || observation.FilledQuantity < filled)) ||
                (previous?.IsFilled == true && !observation.IsFilled);
            var observations = saved.Observations.ToList();
            if (previous == null || observation with { RetrievedAt = previous.RetrievedAt } != previous) observations.Add(observation);
            saved = saved with { Observations = observations, LastCheckedAt = clock.GetUtcNow(),
                ConflictingObservation = saved.ConflictingObservation || conflict };
            journal.SaveSell(saved); // Preserve broker fill facts even if the following inventory read fails.
            if (!saved.ConflictingObservation && saved.HasTerminalBrokerResult && saved.InventoryVerifiedAt == null)
            {
                var inventory = await broker.ReadManualSellInventoryAsync(saved.Intent, ct);
                if (inventory.Status == WealthsimpleReadStatus.Success && inventory.Data is { } state && !state.HasPendingOrders &&
                    state.Quantity == saved.Intent.Quantity - observation.FilledQuantity && state.RetrievedAt >= observation.RetrievedAt &&
                    state.RetrievedAt <= clock.GetUtcNow() && clock.GetUtcNow() - state.RetrievedAt <= ManualBuyChecks.ReviewLifetime)
                    saved = saved with { InventoryVerifiedAt = state.RetrievedAt };
            }
            journal.SaveSell(saved);
            return new(saved, saved.ConflictingObservation ? "Broker results conflict. Review this sell at Wealthsimple before another order."
                : saved.IsResolved ? "Sell result and remaining holdings verified. The broker's actual proceeds are shown below."
                : "The sell or holdings update is still pending. Check status again; do not submit a replacement.");
        }
        finally { gate.Release(); }
    }
}
