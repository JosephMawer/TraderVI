#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Operator permission to run one already-approved assignment in one exact implementation.</summary>
public sealed record BrokerExecutionHostGrant(Guid BindingId, Guid SessionId, string StrategyFingerprint,
    string ImplementationIdentity, DateTimeOffset ReviewedAt, string ReviewedBy, bool Enabled)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ContinuationId { get; init; }
    // Explicit host scope, absent from legacy grants. A dated trial cannot gain restart authority.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool RestoreAfterRestart { get; init; }
    public void Validate()
    {
        if (BindingId == Guid.Empty || SessionId == Guid.Empty || !CompleteTradingStrategy.IsHash(StrategyFingerprint) ||
            string.IsNullOrWhiteSpace(ImplementationIdentity) || ReviewedAt == default || string.IsNullOrWhiteSpace(ReviewedBy))
            throw new InvalidOperationException("Execution-host permission must identify the reviewed assignment, build and operator.");
    }
    public override string ToString() => "Reviewed execution-host permission (account identity omitted)";
}

public interface IBrokerExecutionHostGrantStore
{
    BrokerExecutionHostGrant? Read();
    void Write(BrokerExecutionHostGrant? expected, BrokerExecutionHostGrant next);
}

public sealed record BrokerExecutionHostStatus(string State, string Message, DateTimeOffset CheckedAt,
    DateTimeOffset? NextCheckAt = null);

/// <summary>
/// One non-overlapping desktop polling owner. It cannot create approvals, opening orders or risk references.
/// A running grant resumes only its exact immutable approved session; all execution stays in the existing workflow.
/// </summary>
public sealed class BrokerExecutionHost(IBrokerExecutionHostGrantStore grants, IBrokerManagedSessionControls sessions,
    Func<BrokerManagedSession, CancellationToken, Task<BrokerManagedSession>> tick, ITsxSessionCalendar calendar,
    string implementationIdentity, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextCheck;
    private int failures;
    public bool IsBusy { get; private set; }
    public BrokerExecutionHostStatus Status { get; private set; } = new("Off", "Automatic trading off. Prepare and review an assignment before activation.", default);
    public event EventHandler? StatusChanged;

    public async Task EnableAsync(Guid bindingId, Guid sessionId, string reviewedStrategyFingerprint,
        string reviewer, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            IsBusy = true;
            var saved = await sessions.ReadAsync(bindingId, ct)
                ?? throw new InvalidOperationException("No approved managed assignment exists. Complete its execution review first.");
            if (saved.SessionId != sessionId || saved.Strategy.Fingerprint != reviewedStrategyFingerprint)
                throw new InvalidOperationException("The displayed assignment changed. Review its current identity before activation.");
            if (saved.Continuation is not null && saved.OngoingManagement is null)
            {
                var scope = saved.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the saved continuation scope.");
                scope.Validate(calendar);
                var now = clock.GetUtcNow();
                if (scope.Session != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) ||
                    now < calendar.GetSessionBounds(scope.Session).OpenUtc && !scope.PreOpenApproved || scope.PurchaseExpiresAt <= now ||
                    scope.Continuation?.Fingerprint != saved.Account.Risk.Continuation?.Fingerprint ||
                    scope.October22?.Fingerprint != saved.Account.Risk.October22?.Fingerprint ||
                    scope.SessionContinuation?.Fingerprint != saved.Account.Risk.SessionContinuation?.Fingerprint)
                    throw new InvalidOperationException("Enable only the current dated permission after its risk/approval records agree and before its reviewed close.");
            }
            var grant = new BrokerExecutionHostGrant(bindingId, sessionId, reviewedStrategyFingerprint,
                implementationIdentity, clock.GetUtcNow(), reviewer, true)
                { ContinuationId = saved.OngoingManagement?.RequestId ?? saved.SessionContinuation?.Approval.Review.RequestId ?? saved.October22?.Approval.Review.RequestId ?? saved.Continuation?.Approval.Review.RequestId,
                    RestoreAfterRestart = saved.IsOngoing && saved.EffectiveReview.NoTransferScope is null };
            grant.Validate(); RequireMatchingAssignment(grant, saved);
            ct.ThrowIfCancellationRequested();
            grants.Write(grants.Read(), grant);
            if (grants.Read() != grant) throw new InvalidOperationException("Host activation was not confirmed. Read back its saved permission before retrying.");
            failures = 0; nextCheck = default;
            var open = calendar.GetSessionBounds(saved.EffectiveReview.Session).OpenUtc;
            Present("Waiting", clock.GetUtcNow() < open
                ? "Armed for 9:30 AM Toronto. No pre-open execution; fresh account, source and risk checks are required at the open. Purchases end at today's reviewed close."
                : "Execution host enabled for the reviewed assignment. Fresh session/account/order checks precede every action.",
                clock.GetUtcNow() < open ? new DateTimeOffset(open) : null);
        }
        finally { IsBusy = false; gate.Release(); }
    }

    public async Task DisableAsync(CancellationToken ct = default)
    {
        // Finish an already-running cycle before reporting stopped. Never abandon an uncertain order.
        await gate.WaitAsync(ct);
        try
        {
            var saved = grants.Read();
            if (saved?.Enabled == true) grants.Write(saved, saved with { Enabled = false });
            if (grants.Read()?.Enabled == true) throw new InvalidOperationException("Host stop was not confirmed. Read back its saved permission.");
            Present("Off", "Execution host stopped. Saved assignment, buying mode, risk history and original orders are preserved.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Drain a process shutdown without turning an approved ongoing mode into an operator stop.</summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var grant = grants.Read();
            if (grant?.Enabled == true && !grant.RestoreAfterRestart)
            {
                grants.Write(grant, grant with { Enabled = false });
                if (grants.Read()?.Enabled == true)
                    throw new InvalidOperationException("Legacy host shutdown was not confirmed. Recover its saved permission.");
            }
            Present("Off", grant is { Enabled: true, RestoreAfterRestart: true }
                ? "Engine stopped. Ongoing permission is preserved; restart requires the same build, account, order and strategy checks."
                : "Engine stopped. Saved operator mode and original orders are preserved.");
        }
        finally { gate.Release(); }
    }

    public async Task PollAsync(bool accountAvailable, CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            IsBusy = true;
            var grant = grants.Read();
            if (grant?.Enabled != true) { Present("Off", "Automatic trading off. No execution-host permission is enabled."); return; }
            grant.Validate();
            var now = clock.GetUtcNow();
            if (grant.ImplementationIdentity != implementationIdentity)
            { Present("Blocked", "The installed build changed. Review the saved strategy and build before enabling execution again."); return; }
            if (!accountAvailable) { Present("Waiting", "Execution is waiting for the connected account and the shared manual-order controls."); return; }
            var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
            if (!calendar.IsRegularSession(date)) { Present("Waiting", "Execution is waiting for a reviewed regular TSX session."); return; }
            var bounds = calendar.GetSessionBounds(date);
            if (now.UtcDateTime < bounds.OpenUtc || now.UtcDateTime >= bounds.CloseUtc)
            { Present("Waiting", "Execution is waiting for regular trading hours. No opening order is submitted outside the session.",
                now.UtcDateTime < bounds.OpenUtc ? new DateTimeOffset(bounds.OpenUtc) : null); return; }
            if (now < nextCheck) return;
            var saved = await sessions.ReadAsync(grant.BindingId, ct)
                ?? throw new InvalidOperationException("The approved assignment could not be recovered.");
            RequireMatchingAssignment(grant, saved);
            if (saved.Phase == BrokerManagedPhase.Paused)
            { Present("Paused", "The saved execution assignment is paused. Host startup does not resume it."); return; }
            if (!saved.IsOngoing && saved.Approval.Review.Session != date)
            { Present("Blocked", "The reviewed execution session expired. Review the next session explicitly."); return; }
            // Recheck the durable permission after reads, before delegating any cycle.
            if (grants.Read() != grant) { Present("Waiting", "Host permission changed. Recover its current mode before continuing."); return; }
            ct.ThrowIfCancellationRequested();
            var result = await tick(saved, ct);
            failures = 0; nextCheck = clock.GetUtcNow().AddSeconds(30);
            Present(!result.Blocks.IsEmpty ? "Blocked" : result.Phase == BrokerManagedPhase.Active ? "Running" : "Waiting",
                result.Blocks.IsEmpty ? result.NewBuysStopped
                    ? "New automatic buys are stopped; ordinary exit rules remain enabled."
                    : result.LatestDecision?.Summary ?? "Reviewed strategy cycle complete. Existing account, order and protection controls remain authoritative."
                    : string.Join(" ", result.Blocks), nextCheck);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            failures = System.Math.Min(failures + 1, 5);
            nextCheck = clock.GetUtcNow().AddSeconds(System.Math.Min(300, 30 * System.Math.Pow(2, failures - 1)));
            Present("Blocked", "Execution checks could not complete. Original orders and saved permission are preserved; retry reads after backoff.", nextCheck);
        }
        finally { IsBusy = false; gate.Release(); }
    }

    private void RequireMatchingAssignment(BrokerExecutionHostGrant grant, BrokerManagedSession saved)
    {
        saved.Strategy.Validate();
        if (saved.BindingId != grant.BindingId || saved.SessionId != grant.SessionId || saved.Strategy.Fingerprint != grant.StrategyFingerprint ||
            grant.RestoreAfterRestart && (!saved.IsOngoing || saved.EffectiveReview.NoTransferScope is not null) ||
            grant.ContinuationId != (saved.OngoingManagement?.RequestId ?? saved.SessionContinuation?.Approval.Review.RequestId ?? saved.October22?.Approval.Review.RequestId ?? saved.Continuation?.Approval.Review.RequestId) ||
            saved.OngoingManagement is { } ongoing && (ongoing.ApprovedAt is null || ongoing.ApprovedAt > grant.ReviewedAt ||
                saved.Account.Risk.OngoingAuthorization != ongoing.RiskAuthorization) ||
            saved.Strategy.Strategy.ImplementationIdentity != implementationIdentity || saved.ReviewInvalidated ||
            saved.ImplementationReview is not null && grant.ReviewedAt < saved.ImplementationReview.ApprovedAt ||
            saved.Strategy.Strategy.Family != TradingStrategyFamily.DelphiLive ||
            saved.Strategy.GateProfile != PaperGateProfile.NormalV1 &&
                (saved.SourceSwitch is null || saved.Strategy.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 || grant.ReviewedAt < saved.SourceSwitch.ApprovedAt) ||
            saved.Strategy.Comparison is null || grant.ReviewedAt > clock.GetUtcNow())
            throw new InvalidOperationException("Execution permission must match the exact approved Live source, account and running build.");
    }
    private void Present(string state, string message, DateTimeOffset? next = null)
    {
        Status = new(state, message, clock.GetUtcNow(), next);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
