#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

internal interface ITradingStorageDatabase
{
    Task<ITradingStorageTransaction> BeginAsync(Guid bindingId, bool write, CancellationToken ct);
}
internal interface ITradingStorageTransaction : IAsyncDisposable
{
    Task<T?> FindAsync<T>(object? key, CancellationToken ct) where T : TradingStorageRow;
    Task<TradingCurrentState?> ReadCurrentAsync(CancellationToken ct);
    Task PutAsync(TradingStorageRow row, bool insertOnly, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}

/// <summary>Disconnected compact-state persistence. No broker, engine scheduling or cleanup behavior.</summary>
public sealed class TradingCurrentStore
{
    private readonly ITradingStorageDatabase database;
    private readonly Action<TradingCurrentState>? alignHoldings;
    internal TradingCurrentStore(ITradingStorageDatabase database, Action<TradingCurrentState>? align = null)
    {
        this.database = database;
        alignHoldings = align;
    }
    internal TradingCurrentStore WithHoldingObserver(Action<TradingCurrentState> align) => new(database, align);

    // A latched exit can outlive its active order (for example after rejection).
    internal async Task<TradingOrder?> ReadOrderAsync(Guid bindingId, Guid requestId, CancellationToken ct)
    {
        await using var tx = await database.BeginAsync(bindingId, false, ct);
        var row = await tx.FindAsync<TradingOrder>(requestId, ct);
        if (row is not null)
        {
            TradingStorageValidation.Validate(row);
            if (row.BindingId != bindingId || row.RequestId != requestId)
                throw new InvalidOperationException("Original receipt lookup returned a different identity.");
        }
        await tx.CommitAsync(ct);
        return row;
    }

    // Keep the account read lock until its journal projection is published, so a slower pass
    // cannot replace the newer writer's position set with an older one.
    public async Task<TradingCurrentState?> ReadAsync(Guid bindingId, CancellationToken ct = default)
    {
        if (bindingId == Guid.Empty) throw new ArgumentException("Account identity is required.");
        await using var tx = await database.BeginAsync(bindingId, false, ct);
        var state = await tx.ReadCurrentAsync(ct);
        if (state is not null)
        {
            ValidateCurrent(state, bindingId);
            alignHoldings?.Invoke(state);
        }
        await tx.CommitAsync(ct);
        return state;
    }

    public async Task<TradingSaveResult> SaveAsync(TradingStateWrite write, CancellationToken ct = default)
    {
        var (saved, membershipChanged) = await SaveCoreAsync(write, ct);
        // Publish only committed positions. A lost commit reply or interruption is repaired on the next read.
        if (membershipChanged && alignHoldings != null) await ReadAsync(write.Account.BindingId, ct);
        return saved;
    }

    private async Task<(TradingSaveResult, bool)> SaveCoreAsync(TradingStateWrite write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(write.Kind) || write.Kind.Length > 64 ||
            write.SavedUtc.Kind != DateTimeKind.Utc || write.SavedUtc == default || write.ExpectedVersion < -1)
            throw new ArgumentException("A stable operation identity, UTC boundary and expected version are required.");
        var rows = ChangedRows(write).ToArray();
        var keys = new HashSet<(Type, object?)>();
        foreach (var row in rows)
        {
            TradingStorageValidation.Validate(row);
            if (row.BindingId != write.Account.BindingId || !keys.Add((row.GetType(), TradingTable.For(row.GetType()).Key(row))))
                throw new InvalidOperationException("Mixed accounts or duplicate rows in a state change.");
        }
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(write)));
        await using var tx = await database.BeginAsync(write.Account.BindingId, true, ct);
        var current = await tx.FindAsync<TradingAccountCurrent>(null, ct);
        var markers = new List<TradingCheckpoint>(2);
        foreach (int slot in new[] { 0, 1 })
        {
            var checkpoint = await tx.FindAsync<TradingCheckpoint>(slot, ct);
            if (checkpoint is not null) markers.Add(checkpoint);
        }
        ValidateMarkers(current, markers, write.Account.BindingId);
        foreach (var checkpoint in markers)
        {
            if (checkpoint.OperationId != write.OperationId) continue;
            if (checkpoint.OperationHash != hash) throw new InvalidOperationException("Operation identity was reused for different data.");
            await tx.CommitAsync(ct);
            return (new(checkpoint.Version, true, 0), !write.Positions.IsEmpty);
        }
        if ((current?.Version ?? -1) != write.ExpectedVersion)
            throw new InvalidOperationException("Current state changed. Reload before preparing another change.");
        decimal committed = current?.CommittedPurchaseCost ?? 0m;
        decimal reserved = current?.ReservedPurchaseCost ?? 0m;
        if (write.Account.CommittedPurchaseCost != committed || write.Account.ReservedPurchaseCost != reserved)
            throw new InvalidOperationException("Purchase allowance totals are maintained from original order receipts.");
        var permission = write.Settings.SingleOrDefault(s => s.PermissionId == write.Account.PermissionId)
            ?? await tx.FindAsync<TradingApprovedSettings>(write.Account.PermissionId, ct);
        if (permission is null || permission.BindingId != write.Account.BindingId || permission.StrategyId != write.Account.StrategyId ||
            permission.AssignmentId != write.Account.AssignmentId || permission.BaselineId != write.Account.BaselineId)
            throw new InvalidOperationException("Current account must reference its exact approved settings.");

        long nextVersion = checked(write.ExpectedVersion + 1);
        var pending = new List<(TradingStorageRow Row, bool Insert)>();
        bool membershipChanged = false;
        foreach (var requested in rows)
        {
            if (requested is TradingAccountCurrent) continue;
            var row = requested;
            var prior = await Find(tx, row, ct);
            // Reading an already completed receipt again need not change its last check time.
            if (prior is TradingOrder { NeedsWork: false } completed && row is TradingOrder observed)
                row = observed with { LastCheckedUtc = completed.LastCheckedUtc };
            if (prior is not null)
            {
                TradingStorageValidation.ValidateTransition(prior, row);
                if (TradingStorageValidation.Same(prior, row)) continue;
            }
            if (row is TradingOrder order)
            {
                var before = PurchaseAllowance.For((TradingOrder?)prior);
                var after = PurchaseAllowance.For(order);
                committed += after.Committed - before.Committed;
                reserved += after.Reserved - before.Reserved;
            }
            if (row is TradingPositionCurrent position && (prior is TradingPositionCurrent { IsOpen: true }) != position.IsOpen)
                membershipChanged = true;
            pending.Add((row with { Version = nextVersion }, prior is null));
        }
        var account = write.Account with { Version = nextVersion, CommittedPurchaseCost = committed, ReservedPurchaseCost = reserved };
        TradingStorageValidation.Validate(account);
        if (pending.Count == 0 && current is not null && TradingStorageValidation.Same(current, account))
        {
            await tx.CommitAsync(ct);
            return (new(current.Version, false, 0), false);
        }
        // Even when only one position changes, the account identifies the coherent new state version.
        pending.Add((account, current is null));
        // Settings precede the account FK; the account precedes its child rows.
        foreach (var item in pending.OrderBy(p => p.Row is TradingApprovedSettings ? 0 : p.Row is TradingAccountCurrent ? 1 : 2))
            await tx.PutAsync(item.Row, item.Insert, ct);
        var marker = new TradingCheckpoint { BindingId = write.Account.BindingId, Version = nextVersion,
            Slot = (int)(nextVersion % 2), OperationId = write.OperationId, OperationHash = hash,
            OperationKind = write.Kind, SavedUtc = write.SavedUtc };
        TradingStorageValidation.Validate(marker);
        await tx.PutAsync(marker, nextVersion < 2, ct);
        await tx.CommitAsync(ct);
        return (new(nextVersion, false, pending.Count + 1), membershipChanged);
    }

    internal static IEnumerable<TradingStorageRow> ChangedRows(TradingStateWrite write)
    {
        yield return write.Account;
        foreach (var r in write.Settings) yield return r;
        foreach (var r in write.Positions) yield return r;
        foreach (var r in write.Symbols) yield return r;
        foreach (var order in write.Orders)
            yield return order.Side == "BUY" ? order with { ReservedCash = PurchaseAllowance.For(order).Reserved } : order;
        foreach (var r in write.Restrictions) yield return r;
    }
    private static Task<TradingStorageRow?> Find(ITradingStorageTransaction tx, TradingStorageRow row, CancellationToken ct) => row switch
    {
        TradingApprovedSettings r => Box(tx.FindAsync<TradingApprovedSettings>(r.PermissionId, ct)),
        TradingPositionCurrent r => Box(tx.FindAsync<TradingPositionCurrent>(r.PositionId, ct)),
        TradingSymbolCurrent r => Box(tx.FindAsync<TradingSymbolCurrent>(r.Symbol, ct)),
        TradingOrder r => Box(tx.FindAsync<TradingOrder>(r.RequestId, ct)),
        TradingRestrictionCurrent r => Box(tx.FindAsync<TradingRestrictionCurrent>(r.RestrictionId, ct)),
        _ => throw new InvalidOperationException("Unsupported current row.")
    };
    private static async Task<TradingStorageRow?> Box<T>(Task<T?> task) where T : TradingStorageRow => await task;

    private readonly record struct PurchaseAllowance(decimal Committed, decimal Reserved)
    {
        internal static PurchaseAllowance For(TradingOrder? order)
        {
            if (order is null || order.Side != "BUY") return default;
            // The existing allowance uses the original estimate until reconciliation. Actual
            // fill costs (including costs above that estimate) remain intact on the receipt.
            decimal charge = TradingStorageValidation.IsTerminal(order.Status)
                ? order.LatestNetValue : order.Quantity * order.ReferencePrice;
            decimal booked = decimal.Min(charge, order.AppliedNetValue);
            return new(booked, charge - booked);
        }
    }

    internal static void ValidateCurrent(TradingCurrentState state, Guid bindingId)
    {
        ValidateMarkers(state.Account, state.Checkpoints, bindingId);
        var rows = new TradingStorageRow[] { state.Account, state.Settings }.Concat(state.Positions).Concat(state.Symbols)
            .Concat(state.Orders).Concat(state.Restrictions).Concat(state.Checkpoints).ToArray();
        foreach (var row in rows)
        {
            TradingStorageValidation.Validate(row);
            if (row.BindingId != bindingId || row.Version > state.Account.Version)
                throw new InvalidOperationException("Mixed account or future row in current state.");
        }
        if (state.Settings.PermissionId != state.Account.PermissionId || state.Settings.StrategyId != state.Account.StrategyId ||
            state.Settings.AssignmentId != state.Account.AssignmentId || state.Settings.BaselineId != state.Account.BaselineId ||
            state.Positions.Any(p => !p.IsOpen) || state.Symbols.Any(s => !s.IsRelevant) ||
            state.Orders.Any(o => !o.NeedsWork) || state.Restrictions.Any(r => !r.IsActive))
            throw new InvalidOperationException("Current references, active sets or checkpoint markers do not agree.");
    }

    private static void ValidateMarkers(TradingAccountCurrent? account, IEnumerable<TradingCheckpoint> checkpoints, Guid bindingId)
    {
        var markers = checkpoints.ToArray();
        if (account is not null) TradingStorageValidation.Validate(account);
        foreach (var marker in markers) TradingStorageValidation.Validate(marker);
        if (account is null ? markers.Length != 0 :
            account.BindingId != bindingId || markers.Length != (account.Version == 0 ? 1 : 2) ||
            markers.Any(c => c.BindingId != bindingId) || markers.Select(c => c.Slot).Distinct().Count() != markers.Length ||
            !markers.Any(c => c.Version == account.Version) ||
            (account.Version > 0 && !markers.Any(c => c.Version == account.Version - 1)))
            throw new InvalidOperationException("Account and committed markers do not agree; do not overwrite or retry blindly.");
    }
}
