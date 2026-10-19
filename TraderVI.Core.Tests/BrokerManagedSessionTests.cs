#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public void ExactUngatedStrategyKeepsPublishedTopThreeAndAllPortfolioRules()
    {
        var fixture = new Fixture(); var strategy = fixture.Strategy; strategy.Validate();
        Assert.True(strategy.Admits(true, 3, false));
        Assert.False(strategy.Admits(false, 1, false));
        Assert.False(strategy.Admits(true, 4, true));
        Assert.False((strategy with { GateProfile = PaperGateProfile.NormalV1 }).Admits(true, 3, false));
        Assert.Throws<ArgumentException>(() => (strategy with { GateProfile = PaperGateProfile.NormalV1 }).Validate());
        Assert.Equal(SystemShadowPolicyConfig.Version1, strategy.Strategy.ShadowPolicy);
        Assert.NotEqual(strategy.Fingerprint, (strategy with { GateProfile = PaperGateProfile.NormalV1 }).Fingerprint);
        Assert.Throws<ArgumentException>(() => (strategy with { Strategy = strategy.Strategy with { PolicyVersionId = Guid.NewGuid() } }).Validate());
    }

    [Fact]
    public void PartialSalesKeepIdentityAndRealCostWhileCloseReopenStartsNewCycle()
    {
        var f = new Fixture(); var original = f.Project().Positions.Single();
        f.Facts.Add(f.Fact("sell-part", "AAA", "SELL", 4, 44, f.Now.AddMinutes(-30)));
        var partial = f.Project().Positions.Single();
        Assert.Equal(original.PositionId, partial.PositionId); Assert.Equal(6, partial.Quantity); Assert.Equal(60, partial.CostBasis);
        Assert.Equal(original.EntryCompletedAt, partial.EntryCompletedAt);
        f.Facts.Add(f.Fact("sell-rest", "AAA", "SELL", 6, 66, f.Now.AddMinutes(-20)));
        f.Facts.Add(f.Fact("rebuy", "AAA", "BUY", 3, 36, f.Now.AddMinutes(-10)));
        var reopened = f.Project().Positions.Single();
        Assert.NotEqual(original.PositionId, reopened.PositionId); Assert.Equal(36, reopened.CostBasis);
        Assert.Equal(4, f.Project().Fills.Length);
        Assert.Equal(f.Project().Fingerprint, f.Project().Fingerprint);
    }

    [Fact]
    public void CashAvailabilityDoesNotChangeValuationAndUnqualifiedFlowsBlockBuys()
    {
        var f = new Fixture();
        var projected = f.Project();
        Assert.Equal(900, projected.Account.AvailableCash);
        Assert.Equal(1000, projected.Account.ValuationCash);
        Assert.Empty(projected.NewRiskBlocks);
        var missing = BrokerAccountProjector.Project(f.Binding, projected.Import, f.Risk, null, f.Now);
        Assert.Contains(missing.NewRiskBlocks, b => b.Contains("cash-event"));
        Assert.Equal(f.Risk.SessionReference.Value, missing.Account.SessionOpeningValue);
    }

    [Fact]
    public void IdenticalFinancialReadWithNewImportReceiptPreservesReviewIdentity()
    {
        var f = new Fixture(); var p = f.Project();
        var fresh = BrokerAccountProjector.Project(f.Binding, p.Import with { RequestId = Guid.NewGuid() }, f.Risk, f.Coverage(), f.Now);
        Assert.Equal(p.Fingerprint, fresh.Fingerprint);
        f.Approval().Review.Validate(fresh, f.Now);
    }

    [Theory]
    [InlineData("wrong-strategy")]
    [InlineData("wrong-account")]
    [InlineData("future")]
    [InlineData("bad-hash")]
    [InlineData("duplicate")]
    public async Task EvidenceMustHaveExactScopeAndIdentity(string problem)
    {
        var f = new Fixture(); var approval = f.Approval(); var q = approval.Review.Qualifications[0];
        var invalid = problem switch
        {
            "wrong-strategy" => q with { StrategyFingerprint = new string('B', 64) },
            "wrong-account" => q with { BindingId = Guid.NewGuid() },
            "future" => q with { VerifiedAt = f.Now.AddSeconds(1) },
            "bad-hash" => q with { EvidenceHash = "unverified" },
            _ => q
        };
        var qualifications = approval.Review.Qualifications.SetItem(0, invalid);
        if (problem == "duplicate") qualifications = qualifications.Add(q);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(approval with
            { Review = approval.Review with { Qualifications = qualifications } }));
        Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("conflict")]
    [InlineData("wrong-account")]
    [InlineData("fractional")]
    [InlineData("duplicate-order")]
    public void StaleConflictingOrUnownedAccountNeverProjectsAsReady(string problem)
    {
        var f = new Fixture(); var p = f.Project(); var observation = p.Import.Plan.Observation;
        observation = problem switch
        {
            "stale" => observation with { SnapshotAt = f.Now.AddSeconds(-31) },
            "future" => observation with { SnapshotAt = f.Now.AddSeconds(1) },
            "conflict" => observation with { HistoryConflicts = ["changed order"] },
            "wrong-account" => observation with { AccountId = "other" },
            "fractional" => observation with { Orders = [observation.Orders[0] with { FilledQuantity = 0.5m }] },
            _ => observation with { Orders = observation.Orders.Concat(observation.Orders).ToArray() }
        };
        Assert.Throws<InvalidOperationException>(() => BrokerAccountProjector.Project(f.Binding,
            p.Import with { Plan = p.Import.Plan with { Observation = observation } }, f.Risk, f.Coverage(), f.Now));
    }

    [Theory]
    [InlineData("cash-events")]
    [InlineData("opening-values")]
    [InlineData("native-order-lifecycle")]
    [InlineData("strategy-evidence")]
    [InlineData("restart-recovery")]
    [InlineData("installed-storage")]
    public async Task EveryRequiredQualificationBlocksApprovalWhenAbsent(string missing)
    {
        var f = new Fixture(); var approval = f.Approval();
        approval = approval with { Review = approval.Review with { Qualifications = approval.Review.Qualifications.Where(q => q.Check != missing).ToImmutableArray() } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(approval));
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task ApprovalIsDurableIdempotentAndDoesNotSubmit()
    {
        var f = new Fixture(); var approval = f.Approval();
        var saved = await f.Workflow().ApproveAsync(approval);
        Assert.Equal(BrokerManagedPhase.Transition, saved.Phase);
        Assert.Equal(saved, await f.Workflow().ApproveAsync(approval));
        Assert.Equal(0, f.Transport.Sent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(approval with { Reason = "changed" }));
    }

    [Fact]
    public async Task UncertainSubmissionAndRestartNeverTransmitReplacement()
    {
        var f = new Fixture(); f.Transport.ThrowOnSubmit = true;
        await f.Workflow().ApproveAsync(f.Approval());
        var sent = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent); Assert.Single(sent.Orders);
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, sent.Orders[0].Submission);
        f.Store.Restart();
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.NeedsAttention, recovered.Phase);
        Assert.Equal(sent.Orders[0].RequestId, recovered.Orders[0].RequestId);
        Assert.Equal(1, f.Transport.Sent);
        Assert.Null(recovered.ActivatedAt);
    }

    [Fact]
    public async Task LostSaveResponsePreservesIntentAndRecoveryDoesNotSend()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        f.Store.LoseResponseAtEvent = "OrderIntentReserved";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => f.Workflow().TickAsync(f.Binding.BindingId));
        Assert.Single(f.Store.State!.Orders); Assert.Equal(0, f.Transport.Sent);
        f.Store.Restart();
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task LostAcknowledgementSaveResponseRecoversByOriginalOrderId()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        f.Store.LoseResponseAtEvent = "OrderTransmissionObserved";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => f.Workflow().TickAsync(f.Binding.BindingId));
        var order = f.Store.State!.Orders.Single(); Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart();
        f.Facts.Add(f.Fact(order.ExternalId, "AAA", "SELL", 10, 100, f.Now));
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(order.RequestId, recovered.Orders.Single().RequestId);
        Assert.True(recovered.Orders.Single().Reconciled); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task ConflictingTerminalOrderNeverEnablesActivation()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        var order = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(order.ExternalId, "AAA", "SELL", 4, 40, f.Now));
        var stopped = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.NeedsAttention, stopped.Phase);
        Assert.Null(stopped.ActivatedAt); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task SessionAuthorityDoesNotAutomaticallyContinueTomorrow()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddDays(1); f.Risk = f.Risk with { TradingDate = new(2026, 9, 24) };
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.Paused, result.Phase); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task AdoptionBoundaryCannotConsumeAnEarlierConfirmation()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
        var i = await f.ReadStrategyInputAsync(f.Store.State!, default);
        i = i with { CurrentHoldings = [], Account = i.Account! with { Holdings = [], ValuationCash = 1100 },
            CandidateEffectiveUtc = i.Bars["BBB"].Last().StartUtc.AddMinutes(5) };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(i).Recommendations, r => r.Action == "Buy");
    }

    [Fact]
    public async Task RequiredPartialExitMustFinishAndReconcileBeforeActivation()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        var sent = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("SELL", sent.Orders.Single().Side);
        f.Pending = true;
        var partial = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Null(partial.ActivatedAt); Assert.Equal(1, f.Transport.Sent);
        f.Pending = false;
        f.Facts.Add(f.Fact(sent.Orders[0].ExternalId, "AAA", "SELL", 4, 44, f.Now.AddSeconds(-1)) with
            { SubmittedQuantity = 10, Status = "CANCELLED" });
        var residual = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(2, f.Transport.Sent); Assert.Equal(6, residual.Orders.Last().Quantity);
        Assert.Null(residual.ActivatedAt);
        f.Now = f.Now.AddMinutes(1);
        f.Facts.Add(f.Fact(residual.Orders.Last().ExternalId, "AAA", "SELL", 6, 66, f.Now.AddSeconds(-1)));
        var activated = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.NotNull(activated.ActivatedAt); Assert.Empty(activated.Account.Positions);
        Assert.All(activated.Orders, o => Assert.True(o.Reconciled));
        Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task PausedSessionStillRecoversButNeverCreatesOrders()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        await f.Workflow().PauseAsync(f.Binding.BindingId);
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.Paused, result.Phase); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task ReconciledExitFundsFirstBuyAndRestartProtectsItsRealPosition()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now));
        f.Cash = 1110;
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(active.Orders.Single().Reconciled);
        f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
        var buying = await f.Workflow().TickAsync(f.Binding.BindingId);
        var buy = buying.Orders.Last(); Assert.Equal("BUY", buy.Side); Assert.Equal("BBB", buy.Symbol);
        Assert.True(buy.Quantity > 0); Assert.Equal(2, f.Transport.Sent);
        f.Store.Restart();
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(2, f.Transport.Sent);
        f.Facts.Add(f.Fact(buy.ExternalId, "BBB", "BUY", buy.Quantity, buy.Quantity * 10m, f.Now));
        f.Cash -= buy.Quantity * 10m; f.OfferBuy = false;
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.Orders.Last().Reconciled);
        var position = Assert.Single(recovered.Account.Positions);
        var protection = Assert.Single(recovered.Protection.Where(p => p.PositionId == position.PositionId));
        Assert.Equal(position.EntryCompletedAt, protection.EstablishedAt);
        Assert.Equal(10m * buy.Quantity, position.CostBasis);
        Assert.Equal(buy.PositionTarget, recovered.Account.Fills.Last().PositionTarget);
        Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task OutsideAdditionInvalidatesTransitionAndCannotExpandApprovedExit()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        f.Facts.Add(f.Fact("outside-buy", "AAA", "BUY", 1, 10, f.Now));
        var blocked = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(blocked.ReviewInvalidated); Assert.Equal(0, f.Transport.Sent);
        f.Store.Restart();
        Assert.True((await f.Workflow().TickAsync(f.Binding.BindingId)).ReviewInvalidated);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task ConcurrentTicksReserveOnlyOneOriginalOrder()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        await Task.WhenAll(f.Workflow().TickAsync(f.Binding.BindingId), f.Workflow().TickAsync(f.Binding.BindingId));
        Assert.Single(f.Store.State!.Orders); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task ContinuedCashReferenceReplacesCachedHighWithoutReleasingHolds()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Risk = f.Risk with { DrawdownReference = f.Risk.DrawdownReference with { Value = 900 }, CapitalReviewRequired = true };
        var state = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(900, state.Memory.HighestClosingValue); Assert.True(state.Memory.CapitalReviewRequired);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task RecoveryFindsEarlierProtectionBreachEvenIfLatestPriceRecovered()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Store.Restart(); f.Now = f.Now.AddMinutes(11);
        var input = await f.ReadStrategyInputAsync(f.Store.State!, default);
        var at = active.ActivatedAt!.Value.UtcDateTime;
        input = input with { Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
            [new(Guid.NewGuid(), at, at.AddMinutes(5), 5, 10, 10, 8, 9),
             new(Guid.NewGuid(), at.AddMinutes(5), at.AddMinutes(10), 5, 10, 11, 10, 11)]) };
        var replay = BrokerProtectionHandover.Replay(input, active.Protection);
        Assert.Equal("HardLoss", replay.Memory.Symbols["AAA"].ExitReason);
        Assert.Equal(at.AddMinutes(10), replay.Protection.Single().ThroughUtc);
        var evaluated = TradingRecommendationEngine.Evaluate(input with { PreviousMemory = replay.Memory });
        Assert.Equal("Sell", Assert.Single(evaluated.Recommendations).Action);
    }

    [Fact]
    public async Task RetainedProtectionTransfersOnceWithoutCopyingPaperCapitalOrResettingHolds()
    {
        var f = new Fixture(); var account = f.Project(); var position = account.Positions.Single();
        var previous = new BrokerPositionProtection(position.PositionId, "operator", 10.5m, f.Now.AddDays(-1),
            new(position.PositionId, SystemShadowTrailingState.Open(10) with
                { Progressive = ProgressiveProfitProtection.Carry(10m, null, f.Now.AddDays(-1).UtcDateTime, 10.5m) }, null, null, null, null, null));
        var approval = f.Approval(retain: true) with { Review = f.Approval(retain: true).Review with { CurrentProtection = [previous] } };
        await f.Workflow().ApproveAsync(approval);
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.Active, active.Phase);
        Assert.Equal(10.5m, active.Protection.Single().CarriedFloor);
        Assert.Equal(100m, active.Account.Positions.Single().CostBasis);
        Assert.Equal(10m, active.Memory.Symbols["AAA"].ShadowProtection!.HighestCompletedFifteenMinuteClose);
        Assert.Equal(0.005m, active.Memory.Symbols["AAA"].ShadowProtection!.Progressive!.TightestGap);
        Assert.Equal(10.5m, active.Memory.Symbols["AAA"].ShadowProtection!.Progressive!.StopPrice);
        var at = active.ActivatedAt;
        f.Store.Restart(); f.Now = f.Now.AddSeconds(1);
        Assert.Equal(at, (await f.Workflow().TickAsync(f.Binding.BindingId)).ActivatedAt);
        Assert.Equal(0, f.Transport.Sent);
    }

    internal sealed class Fixture : IBrokerManagedAccountSource
    {
        internal DateTimeOffset Now = new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);
        internal readonly List<BrokerOrderFact> Facts = [];
        internal readonly Store Store = new();
        internal readonly Transport Transport = new();
        internal readonly BrokerImportBinding Binding;
        internal BrokerAccountRiskState Risk;
        internal BrokerManagedStrategy Strategy;
        internal bool Pending;
        internal bool OfferBuy;
        internal int Synchronizations;
        internal Func<TradingRecommendationInput, TradingRecommendationInput>? TransformInput;
        internal Func<TradingRecommendationInput, ImmutableArray<TradingQuote>>? QuoteRead;
        internal Func<BrokerAccountProjection, BrokerAccountProjection>? TransformAccount;
        internal string? RunningImplementation;
        public string ImplementationIdentity => RunningImplementation ?? Strategy.Strategy.ImplementationIdentity;
        internal decimal Cash = 1000;
        private readonly Guid candidateId = Guid.NewGuid(), runId = Guid.NewGuid();
        internal Fixture()
        {
            var core = TradingStrategyResolutionTests.Strategy() with { Selection = "FrozenDaily:Breakout" };
            var comparison = new PaperComparisonAccount(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BreakoutTop3",
                PaperGateProfile.PortfolioRulesOnlyV1, Now.AddDays(-1).UtcDateTime, core.Daily.StrategyVersionId, null,
                core.PolicyVersionId, JsonSerializer.Serialize(core.ShadowPolicy));
            Strategy = new(core, comparison.AccountId, comparison.Profile, comparison);
            Binding = new(Guid.NewGuid(), "TFSA", BrokerAccountImport.Hash(new[] { "fixture-identity", "fixture-account" }), Hash, Now.AddDays(-1), "fixture");
            var reference = new BrokerRiskReference(1100, BrokerRiskReferenceSource.SetupObservation, Guid.NewGuid(), Now.AddHours(-1));
            Risk = new(Guid.NewGuid(), Binding.BindingId, "TFSA", null, new(2026, 9, 23), reference, reference, false, false, null, Now.AddHours(-1));
            Facts.Add(Fact("first-buy", "AAA", "BUY", 10, 100, Now.AddDays(-1)));
            Transport.BeforeSend = () => Assert.NotEmpty(Store.State!.Orders);
        }
        internal static string Hash = new('A', 64);
        internal BrokerOrderFact Fact(string id, string symbol, string side, decimal quantity, decimal value, DateTimeOffset at) =>
            new(id, "security-" + symbol, symbol, side, "FILLED", "CAD", "EQUITY", quantity, quantity, value, value, 0, 0, at.AddSeconds(-10), at);
        internal BrokerCashCoverage Coverage() => new(Binding.BindingId, Risk.Through, Now, Now, "Synthetic qualified fixture", Hash);
        internal BrokerAccountProjection Project()
        {
            var holdings = Facts.GroupBy(f => f.SecurityId).Select(g =>
            {
                decimal q = 0, c = 0;
                foreach (var fact in g.OrderBy(o => o.LastFilledAt))
                    if (fact.Side == "BUY") { q += fact.FilledQuantity; c += fact.NetValue!.Value; }
                    else { c *= (q - fact.FilledQuantity) / q; q -= fact.FilledQuantity; }
                return new BrokerReconciledHolding(g.Key, g.First().Symbol, q, q, c, c, g.First().LastFilledAt, 0);
            }).Where(h => h.BrokerQuantity > 0).ToArray();
            var observation = new BrokerAccountReconciliation("fixture-identity", "fixture-account", Now, Now,
                Cash, Cash - 100, 1100, Facts.ToArray(), holdings, [], []) { ActivityFingerprint = Hash };
            var plan = new BrokerImportPlan("TFSA", Binding.BrokerKey, Hash, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), observation, [], []);
            var receipt = new BrokerImportReceipt(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Binding.BindingId, Hash, Now, plan);
            return BrokerAccountProjector.Project(Binding, receipt, Risk, Coverage(), Now);
        }
        internal BrokerExecutionApproval Approval(bool retain = false, bool ongoing = false)
        {
            var account = Project();
            return new(new(Guid.NewGuid(), Binding.BindingId, Strategy, Risk.BaselineId, account.Fingerprint,
                account.Account.TradingDate, Now, 900,
                retain ? [] : account.Positions.Select(p => p.PositionId).ToImmutableArray(),
                retain ? account.Positions.Select(p => p.PositionId).ToImmutableArray() : [], [],
                BrokerActivationReview.RequiredChecks.Select(c => new BrokerQualification(c, Binding.BindingId, Strategy.Fingerprint,
                    Now, Hash, "Synthetic offline fixture; not operational evidence")).ToImmutableArray())
                { ReviewedPositions = account.Positions, Duration = ongoing ? BrokerExecutionDuration.Ongoing : BrokerExecutionDuration.SingleSession }, "fixture", "Offline demonstration");
        }
        internal BrokerManagedSessionWorkflow Workflow() => new(Store, this, Transport, new Clock(this));
        public Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct)
        {
            Synchronizations++;
            if (Pending) throw new InvalidOperationException("Partial fill still pending");
            var account = Project();
            return Task.FromResult(TransformAccount?.Invoke(account) ?? account);
        }
        public Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection account, CancellationToken ct) =>
            Task.FromResult(account.Positions.Select(p => new BrokerProtectionMark(p.PositionId, 11, Now, Hash)).ToImmutableArray());
        public Task PreserveRiskHoldsAsync(BrokerAccountProjection account, TradingRecommendationMemory memory, CancellationToken ct) => Task.CompletedTask;
        public Task<string> ResolveSecurityAsync(string symbol, CancellationToken ct) => Task.FromResult("security-" + symbol);
        public Task<ImmutableArray<TradingQuote>> ReadQuotesAsync(TradingRecommendationInput input, CancellationToken ct) =>
            Task.FromResult(QuoteRead?.Invoke(input) ?? input.Quotes);
        public Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct)
        {
            var assignment = new TradingStrategyAssignment(session.SessionId, "TFSA", Strategy.Strategy, Strategy.PaperAccountId,
                session.SessionId, null, session.ActivatedAt!.Value.UtcDateTime, "fixture", "offline");
            var input = new TradingRecommendationInput(Guid.NewGuid(), assignment, Now.UtcDateTime,
                session.Account.Account.TradingDate, Now.UtcDateTime.Date.AddHours(13.5), Now.UtcDateTime.Date.AddHours(20),
                Strategy.Strategy.Daily.StrategyVersionId, runId, Strategy.Strategy.ImplementationIdentity,
                session.Account.Account, session.Account.Account.LedgerFingerprint, false, session.Account.Account.Holdings,
                session.Account.Fills, OfferBuy ? [new(candidateId, runId, "BBB", 1, 10)] : [],
                OfferBuy ? ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("BBB",
                    [new(Guid.NewGuid(), Now.UtcDateTime.AddMinutes(-11), Now.UtcDateTime.AddMinutes(-6), 5, 10, 10, 10, 10),
                     new(Guid.NewGuid(), Now.UtcDateTime.AddMinutes(-6), Now.UtcDateTime.AddMinutes(-1), 5, 10, 10, 10, 10)])
                    : ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty, [],
                ImmutableDictionary<Guid, int>.Empty, session.Memory, []);
            return Task.FromResult(TransformInput?.Invoke(input) ?? input);
        }
        private sealed class Clock(Fixture f) : TimeProvider { public override DateTimeOffset GetUtcNow() => f.Now; }
    }
    internal sealed class Store : IBrokerManagedSessionStore
    {
        internal BrokerManagedSession? State;
        internal string? LoseResponseAtEvent;
        private readonly SemaphoreSlim gate = new(1);
        public async Task<IBrokerManagedSessionLease> AcquireAsync(Guid id, CancellationToken ct)
        { await gate.WaitAsync(ct); return new Lease(this); }
        internal void Restart() => State = JsonSerializer.Deserialize<BrokerManagedSession>(JsonSerializer.Serialize(State));
        private sealed class Lease(Store store) : IBrokerManagedSessionLease
        {
            public BrokerManagedSession? Current => store.State;
            public Task AppendAsync(BrokerManagedSession next, CancellationToken ct)
            {
                BrokerManagedSessionRepository.ValidateNext(store.State, next); store.State = next;
                if (store.LoseResponseAtEvent == next.Event) { store.LoseResponseAtEvent = null; throw new IOExceptionForTest(); }
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() { store.gate.Release(); return ValueTask.CompletedTask; }
        }
    }
    internal sealed class Transport : IBrokerManagedOrderTransport
    {
        internal int Sent; internal bool ThrowOnSubmit; internal Action? BeforeSend;
        public Task<IBrokerManagedOrderPreflight> PrepareAsync(BrokerStrategyOrder order, BrokerAccountProjection account, CancellationToken ct) =>
            Task.FromResult<IBrokerManagedOrderPreflight>(new Prepared(this));
        private sealed class Prepared(Transport owner) : IBrokerManagedOrderPreflight
        {
            public Task<WealthsimpleSubmissionStatus> SubmitOnceAsync(CancellationToken ct)
            {
                owner.BeforeSend?.Invoke(); owner.Sent++;
                if (owner.ThrowOnSubmit) throw new TimeoutException();
                return Task.FromResult(WealthsimpleSubmissionStatus.Acknowledged);
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class IOExceptionForTest : Exception;
}
