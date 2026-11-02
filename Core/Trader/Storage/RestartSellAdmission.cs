#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

// Offline composition supplies a lease shared with its manual-order and grant writers.
internal interface IRestartSellLease : IDisposable
{
    void Verify(TradingCurrentState state, TradingPositionCurrent position, Guid requestId, DateTimeOffset now, bool final);
}

internal sealed class RestartSellAdmission(BrokerManagedSession session, BrokerExecutionHostGrantFileStore grants,
    ManualBuyFileJournal journal, string build, IDisposable lease) : IRestartSellLease
{
    private IDisposable? grantFence;
    internal static async Task SaveTriggerAsync(TradingCurrentStore store, BrokerManagedSession source, Guid positionId,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(source.BindingId, ct) ?? throw new InvalidOperationException("Missing compact account.");
        var position = state.Positions.Single(p => p.PositionId == positionId);
        var updated = CopyTrigger(source, state, position);
        await store.SaveAsync(new(Guid.NewGuid(), "RecordOriginalProtectiveExit", now.UtcDateTime,
            state.Account.Version, state.Account) { Positions = [updated] }, ct);
    }

    internal static TradingPositionCurrent CopyTrigger(BrokerManagedSession session, TradingCurrentState state,
        TradingPositionCurrent position)
    {
        var saved = session.Protection.Single(p => p.PositionId == position.PositionId);
        var held = session.Account.Positions.Single(p => p.PositionId == position.PositionId);
        var trigger = saved.FirstExit ?? throw new InvalidOperationException("Original protective decision is missing.");
        if (state.Account.BindingId != session.BindingId || position.BindingId != session.BindingId ||
            state.Account.AssignmentId != session.SessionId || state.Settings.FinancialPermissionId != session.EffectiveReview.RequestId ||
            trigger.PermissionId != session.EffectiveReview.RequestId || state.Account.StrategyId != session.Strategy.VersionId ||
            state.Account.BaselineId != session.EffectiveReview.BaselineId || position.ProtectionOwner != saved.Owner ||
            position.SecurityId != held.SecurityId || position.Symbol != held.Symbol || position.Quantity != held.Quantity ||
            position.CostBasis != held.CostBasis ||
            trigger.DecisionId == Guid.Empty || trigger.TriggeredUtc.Kind != DateTimeKind.Utc ||
            trigger.TriggeredUtc < position.FirstEntryUtc || trigger.TriggeredUtc > session.SavedAt.UtcDateTime)
            throw new InvalidOperationException("Original protective decision does not match this account, position and permission.");
        return position with { ExitDecisionId = trigger.DecisionId, ExitReason = trigger.Reason, ExitTriggeredUtc = trigger.TriggeredUtc };
    }

    public void Verify(TradingCurrentState state, TradingPositionCurrent position, Guid requestId, DateTimeOffset now, bool final)
    {
        try
        {
            if (final) grantFence = grants.AcquireFence();
            VerifyCurrent(state, position, requestId, now);
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or ArgumentException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("The protected grant or order journal is busy or unreadable. Recheck before selling.", error);
        }
    }

    private void VerifyCurrent(TradingCurrentState state, TradingPositionCurrent position, Guid requestId, DateTimeOffset now)
    {
        var grant = grants.Read();
        if (grant is not { Enabled: true, RestoreAfterRestart: true } || session.Phase != BrokerManagedPhase.Active ||
            session.TrialOrderReviewRequired || state.Settings.ImplementationIdentity != build)
            throw new InvalidOperationException("Current host grant does not authorize a protective sell on restart.");
        grant.Validate();
        BrokerExecutionHost.RequireMatchingAssignment(grant, session, build, now);
        if (CopyTrigger(session, state, position) != position)
            throw new InvalidOperationException("Saved protective decision differs from the original trigger.");
        var account = session.Account.Import.Plan.Observation;
        journal.RequireClearForRestartSell(account.IdentityId, account.AccountId, position.SecurityId,
            position.PositionId, position.FirstEntryUtc, requestId);
    }
    public void Dispose()
    {
        try { grantFence?.Dispose(); }
        finally { lease.Dispose(); }
    }
}
