#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
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
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

// Exclusive one-request maintenance. No scheduler, strategy evaluator, enable operation or submission transport.
internal static class SettledSaleMaintenance
{
    private sealed record Target(string InstalledDirectory, Guid RevisionId, Guid RequestId);
    internal static async Task<int> RunAsync(string packet, bool allowReservedFill = false)
    {
        string stage = "Target";
        try
        {
            var target = JsonSerializer.Deserialize<Target>(File.ReadAllText(packet));
            if (target == null || target.RevisionId == Guid.Empty || target.RequestId == Guid.Empty ||
                !Path.IsPathFullyQualified(target.InstalledDirectory) || !File.Exists(Path.Combine(target.InstalledDirectory, "TraderVI.Engine.exe"))) return 64;
            string build = "CoreSha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(target.InstalledDirectory, "Core.dll"))));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var ct = timeout.Token;
            stage = "Ownership";
            using var owner = new EngineOwnershipLease(EnginePaths.OwnershipPath);
            var orders = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple", "ManualOrders");
            var protector = new WindowsManualBuyProtector();
            using var journal = new ManualBuyFileJournal(orders, protector);
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(orders, "execution-host.dat"), protector);
            using var fence = grants.AcquireFence();
            var grant = grants.Read() ?? throw new InvalidDataException();
            var continuity = await new EngineContinuityRepository().ReadAsync(ct);
            var witness = new EngineContinuityFile(Path.Combine(EnginePaths.DirectoryPath, "continuity.dat"), protector).Read();
            if (witness is not { Clean: true } || continuity.Database != witness.Database || continuity.Stamp != witness)
                throw new InvalidDataException();
            var calendar = Core.Trader.DelphiLive.ReviewedTsxSessionCalendar.Load(
                Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable)!);
            await using var lease = await new BrokerManagedSessionRepository(calendar, sharedStorage: true).AcquireAsync(grant.BindingId, ct);
            var saved = lease.Current ?? throw new InvalidDataException();
            var store = new SqlTradingStorageDatabase(EngineIntegrityRepository.OpenAsync).CreateStore();
            var state = await store.ReadAsync(grant.BindingId, ct) ?? throw new InvalidDataException();
            var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(CompactManagedWriter.DirectoryPath, state.Account.Version),
                new CompactReceiptProtector());
            stage = "PriorAssignment";
            if (seal.ManagedRevision != saved.RevisionId || !seal.Matches(grant.BindingId, continuity.Database, state) ||
                !saved.HasReviewedCompactRestart ||
                (!allowReservedFill && (!state.Orders.IsEmpty || !state.Restrictions.IsEmpty)) ||
                state.Orders.Any(o => o.RequestId != target.RequestId || o.Side != "SELL") ||
                saved.ReviewInvalidated || saved.TrialOrderReviewRequired || !grants.Read()!.Equals(grant)) throw new InvalidDataException();
            BrokerExecutionHost.RequireMatchingAssignment(grant, saved, build, DateTimeOffset.UtcNow);
            seal.Assignment.RequireAssignment(state, grant, build, DateTimeOffset.UtcNow, allowReadRecovery: true);
            if (saved.Event == CompactManagedSaleHandoff.Event && saved.PreviousRevisionId == target.RevisionId &&
                saved.Orders.Any(o => o.RequestId == target.RequestId && o.Side == "SELL" && o.Reconciled))
            { Console.WriteLine("OriginalSellAlreadyReconciled"); return 0; }
            if (saved.RevisionId != target.RevisionId || saved.Orders.Where(o => !o.Reconciled).Single() is not { Side: "SELL" } original ||
                original.RequestId != target.RequestId) throw new InvalidDataException();
            await using var integrity = await new EngineIntegrityRepository().AcquireProtectionLeaseAsync(grant.BindingId, ct);
            if (integrity.BlockReason != null) throw new InvalidDataException();
            stage = "BrokerCurrentAccount";
            using var credentials = new WindowsWealthsimpleSessionStore();
            var broker = new WealthsimpleConnection(credentials);
            try
            {
                await broker.StartAsync();
                long login = broker.SessionRevision;
                var catalog = await broker.ReadTfsaAccountsAsync(ct);
                if (catalog.Status != WealthsimpleReadStatus.Success || catalog.Data == null) throw new InvalidDataException();
                var account = BrokerManagedSessionComposition.MatchAccount(saved, catalog.Data);
                var snapshot = await broker.ReadTfsaSnapshotAsync(account, ct);
                var pending = await broker.ReadTfsaPendingOrdersAsync(account, ct);
                if (snapshot.Status != WealthsimpleReadStatus.Success || snapshot.Data?.Account.Id != seal.Assignment.Account ||
                    pending.Status != WealthsimpleReadStatus.Success || pending.Data?.Account.Id != account.Id || pending.Data.Orders.Count != 0)
                    throw new InvalidDataException();
                void Check()
                {
                    if (broker.SessionRevision != login || !broker.IsConnected || grants.Read() != grant) throw new InvalidDataException();
                    seal.Assignment.RequireAssignment(state, grant, build, DateTimeOffset.UtcNow, allowReadRecovery: true);
                }
                var sellChecks = new ManualMarketSellWorkflow(new SellReads(broker), journal);
                if (state.Orders.SingleOrDefault() is { } reserved)
                {
                    stage = "ReservedOriginalFill";
                    Check();
                    var originalJournal = new RestartOrderJournal(journal, grant.BindingId, seal.Assignment.Identity, account.Id);
                    var record = originalJournal.FindOriginalSell(reserved);
                    var position = OfflineAccountRestart.ReservedSalePosition(state, reserved);
                    if (reserved.RequestId != original.RequestId || reserved.DecisionId != original.DecisionId ||
                        reserved.CreatedUtc != original.CreatedAt.UtcDateTime || reserved.PermissionId != state.Settings.PermissionId ||
                        reserved.PositionId != original.PositionId || reserved.Quantity != original.Quantity ||
                        reserved.Symbol != original.Symbol || reserved.SecurityId != original.SecurityId ||
                        position.ExitDecisionId != reserved.DecisionId || !state.Settings.OwnsProtection(position) ||
                        record?.Intent.ProtectiveSale is not { } exit || exit.DecisionId != reserved.DecisionId ||
                        exit.PositionId != position.PositionId || exit.TriggeredUtc != position.ExitTriggeredUtc ||
                        state.Restrictions.Any(r => r.ReasonCode != "BrokerHoldingMismatch" || r.ScopeKey != "Security:" + position.SecurityId))
                        throw new InvalidDataException();
                    var read = await broker.ReadOriginalOrderAsync(account, seal.Assignment.Identity, reserved, ct);
                    if (read.Status != WealthsimpleReadStatus.Success || read.Data is not { LastFilledAt: { } filled } fact)
                        throw new InvalidDataException();
                    Console.WriteLine($"OriginalSaleTimes localReservation={reserved.CreatedUtc:O} localJournal={record.Intent.CreatedAt:O} brokerCreated={fact.CreatedAt:O} brokerFilled={filled:O}");
                    var now = DateTimeOffset.UtcNow;
                    var write = OfflineAccountRestart.PrepareSale(state, position, reserved, fact, now, originalJournal);
                    if (write.Orders.Single() is not { Status: "Filled", NeedsWork: false } ||
                        write.Positions.Single() is not { IsOpen: false, Quantity: 0 }) throw new InvalidDataException();
                    var afterFill = state with { Positions = state.Positions.Remove(position), Orders = [] };
                    var discrepancies = ImmutableArray.CreateBuilder<RestartNotice>();
                    var observed = RestartAccountObservation.Prepare(afterFill, grant.BindingId, snapshot.Data, now, discrepancies);
                    if (discrepancies.Count != 0 || state.Restrictions.Any(r => !observed.Restrictions.Any(
                        update => update.RestrictionId == r.RestrictionId && !update.IsActive))) throw new InvalidDataException();
                    if ((await sellChecks.CheckClosedSaleAsync(record.Intent, fact, ct)).Order is not { IsResolved: true })
                        throw new InvalidDataException();
                    Check();
                    var committing = store.WithCheckpointWriter(updated =>
                    {
                        if (updated.Account.Version != seal.Version + 1) throw new InvalidDataException();
                        (CompactDelphiCheckpoint.From(grant.BindingId, continuity.Database, seal.Assignment, updated)
                            with { ManagedRevision = saved.RevisionId }).Write(
                                CompactAccountPreparation.Slot(CompactManagedWriter.DirectoryPath, updated.Account.Version),
                                new CompactReceiptProtector());
                    });
                    await committing.SaveAsync(write with { Account = observed.Account, Restrictions = observed.Restrictions }, ct);
                    state = await store.ReadAsync(grant.BindingId, ct) ?? throw new InvalidDataException();
                    originalJournal.AlignHoldings(state);
                }
                var notices = ImmutableArray.CreateBuilder<RestartNotice>();
                _ = RestartAccountObservation.Prepare(state, grant.BindingId, snapshot.Data, DateTimeOffset.UtcNow, notices);
                if (notices.Count != 0 || !state.Restrictions.IsEmpty) throw new InvalidDataException();
                stage = "OriginalSellHandoff";
                await CompactManagedSaleHandoff.ReconcileAsync(lease, new(state, []), store, journal.FindSell, async (order, token) =>
                {
                    if (order.RequestId != target.RequestId) throw new InvalidDataException();
                    var read = await broker.ReadOriginalOrderAsync(account, seal.Assignment.Identity, order, token);
                    return read.Status == WealthsimpleReadStatus.Success ? read.Data : null;
                }, sellChecks, seal.Assignment.Identity, account.Id, Check, TimeProvider.System, ct);
                if (lease.Current?.Event != CompactManagedSaleHandoff.Event || lease.Current.PreviousRevisionId != target.RevisionId ||
                    lease.Current.Orders.Any(o => !o.Reconciled)) throw new InvalidDataException();
                Console.WriteLine("OriginalSellReconciled");
                return 0;
            }
            finally { await broker.StopAsync(); }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SettledSaleMaintenance/{stage}/{ex.GetType().Name}");
            return 2;
        }
    }

    private sealed class SellReads(IManualSellBroker source) : IManualSellBroker
    {
        public bool IsConnected => source.IsConnected;
        public long SessionRevision => source.SessionRevision;
        public Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent, CancellationToken ct) =>
            source.ReadManualSellOrderAsync(intent, ct);
        public Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent, CancellationToken ct) =>
            source.ReadManualSellInventoryAsync(intent, ct);
        public Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) =>
            throw new NotSupportedException("Maintenance has no sell preflight.");
        public Task<WealthsimpleMarketOrderSubmission> SubmitManualSellAsync(WealthsimpleMarketSell order, CancellationToken ct) =>
            throw new NotSupportedException("Maintenance cannot submit orders.");
    }
}
