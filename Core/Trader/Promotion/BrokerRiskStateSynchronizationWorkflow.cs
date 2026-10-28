#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Evidence for mirroring existing risk-journal events, without granting trading authority.</summary>
public sealed record BrokerRiskStateSynchronization(Guid PreviousRevisionId,
    ImmutableArray<BrokerRiskContinuationReceipt> Receipts);

/// <summary>Reconciles an unchanged ongoing account while the execution host is stopped.</summary>
public sealed class BrokerRiskStateSynchronizationWorkflow(IBrokerManagedSessionStore sessions,
    IBrokerAccountRiskContinuationStore risks, IBrokerManagedAccountSource source,
    IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    internal const int MaximumReceipts = 64;

    public async Task<BrokerManagedSession> SynchronizeAsync(Guid binding, CancellationToken ct = default)
    {
        RequireHostOff();
        await using var lease = await sessions.AcquireAsync(binding, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing account assignment first.");
        if (prior.OngoingManagement is null) return prior; // Legacy dated reviews retain their existing path.
        BrokerImplementationReviewWorkflow.RequireSession(prior, calendar, clock.GetUtcNow());
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        RequireHostOff();
        if (BrokerAccountImport.Hash(prior.Account.Risk) == BrokerAccountImport.Hash(account.Risk)) return prior;

        var receipts = new List<BrokerRiskContinuationReceipt>();
        var cursor = account.Risk.RevisionId;
        var seen = new HashSet<Guid>();
        while (cursor != prior.Account.Risk.RevisionId)
        {
            if (cursor is not { } id || !seen.Add(id) || receipts.Count >= MaximumReceipts)
                throw new InvalidOperationException("Risk journal does not provide a bounded continuation of this saved session.");
            var receipt = await risks.FindAsync(id, ct)
                ?? throw new InvalidOperationException("Recover the missing original risk receipt before reviewing the build.");
            if (receipt.Request.RequestId != id || receipt.State.RevisionId != id)
                throw new InvalidOperationException("Risk receipt identity differs from the requested journal revision.");
            receipts.Add(receipt); cursor = receipt.Request.PreviousRevisionId;
        }
        receipts.Reverse();
        var now = clock.GetUtcNow();
        var next = prior with
        {
            Revision = prior.Revision + 1,
            RevisionId = BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:RiskStateSynchronized:{account.Risk.RevisionId:D}"),
            PreviousRevisionId = prior.RevisionId, Event = "RiskStateSynchronized", SavedAt = now, Account = account,
            RiskSynchronization = new(prior.RevisionId, receipts.ToImmutableArray())
        };
        ValidateAppend(prior, next, calendar);
        RequireHostOff(); ct.ThrowIfCancellationRequested();
        await lease.AppendAsync(next, ct);
        return next;
    }

    internal static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar)
    {
        var proof = next.RiskSynchronization ?? throw new InvalidOperationException("Original risk continuation receipts are required.");
        if (prior.OngoingManagement is null || proof.PreviousRevisionId != prior.RevisionId ||
            proof.Receipts.IsDefaultOrEmpty || proof.Receipts.Length > MaximumReceipts ||
            next.Revision != prior.Revision + 1 || next.PreviousRevisionId != prior.RevisionId ||
            next.RevisionId != BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:RiskStateSynchronized:{next.Account.Risk.RevisionId:D}") ||
            next.Event != "RiskStateSynchronized" || next.SavedAt < prior.SavedAt ||
            BrokerAccountImport.Hash(next with { Revision = prior.Revision, RevisionId = prior.RevisionId,
                PreviousRevisionId = prior.PreviousRevisionId, Event = prior.Event, SavedAt = prior.SavedAt,
                Account = prior.Account, RiskSynchronization = prior.RiskSynchronization }) != BrokerAccountImport.Hash(prior))
            throw new InvalidOperationException("Risk synchronization must preserve permission, orders, protection, memory and execution controls.");
        BrokerImplementationReviewWorkflow.RequireSession(prior, calendar, next.SavedAt);
        var risk = prior.Account.Risk;
        foreach (var receipt in proof.Receipts)
        {
            if (receipt.SavedAt > next.SavedAt || receipt.SavedAt < risk.Through ||
                receipt.Request.Kind is not (BrokerRiskContinuationKind.Closing or BrokerRiskContinuationKind.Opening or BrokerRiskContinuationKind.HoldObservation))
                throw new InvalidOperationException("Only existing ordinary risk boundaries and hold observations may be synchronized.");
            var applied = BrokerAccountRiskContinuation.Apply(risk, receipt.Request, calendar, receipt.SavedAt);
            if (BrokerAccountImport.Hash(applied) != BrokerAccountImport.Hash(receipt.State))
                throw new InvalidOperationException("Saved risk receipt does not reproduce its original transition.");
            risk = applied;
        }
        if (BrokerAccountImport.Hash(risk) != BrokerAccountImport.Hash(next.Account.Risk) ||
            BrokerAccountImport.Hash(risk) == BrokerAccountImport.Hash(prior.Account.Risk))
            throw new InvalidOperationException("Current account risk must match the exact continued journal state.");
        // The build review's equality guard remains unchanged. This separate event supplies its verified starting state.
        BrokerImplementationReviewWorkflow.RequireAccount(prior with { Account = prior.Account with { Risk = risk } }, next.Account, next.SavedAt);
    }

    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true)
            throw new InvalidOperationException("Stop the execution host before synchronizing risk for build review.");
    }
}
