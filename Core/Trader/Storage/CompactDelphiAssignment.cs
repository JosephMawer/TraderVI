#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

// Capture once at the approved writer boundary. Restart consumes only current rows, active order terms and the grant.
internal sealed record CompactDelphiAssignment(BrokerExecutionHostGrant Grant, Guid Permission, Guid Strategy, Guid Source,
    Guid Baseline, string Identity, string Account, ImmutableArray<BuyOpeningTerms> Opening)
{
    internal static ImmutableArray<TradingSymbolCurrent> CurrentSymbols(BrokerManagedSession saved)
    {
        var date = saved.Account.Risk.TradingDate;
        var symbols = saved.Memory.Symbols.Keys.Concat(saved.Account.Positions.Select(p => p.Symbol))
            .Concat(saved.Orders.Where(o => !o.Reconciled).Select(o => o.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase);
        var fills = saved.Account.Fills.ToLookup(f => f.Symbol, StringComparer.OrdinalIgnoreCase);
        var strategy = saved.Strategy.VersionId;
        return symbols.Select(symbol =>
        {
            saved.Memory.Symbols.TryGetValue(symbol, out var memory);
            var minute = memory?.MinuteSignal;
            var last = memory?.LastLiveResult;
            var exit = fills[symbol].Where(f => f.Side == "SELL").MaxBy(f => f.FillUtc);
            return new TradingSymbolCurrent { BindingId = saved.BindingId, StrategyId = strategy, SessionId = saved.SessionId,
                Symbol = symbol.ToUpperInvariant(), IsRelevant = true, TradingDate = date.ToDateTime(TimeOnly.MinValue),
                EntriesToday = fills[symbol].Count(f => f.TradingDate == date && f.Side == "BUY" && f.RecommendationKind != "AddOn"),
                LastExitUtc = exit?.FillUtc, LastExitReason = exit?.ExitReason, LastInputId = last?.CurrentStockObservationId,
                LastProcessedUtc = new[] { memory?.LastLiveBarUtc, minute?.LastEndUtc }.Max(),
                ConfirmationStartedUtc = minute != null ? minute.EpisodeStartUtc : last?.ConfirmationStartedBarEndUtc,
                SignalExpiresUtc = minute?.ExpiresUtc, ProgressJson = JsonSerializer.Serialize(new {
                    LiveSessionId = memory?.LiveSessionId, LiveEpoch = memory?.LiveEpoch, EvaluationId = last?.EvaluationId,
                    PolicyIdentity = minute?.PolicyIdentity, SessionOpenUtc = minute?.SessionOpenUtc, Confirmed = minute?.Confirmed }) };
        }).ToImmutableArray();
    }

    internal static CompactDelphiAssignment Capture(BrokerManagedSession saved, BrokerExecutionHostGrant grant,
        TradingCurrentState state, string build, DateTimeOffset at)
    {
        grant.Validate();
        BrokerExecutionHost.RequireMatchingAssignment(grant, saved, build, at);
        if (!grant.Enabled || !grant.RestoreAfterRestart || grant.ImplementationIdentity != build || saved.TrialOrderReviewRequired ||
            saved.Phase != BrokerManagedPhase.Active || !saved.Blocks.IsEmpty || saved.ReviewInvalidated)
            throw new InvalidDataException("Assignment is not qualified for automatic restart.");
        var review = saved.EffectiveReview;
        if (!CurrentSymbols(saved).OrderBy(s => s.Symbol, StringComparer.Ordinal).SequenceEqual(
            state.Symbols.OrderBy(s => s.Symbol, StringComparer.Ordinal).Select(s => s with { Version = 0 })))
            throw new InvalidDataException("Saved Delphi symbol progress differs from the compact rows.");
        var observation = saved.Account.Import.Plan.Observation;
        var orders = saved.Orders.Where(o => !o.Reconciled).ToDictionary(o => o.RequestId);
        if (!orders.Keys.ToHashSet().SetEquals(state.Orders.Select(o => o.RequestId))) throw new InvalidDataException("Unfinished order set differs.");
        var terms = ImmutableArray.CreateBuilder<BuyOpeningTerms>();
        foreach (var order in state.Orders)
        {
            var o = orders[order.RequestId];
            if (o.DecisionId != order.DecisionId || o.Quantity != order.Quantity || o.Side != order.Side || o.Symbol != order.Symbol ||
                o.SecurityId != order.SecurityId || o.ReferencePrice != order.ReferencePrice || order.PermissionId != review.RequestId)
                throw new InvalidDataException("Original order terms differ.");
            if (o.Side != "BUY") continue;
            if (o.Kind is not ("Initial" or "Reentry") || o.PositionId.HasValue)
                throw new InvalidDataException("An opening buy needs its approved opening terms.");
            // A newly opened Delphi position has no earned trailing floor, high or exit trigger.
            terms.Add(new(saved.BindingId, review.RequestId, o.RequestId, saved.Strategy.VersionId.ToString("D"),
                DelphiLiveProfitProtectionStage.None.ToString(), null, o.PositionTarget)
                { Progressive = saved.Strategy.Strategy.ReadLivePolicy().UseProgressiveProfitProtection });
        }
        foreach (var p in state.Positions.Where(p => p.ExitDecisionId != null))
            if (RestartSellAdmission.CopyTrigger(saved, state, p) != p) throw new InvalidDataException("Original exit differs.");
        var result = new CompactDelphiAssignment(grant, review.RequestId, saved.Strategy.VersionId, saved.Strategy.PaperAccountId,
            review.BaselineId, observation.IdentityId, observation.AccountId, terms.ToImmutable());
        result.RequireAssignment(state, grant, build, at);
        return result;
    }

    internal void RequireAssignment(TradingCurrentState state, BrokerExecutionHostGrant? grant, string build, DateTimeOffset at,
        bool allowReadRecovery = false)
    {
        // Resuming the exact same stopped assignment produces a fresh operator/time receipt.
        // A new binding, continuation, strategy or implementation still requires a new approval.
        if (grant is not { Enabled: true, RestoreAfterRestart: true } || grant.ReviewedAt < Grant.ReviewedAt ||
            !Grant.Enabled && state.Settings.PermissionId == Permission ||
            grant != (Grant with { Enabled = true, ReviewedAt = grant.ReviewedAt, ReviewedBy = grant.ReviewedBy }) || grant.ReviewedAt > at ||
            grant.BindingId != state.Account.BindingId || grant.SessionId != state.Account.AssignmentId ||
            grant.ImplementationIdentity != build || state.Settings.ImplementationIdentity != build ||
            state.Settings.FinancialPermissionId != Permission || state.Settings.SourceId != Source || state.Settings.StrategyId != Strategy ||
            state.Settings.BaselineId != Baseline || !state.Account.ExecutionEnabled ||
            !(state.Account.Phase == "Active" || allowReadRecovery && state.Account.Phase == "NeedsAttention") ||
            state.Settings.ApprovedUtc > at.UtcDateTime || state.Settings.ExpiresUtc <= at.UtcDateTime)
            throw new InvalidOperationException("Restart permission, implementation or assignment changed.");
    }

    internal Task<AccountRestartResult> RecoverAsync(TradingCurrentStore store, ManualBuyFileJournal journal,
        BrokerExecutionHostGrantFileStore grants, string build, TimeProvider time,
        Func<Guid,string,CancellationToken,Task<BrokerOrderFact?>> lookup,
        Func<Guid,CancellationToken,Task<(Guid Binding, WealthsimpleTfsaSnapshot Snapshot)>> snapshot,
        Action<TradingOrder> recordEligible, CancellationToken ct) =>
        new OfflineAccountRestart(store, lookup, snapshot, (sale, _) => { recordEligible(sale); return Task.CompletedTask; },
            _ => Task.FromResult<IRestartSellLease>(new Admission(this, grants, journal, build)),
            (settings, order) => Opening.Single(t => t.RequestId == order.RequestId && t.PermissionId == settings.PermissionId),
            time, new RestartOrderJournal(journal, Grant.BindingId, Identity, Account)).RunAsync(Grant.BindingId, ct);

    internal Task<AccountRestartResult> RecoverWithBrokerAsync(TradingCurrentStore store, ManualBuyFileJournal journal,
        BrokerExecutionHostGrantFileStore grants, string build, WealthsimpleConnection broker, WealthsimpleTfsaAccount account,
        ReservedProtectiveSale sales, Func<TradingOrder, CancellationToken, Task> recordManagedIntent,
        Func<TradingOrder, bool> ownsSale, Func<bool> regularSession, TimeProvider time, CancellationToken ct)
    {
        long revision = broker.SessionRevision;
        void RequireConnection(Guid binding)
        {
            if (binding != Grant.BindingId || account.Id != Account || !broker.IsConnected || revision != broker.SessionRevision)
                throw new InvalidOperationException("The connected account changed. Retry the original request on the approved account.");
        }
        async Task<BrokerOrderFact?> Original(TradingOrder order, CancellationToken token)
        {
            RequireConnection(order.BindingId);
            var result = await broker.ReadOriginalOrderAsync(account, Identity, order, token);
            RequireConnection(order.BindingId);
            return result.Status == WealthsimpleReadStatus.Success ? result.Data : null;
        }
        async Task<(Guid, WealthsimpleTfsaSnapshot)> Snapshot(Guid binding, CancellationToken token)
        {
            RequireConnection(binding);
            var result = await broker.ReadTfsaSnapshotAsync(account, token);
            RequireConnection(binding);
            if (result.Status != WealthsimpleReadStatus.Success || result.Data is not { } snapshot || snapshot.Account.Id != Account)
                throw new InvalidOperationException("Current broker holdings and cash could not be confirmed. Retry the account read.");
            return (binding, snapshot);
        }
        return new OfflineAccountRestart(store, Original, Snapshot, async (order, token) =>
        {
            RequireConnection(order.BindingId);
            var current = await store.ReadAsync(order.BindingId, token) ?? throw new InvalidOperationException("Missing reserved account.");
            RequireAssignment(current, grants.Read(), build, time.GetUtcNow());
            var reserved = current.Orders.Single(o => o.RequestId == order.RequestId);
            var holding = current.Positions.Single(p => p.PositionId == reserved.PositionId);
            await recordManagedIntent(reserved, token);
            await sales.SendAsync(current, holding, reserved, Identity, token);
        }, _ => regularSession() ? Task.FromResult<IRestartSellLease>(new Admission(this, grants, journal, build)) :
            throw new InvalidOperationException("Protective decision saved until the regular trading session opens."),
            (settings, order) => Opening.Single(t => t.RequestId == order.RequestId && t.PermissionId == settings.PermissionId),
            time, new RestartOrderJournal(journal, Grant.BindingId, Identity, Account), ownsSale).RunAsync(Grant.BindingId, ct);
    }

    private sealed class Admission(CompactDelphiAssignment assignment, BrokerExecutionHostGrantFileStore grants,
        ManualBuyFileJournal journal, string build) : IRestartSellLease
    {
        private IDisposable? fence;
        public void Verify(TradingCurrentState state, TradingPositionCurrent p, Guid request, DateTimeOffset now, bool final)
        {
            if (final) fence = grants.AcquireFence();
            assignment.RequireAssignment(state, grants.Read(), build, now);
            if (!state.Settings.SellAllowed || p.ExitDecisionId is not Guid decision ||
                BrokerManagedStrategy.StableId(decision.ToString("D")) != request || !state.Settings.OwnsProtection(p))
                throw new InvalidOperationException("Exit decision does not belong to the approved position.");
            journal.RequireClearForRestartSell(assignment.Identity, assignment.Account, p.SecurityId, p.PositionId, p.FirstEntryUtc, request);
        }
        public void Dispose() => fence?.Dispose();
    }

    internal static string StateDigest(TradingCurrentState state) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        state.Account, state.Settings, Positions = state.Positions.OrderBy(p => p.PositionId),
        Symbols = state.Symbols.OrderBy(s => s.Symbol, StringComparer.Ordinal), Orders = state.Orders.OrderBy(o => o.RequestId),
        Restrictions = state.Restrictions.OrderBy(r => r.RestrictionId), Checkpoints = state.Checkpoints.OrderBy(c => c.Slot)
    })));
}
