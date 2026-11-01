#nullable enable
using Core.Trader.Storage;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    // Transactional test double, not a SQL performance simulator. Archived rows are never enumerated
    // by ReadCurrent; an independent active index models the intended query boundary.
    private sealed class Database : ITradingStorageDatabase
    {
        internal readonly Dictionary<(Type, Guid, object?), TradingStorageRow> Rows = new();
        private readonly Dictionary<(Type, Guid), Dictionary<object, TradingStorageRow>> active = new();
        private readonly Dictionary<(Guid, Guid), Guid> decisions = new();
        private readonly Dictionary<(Guid, string), Guid> brokerIds = new();
        private readonly SemaphoreSlim gate = new(1, 1);
        internal bool LoseCommitReply;
        internal Action? AfterNextWriteCommit;
        internal int FailAfterWrites;
        internal int CommittedWrites;
        internal int ReadRows;
        internal int ReadCalls;
        internal TradingCurrentStore Store => new(this);
        internal void Seed(TradingStorageRow row)
        {
            var table = TradingTable.For(row.GetType());
            var key = (row.GetType(), row.BindingId, table.Key(row));
            Rows[key] = row;
            if (row is TradingOrder o)
            {
                decisions[(o.BindingId, o.DecisionId)] = o.RequestId;
                if (o.BrokerOrderId is not null) brokerIds[(o.BindingId, o.BrokerOrderId)] = o.RequestId;
            }
            if (table.ActiveColumn is null) return;
            var activeKey = (row.GetType(), row.BindingId);
            if (!active.TryGetValue(activeKey, out var values)) active[activeKey] = values = new();
            if ((bool)row.GetType().GetProperty(table.ActiveColumn)!.GetValue(row)!) values[table.Key(row)!] = row;
            else values.Remove(table.Key(row)!);
        }
        public async Task<ITradingStorageTransaction> BeginAsync(Guid bindingId, bool write, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            return new Transaction(this, bindingId, write);
        }
        private sealed class Transaction(Database db, Guid binding, bool write) : ITradingStorageTransaction
        {
            private readonly Dictionary<(Type, Guid, object?), TradingStorageRow> pending = new();
            private int writes;
            public Task<T?> FindAsync<T>(object? key, CancellationToken ct) where T : TradingStorageRow
            {
                ct.ThrowIfCancellationRequested();
                var slot = (typeof(T), binding, key);
                db.ReadCalls++;
                var row = pending.GetValueOrDefault(slot) ?? db.Rows.GetValueOrDefault(slot);
                if (row is not null) db.ReadRows++;
                return Task.FromResult((T?)row);
            }
            private ImmutableArray<T> Active<T>() where T : TradingStorageRow
            {
                var result = db.active.TryGetValue((typeof(T), binding), out var values)
                    ? values.Values.Cast<T>().ToImmutableArray() : [];
                db.ReadRows += result.Length;
                return result;
            }
            public async Task<TradingCurrentState?> ReadCurrentAsync(CancellationToken ct)
            {
                var account = await FindAsync<TradingAccountCurrent>(null, ct);
                if (account is null) return null;
                var settings = await FindAsync<TradingApprovedSettings>(account.PermissionId, ct);
                var checkpoints = ImmutableArray.CreateBuilder<TradingCheckpoint>();
                foreach (int slot in new[] { 0, 1 })
                    if (await FindAsync<TradingCheckpoint>(slot, ct) is { } c) checkpoints.Add(c);
                return new(account, settings!, Active<TradingPositionCurrent>(), Active<TradingSymbolCurrent>(),
                    Active<TradingOrder>(), Active<TradingRestrictionCurrent>(), checkpoints.ToImmutable());
            }
            public Task PutAsync(TradingStorageRow row, bool insertOnly, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (!write || row.BindingId != binding) throw new InvalidOperationException("Wrong write owner.");
                var key = (row.GetType(), binding, TradingTable.For(row.GetType()).Key(row));
                if (insertOnly == (db.Rows.ContainsKey(key) || pending.ContainsKey(key)))
                    throw new InvalidOperationException("Insert/update shape mismatch.");
                if (row is TradingOrder o)
                {
                    if (db.decisions.TryGetValue((binding, o.DecisionId), out var original) && original != o.RequestId ||
                        o.BrokerOrderId is not null && db.brokerIds.TryGetValue((binding, o.BrokerOrderId), out original) && original != o.RequestId ||
                        pending.Values.OfType<TradingOrder>().Any(p => p.RequestId != o.RequestId &&
                            (p.DecisionId == o.DecisionId || o.BrokerOrderId is not null && p.BrokerOrderId == o.BrokerOrderId)))
                        throw new InvalidOperationException("Duplicate decision/broker order.");
                }
                pending[key] = row;
                if (++writes == db.FailAfterWrites) throw new InvalidOperationException("Simulated interrupted transaction.");
                return Task.CompletedTask;
            }
            public Task CommitAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var row in pending.Values) db.Seed(row);
                db.CommittedWrites += pending.Count;
                if (pending.Count > 0 && db.AfterNextWriteCommit is { } after)
                {
                    db.AfterNextWriteCommit = null;
                    after();
                }
                if (db.LoseCommitReply && pending.Count > 0)
                {
                    db.LoseCommitReply = false;
                    throw new TimeoutException("Commit succeeded but reply was lost.");
                }
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() { db.gate.Release(); return ValueTask.CompletedTask; }
        }
    }
}
