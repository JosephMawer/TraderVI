#nullable enable
using Core.Trader.Promotion;
using Core.Runtime;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    private sealed class SimulatedPowerLoss : Exception;

    private static async Task<BrokerManagedSessionTests.Fixture> CrashAfterProtectiveTrigger()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        BrokerManagedSessionTests.UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6);
        var bar = new TradingPriceBar(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 11, 11, 11, 11);
        f.TransformInput = input => input with { Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA", [bar]) };
        f.QuoteRead = input =>
        {
            f.Now = new DateTimeOffset(input.EvaluatedUtc.AddSeconds(1));
            return [new("AAA", input.EvaluatedUtc, f.Now.UtcDateTime, 11, 10.94m, 11, "TmxQuoteFieldsV1")];
        };
        f.Store.AfterAppend = name => { if (name == "ProtectiveExitTriggered") throw new SimulatedPowerLoss(); };
        await Assert.ThrowsAsync<SimulatedPowerLoss>(() => f.Workflow().TickAsync(f.Binding.BindingId));
        Assert.Empty(f.Store.State!.Orders);
        Assert.Equal(0, f.Transport.Sent);
        var original = f.Store.State.Protection.Single().FirstExit!;
        Assert.NotEqual(Guid.Empty, original.DecisionId);
        Assert.Equal(f.Now.UtcDateTime, original.TriggeredUtc);
        Assert.Equal(f.Store.State.Memory.Symbols["AAA"].ExitReason, original.Reason);
        f.Store.AfterAppend = null;
        // Restore the last committed record through the managed-session shared-storage codec.
        var packed = EngineArchiveCodec.Pack(JsonSerializer.Serialize(f.Store.State));
        f.Store.State = JsonSerializer.Deserialize<BrokerManagedSession>(EngineArchiveCodec.Restore(packed.Manifest, packed.Contents))!;
        Assert.Equal(original, f.Store.State!.Protection.Single().FirstExit);
        return f;
    }

    private sealed class SimulatedManualGate : IDisposable
    {
        internal bool Held;
        internal IDisposable Enter() { Assert.False(Held); Held = true; return this; }
        public void Dispose() { Assert.True(Held); Held = false; }
    }

    [Fact]
    public async Task LegacyLatchedReasonDoesNotAcquireANewDecisionTimeOnRestart()
    {
        var f = await CrashAfterProtectiveTrigger();
        var source = f.Store.State!;
        f.Store.State = source with { Protection = [source.Protection.Single() with { FirstExit = null }] };
        f.Store.Restart();
        f.Now = f.Now.AddMinutes(1);
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Null(f.Store.State!.Protection.Single().FirstExit);
        var position = Position() with { PositionId = source.Account.Positions.Single().PositionId };
        var (_, compact) = await Started();
        var error = Assert.Throws<InvalidOperationException>(() => RestartSellAdmission.CopyTrigger(f.Store.State, compact, position));
        Assert.Contains("decision is missing", error.Message);
    }

    [Fact]
    public async Task ManagedWorkflowAlsoConsumesOriginalProtectiveDecisionAfterARebound()
    {
        var f = await CrashAfterProtectiveTrigger();
        var exit = f.Store.State!.Protection.Single().FirstExit!;
        f.Now = f.Now.AddMinutes(1);
        f.QuoteRead = input => [new("AAA", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 11.20m, 11.19m, 11.21m, "TmxQuoteFieldsV1")];
        var resumed = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(exit, resumed.Protection.Single().FirstExit);
        Assert.Equal(exit.DecisionId, Assert.Single(resumed.Orders).DecisionId);
        Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart();
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent);
    }

    [Theory]
    [InlineData("eligible", 1)]
    [InlineData("revoked before restart", 0)]
    [InlineData("revoked before send", 0)]
    [InlineData("manual sell", 0)]
    [InlineData("manual sell before send", 0)]
    [InlineData("original journal request", 0)]
    [InlineData("filled manual sale", 0)]
    [InlineData("unrelated manual sell", 1)]
    public async Task PowerLossBetweenProtectiveDecisionAndReservation(string scenario, int expectedSends)
    {
        var f = await CrashAfterProtectiveTrigger();
        var source = f.Store.State!;
        var protection = source.Protection.Single();
        var exit = protection.FirstExit!;
        var held = source.Account.Positions.Single();
        var account = Account with { BindingId = source.BindingId, PermissionId = source.EffectiveReview.RequestId,
            StrategyId = source.Strategy.VersionId, AssignmentId = source.SessionId, BaselineId = source.EffectiveReview.BaselineId,
            ExecutionEnabled = true };
        var approved = Settings with { BindingId = account.BindingId, PermissionId = account.PermissionId, StrategyId = account.StrategyId,
            AssignmentId = account.AssignmentId, BaselineId = account.BaselineId, ImplementationIdentity = f.ImplementationIdentity,
            ApprovedUtc = source.Approval.Review.PreparedAt.UtcDateTime };
        var position = Position() with { BindingId = account.BindingId, PositionId = held.PositionId, Symbol = held.Symbol,
            SecurityId = held.SecurityId, Quantity = held.Quantity, CostBasis = held.CostBasis, FirstEntryUtc = held.EntryCompletedAt.UtcDateTime,
            LastEntryUtc = held.EntryCompletedAt.UtcDateTime, ProtectionOwner = protection.Owner,
            ProtectionFloor = source.Memory.Symbols[held.Symbol].LiveProtection!.Progressive!.StopPrice };
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Account = account, Settings = [approved], Positions = [position] });
        await RestartSellAdmission.SaveTriggerAsync(db.Store, source, held.PositionId, RecoveryAt);
        var saved = (await db.Store.ReadAsync(account.BindingId))!;
        Assert.Empty(saved.Orders);
        Assert.Equal(exit.DecisionId, saved.Positions.Single().ExitDecisionId);
        Assert.Equal(exit.TriggeredUtc, saved.Positions.Single().ExitTriggeredUtc);

        var grant = new BrokerExecutionHostGrant(account.BindingId, source.SessionId, source.Strategy.Fingerprint,
            f.ImplementationIdentity, f.Now, "synthetic reviewer", true) { RestoreAfterRestart = true };
        var observation = source.Account.Import.Plan.Observation;
        var quote = new ManualBuyQuote(11, 10.94m, f.Now, "OPEN", f.Now.AddHours(-1), f.Now.AddHours(1), f.Now);
        var journalId = scenario == "original journal request" ? BrokerManagedStrategy.StableId(exit.DecisionId.ToString("D")) : Guid.NewGuid();
        var manual = new ManualSellRecord(new(journalId, observation.IdentityId, observation.AccountId,
            scenario == "unrelated manual sell" ? "other-security" : held.SecurityId, "test account", held.Symbol,
            "XTSE", held.Quantity, f.Now, quote, f.Now), WealthsimpleSubmissionStatus.OutcomeUnknown, []);
        var gate = new SimulatedManualGate();
        var attempts = new List<TradingOrder>();
        using var directory = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(directory.Path, "execution-host.dat"), protector);
        grants.Write(null, grant);
        if (scenario == "revoked before restart") grants.Write(grant, grant with { Enabled = false });
        if (scenario == "filled manual sale")
        {
            var zero = new WealthsimpleMoney(0m, "CAD");
            var value = new WealthsimpleMoney(held.Quantity * 11m, "CAD");
            manual = manual with { Submission = WealthsimpleSubmissionStatus.Acknowledged, InventoryVerifiedAt = f.Now,
                Observations = [new("FILLED", held.Quantity, held.Quantity, new(11, "CAD"), value, value, zero, zero,
                    f.Now, f.Now, null, "REGULAR", f.Now)] };
            Assert.True(manual.IsResolved);
        }
        if (scenario is "manual sell" or "unrelated manual sell" or "original journal request" or "filled manual sale")
        {
            using var manualWriter = new ManualBuyFileJournal(directory.Path, protector);
            manualWriter.SaveSell(manual);
        }
        using var journal = new ManualBuyFileJournal(directory.Path, protector);
        if (scenario == "revoked before send") db.AfterNextWriteCommit = () => grants.Write(grant, grant with { Enabled = false });
        if (scenario == "manual sell before send") db.AfterNextWriteCommit = () => journal.SaveSell(manual);
        Task<IRestartSellLease> Acquire(CancellationToken ct) => Task.FromResult<IRestartSellLease>(
            new RestartSellAdmission(source, grants, journal, f.ImplementationIdentity, gate.Enter()));
        Task<AccountRestartResult> Restart() => new OfflineAccountRestart(db.Store, (_, _, _) => Task.FromResult<BrokerOrderFact?>(null),
            (_, _) => throw new TimeoutException(), (sale, _) =>
            {
                Assert.True(gate.Held);
                Assert.Throws<IOException>(() => grants.Write(grant, grant with { Enabled = false }));
                using var competing = new ManualBuyFileJournal(directory.Path, protector);
                Assert.Throws<IOException>(() => competing.ReadSells());
                attempts.Add(sale);
                return Task.CompletedTask;
            }, Acquire, SavedOpeningTerms, new RecoveryClock()).RunAsync(account.BindingId);
        var first = await Restart();
        await Restart();
        Assert.Equal(expectedSends, attempts.Count);
        if (expectedSends == 1)
        {
            var sale = Assert.Single(attempts);
            Assert.Equal(exit.DecisionId, sale.DecisionId);
            Assert.Equal(BrokerManagedStrategy.StableId(exit.DecisionId.ToString("D")), sale.RequestId);
        }
        else Assert.Contains(first.Notices, n => n.Reason.Contains(scenario.StartsWith("revoked") ? "grant" : "journal"));
        if (scenario is "revoked before restart" or "manual sell") Assert.Empty(first.Current.Orders);
        Assert.False(gate.Held);
        Assert.Equal(exit.TriggeredUtc, first.Current.Positions.Single().ExitTriggeredUtc);
    }
}
