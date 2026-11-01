#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

internal sealed class CompactEngineRestart(TradingCurrentStore store, ManualBuyFileJournal journal,
    BrokerExecutionHostGrantFileStore grants, WealthsimpleConnection broker, IBrokerManagedSessionStore sessions,
    ITsxSessionCalendar calendar, IManualBuyProtector protector, string directory, string build, TimeProvider clock)
{
    private bool checkedAccount;

    internal async Task CheckAccountOnceAsync()
    {
        if (checkedAccount) return;
        var grant = grants.Read();
        if (grant is not { Enabled: true, RestoreAfterRestart: true }) return;
        if (!broker.IsConnected) throw new InvalidOperationException("Waiting for Wealthsimple sign-in.");
        var revision = broker.SessionRevision;
        var state = await store.ReadAsync(grant.BindingId) ?? throw new InvalidDataException("Missing compact account.");
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(directory, state.Account.Version), protector);
        var accounts = await broker.ReadTfsaAccountsAsync();
        var account = accounts.Data?.SingleOrDefault(a => a.Id == seal.Assignment.Account);
        if (accounts.Status != WealthsimpleReadStatus.Success || account == null) throw new InvalidOperationException("The approved account is unavailable.");
        var snapshot = await broker.ReadTfsaSnapshotAsync(account);
        var pending = await broker.ReadTfsaPendingOrdersAsync(account);
        if (snapshot.Status != WealthsimpleReadStatus.Success || snapshot.Data?.Account.Id != account.Id ||
            pending.Status != WealthsimpleReadStatus.Success || pending.Data?.Account.Id != account.Id ||
            revision != broker.SessionRevision || !broker.IsConnected || grants.Read() != grant)
            throw new InvalidOperationException("Current account and order checks could not be confirmed.");
        // A closed market can still verify today's settled book. An open order stays with
        // the scoped recovery worker and cannot acquire fresh submission authority here.
        var notices = ImmutableArray.CreateBuilder<RestartNotice>();
        _ = RestartAccountObservation.Prepare(state, grant.BindingId, snapshot.Data, clock.GetUtcNow(), notices);
        if (notices.Count != 0 || !state.Orders.IsEmpty || pending.Data.Orders.Count != 0)
            throw new InvalidOperationException("Original orders or holdings require scoped reconciliation.");
        seal.Assignment.RequireAssignment(state, grant, build, clock.GetUtcNow());
        checkedAccount = true;
    }

    private async Task<(TradingCurrentState State, CompactDelphiCheckpoint Seal)> ReadAsync(Guid binding, Guid revision,
        EngineDatabaseIdentity database, CancellationToken ct)
    {
        var state = await store.ReadAsync(binding, ct) ?? throw new InvalidDataException("The compact account has not been prepared.");
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(directory, state.Account.Version), protector);
        if (seal.ManagedRevision != revision || !seal.Matches(binding, database, state))
            throw new InvalidDataException("The current strategy and compact restart receipt disagree.");
        return (state, seal);
    }

    internal async Task<bool> QualifyAsync(CancellationToken ct)
    {
        var grant = grants.Read() ?? throw new InvalidDataException("The saved assignment is missing.");
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction();
        var db = await EngineContinuityRepository.IdentityAsync(c, t, ct);
        var head = await c.QuerySingleAsync<Guid>(new CommandDefinition(
            "SELECT TOP(1) RevisionId FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@BindingId ORDER BY Revision DESC;",
            new { grant.BindingId }, t, cancellationToken: ct));
        t.Commit();
        var (state, seal) = await ReadAsync(grant.BindingId, head, db, ct);
        // A deliberate stop still allows startup. Financial authority is checked on each action.
        return state.Settings.ImplementationIdentity == build && seal.Assignment.Grant.SessionId == grant.SessionId;
    }

    internal async Task<BrokerManagedSession> CycleAsync(BrokerManagedSession observed, WealthsimpleTfsaAccount account,
        SemaphoreSlim orderGate, CancellationToken ct)
    {
        if (!observed.HasReviewedCompactRestart) throw new InvalidOperationException("Review this build's restart terms first.");
        IBrokerRiskEvidenceSource risk = new BrokerOngoingRiskEvidenceSource(observed.OngoingManagement!, calendar,
            new BrokerOngoingBoundaryRepository().ReadAsync);
        var workflow = BrokerManagedSessionComposition.Create(broker, broker, broker, journal, journal, orderGate, account,
            calendar, risk, clock, true, sessions);
        await using (var lease = await sessions.AcquireAsync(observed.BindingId, ct))
        {
            var saved = lease.Current ?? throw new InvalidDataException("Missing approved account.");
            if (saved.RevisionId != observed.RevisionId) throw new InvalidOperationException("The account changed before recovery.");
            var db = (await new EngineContinuityRepository().ReadAsync(ct)).Database;
            var (state, initial) = await ReadAsync(saved.BindingId, saved.RevisionId, db, ct);
            initial.Assignment.RequireAssignment(state, grants.Read(), build, clock.GetUtcNow(), allowReadRecovery: true);
            var committing = store.WithCheckpointWriter(updated =>
            {
                var prior = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(directory, updated.Account.Version - 1), protector);
                if (prior.Version != updated.Account.Version - 1 || prior.Database != db || prior.Rehearsal != saved.BindingId ||
                    prior.ManagedRevision != lease.Current!.RevisionId)
                    throw new InvalidDataException("The restart writer lost its original strategy boundary.");
                var seal = CompactDelphiCheckpoint.From(saved.BindingId, db, prior.Assignment, updated) with { ManagedRevision = prior.ManagedRevision };
                seal.Write(CompactAccountPreparation.Slot(directory, updated.Account.Version), protector);
            });
            async Task RecordIntent(TradingOrder sale, CancellationToken token)
            {
                var current = lease.Current!;
                if (current.Orders.SingleOrDefault(o => o.RequestId == sale.RequestId) is { } existing)
                {
                    if (existing.DecisionId != sale.DecisionId || existing.PositionId != sale.PositionId || existing.Side != "SELL" ||
                        existing.Quantity != sale.Quantity || existing.SecurityId != sale.SecurityId)
                        throw new InvalidDataException("The sale already has different original terms.");
                    return;
                }
                var protection = current.Protection.Single(p => p.PositionId == sale.PositionId);
                if (sale.Side != "SELL" || protection.FirstExit?.DecisionId != sale.DecisionId ||
                    protection.FirstExit.PermissionId != sale.PermissionId)
                    throw new InvalidDataException("The saved sale does not belong to the original protective decision.");
                var original = new BrokerStrategyOrder(sale.RequestId, sale.DecisionId, sale.PositionId, sale.SecurityId,
                    sale.Symbol, "SELL", sale.Quantity, sale.ReferencePrice, "RiskExit", null, sale.CreatedUtc);
                await lease.AppendAsync(current with { Revision = current.Revision + 1, PreviousRevisionId = current.RevisionId,
                    RevisionId = Guid.NewGuid(), Event = "OrderIntentReserved", SavedAt = clock.GetUtcNow(), Orders = current.Orders.Add(original) }, token);
            }
            // Repair a crash between the compact reservation and the strategy's receipt. Look at
            // original IDs only. An adopted reservation never earns another submission attempt.
            foreach (var sale in state.Orders.Where(o => o.Side == "SELL" && !saved.Orders.Any(s => s.RequestId == o.RequestId)))
                await RecordIntent(sale, ct);
            var sender = new ReservedProtectiveSale(broker, account, broker.FindCanadianSecuritiesAsync, journal, orderGate,
                calendar, clock, new EngineIntegrityRepository().AcquireProtectionLeaseAsync);
            bool Open()
            {
                var now = clock.GetUtcNow();
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
                if (!calendar.IsRegularSession(day)) return false;
                var bounds = calendar.GetSessionBounds(day);
                return now >= bounds.OpenUtc && now < bounds.CloseUtc;
            }
            bool OwnsSale(TradingOrder sale) => lease.Current!.Orders.Any(o => o.RequestId == sale.RequestId && o.Side == "SELL" &&
                o.DecisionId == sale.DecisionId && o.PositionId == sale.PositionId && o.Quantity == sale.Quantity &&
                o.SecurityId == sale.SecurityId && o.Symbol == sale.Symbol);
            Task<AccountRestartResult> Recover() => initial.Assignment.RecoverWithBrokerAsync(committing, journal, grants,
                build, broker, account, sender, RecordIntent, OwnsSale, Open, clock, ct);
            var result = await Recover();
            if (!result.Current.Orders.IsEmpty || !result.Notices.IsEmpty || !result.Current.Restrictions.IsEmpty)
            {
                if (Open())
                {
                    var decisions = lease.Current!.Protection.Select(p => p.FirstExit?.DecisionId).ToHashSet();
                    await workflow.CheckProtectionDuringRestartAsync(lease, ct);
                    if (lease.Current!.Protection.Any(p => p.FirstExit != null && !decisions.Contains(p.FirstExit.DecisionId)))
                        result = await Recover();
                }
                var updated = lease.Current!;
                var reasons = result.Notices.Select(n => $"{n.Action} {n.Symbol} ({n.OriginalId:D}). {n.Reason}")
                    .Concat(result.Current.Restrictions.Select(r => $"{r.ScopeKey} ({r.RestrictionId:D}). {r.Explanation}"));
                return updated with { Blocks = updated.Blocks.Concat(reasons).Distinct().ToImmutableArray() };
            }
            initial.Assignment.RequireAssignment(result.Current, grants.Read(), build, clock.GetUtcNow(), allowReadRecovery: true);
            return await workflow.TickAsync(lease, observed.BindingId, ct);
        }
    }
}
