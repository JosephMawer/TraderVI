#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static async Task ActivateWithCashAsync(Fixture f)
    {
        await f.Workflow().ApproveAsync(f.Approval());
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now));
        f.Cash = 1110;
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RestartRestoresBuyingModeAndSameStrategyOnlyAfterFreshSynchronization(bool stopped)
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        if (stopped) await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var fingerprint = f.Store.State!.Strategy.Fingerprint;
        var opening = f.Store.State.Account.Risk.SessionReference;
        var before = f.Synchronizations;
        f.Store.Restart();
        var restored = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(before + 1, f.Synchronizations);
        Assert.Equal(stopped, restored.NewBuysStopped);
        Assert.Equal(fingerprint, restored.Strategy.Fingerprint);
        Assert.Equal(opening, restored.Account.Risk.SessionReference);
        Assert.Equal(stopped ? 1 : 2, f.Transport.Sent);
        Assert.Equal(stopped ? 0 : 1, restored.Orders.Count(o => o.Side == "BUY"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedFreshCheckAfterRestartPreservesModeAndCannotSubmit(bool stopped)
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        if (stopped) await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var previous = f.Store.State!;
        f.Store.Restart(); f.Pending = true;
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.NeedsAttention, result.Phase);
        Assert.Equal(stopped, result.NewBuysStopped);
        Assert.Equal(previous.Approval.Fingerprint, result.Approval.Fingerprint);
        Assert.Equal(1, f.Transport.Sent);
        f.Pending = false;
        result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(stopped, result.NewBuysStopped);
        Assert.Equal(stopped ? 1 : 2, f.Transport.Sent);
    }

    [Fact]
    public async Task StopDuringTransitionStillReconcilesRequiredExitAndNeverReplacesItWithABuy()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        Assert.Equal("SELL", exit.Side);
        f.Pending = true; await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent);
        f.Pending = false;
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 4, 44, f.Now) with { SubmittedQuantity = 10, Status = "CANCELLED" });
        f.Store.Restart();
        var residual = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Last();
        Assert.Equal("SELL", residual.Side); Assert.Equal(6, residual.Quantity);
        f.Now = f.Now.AddMinutes(1);
        f.Facts.Add(f.Fact(residual.ExternalId, "AAA", "SELL", 6, 66, f.Now));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.NotNull(active.ActivatedAt); Assert.True(active.NewBuysStopped);
        Assert.All(active.Orders, o => { Assert.Equal("SELL", o.Side); Assert.True(o.Reconciled); });
        Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task StoppedBuyingStillSellsAProtectionBreachDiscoveredAfterRestart()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        f.Store.Restart(); f.Now = f.Now.AddMinutes(11);
        var at = active.ActivatedAt!.Value.UtcDateTime;
        f.TransformInput = input => input with { Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
            [new(Guid.NewGuid(), at, at.AddMinutes(5), 5, 10, 10, 8, 9),
             new(Guid.NewGuid(), at.AddMinutes(5), at.AddMinutes(10), 5, 10, 11, 10, 11)]) };
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        var exit = Assert.Single(result.Orders);
        Assert.Equal("SELL", exit.Side); Assert.Equal("AAA", exit.Symbol); Assert.Equal(10, exit.Quantity);
        Assert.True(result.NewBuysStopped); Assert.Equal("HardLoss", result.Memory.Symbols["AAA"].ExitReason);
        Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task ResumeEnablesOnlyBuyingModeAndWaitsForANewCheckedTick()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        f.Store.Restart();
        var before = f.Synchronizations;
        var resumed = await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        Assert.False(resumed.NewBuysStopped); Assert.Equal(1, f.Transport.Sent); Assert.Equal(before, f.Synchronizations);
        f.Pending = true; await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent);
        f.Pending = false; await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task ResumeDoesNotClearLossHoldsOrFullExecutionPause()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        f.Risk = f.Risk with { DailyLossHold = true, CapitalReviewRequired = true };
        await f.Workflow().TickAsync(f.Binding.BindingId);
        var resume = await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        Assert.True(resume.Memory.DailyLossHold); Assert.True(resume.Memory.CapitalReviewRequired);
        await f.Workflow().TickAsync(f.Binding.BindingId); Assert.Equal(1, f.Transport.Sent);
        await f.Workflow().PauseAsync(f.Binding.BindingId);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        resume = await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.Paused, resume.Phase);
        f.Store.Restart(); await f.Workflow().TickAsync(f.Binding.BindingId); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task LostBuyingModeSaveResponseRecoversWithoutAnotherEventOrOrder()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        f.Store.LoseResponseAtEvent = "OperatorStoppedNewBuys";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => f.Workflow().StopNewBuysAsync(f.Binding.BindingId));
        var revision = f.Store.State!.RevisionId;
        f.Store.Restart(); var recovered = await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        Assert.Equal(revision, recovered.RevisionId); Assert.True(recovered.NewBuysStopped);
        await f.Workflow().TickAsync(f.Binding.BindingId); Assert.Equal(1, f.Transport.Sent);
        f.Store.LoseResponseAtEvent = "OperatorResumedBuying";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => f.Workflow().ResumeBuyingAsync(f.Binding.BindingId));
        revision = f.Store.State!.RevisionId;
        f.Store.Restart(); recovered = await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        Assert.Equal(revision, recovered.RevisionId); Assert.False(recovered.NewBuysStopped);
        Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task StoppingAfterUncertainBuyPreservesOriginalOrderAndDoesNotResend()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        f.Transport.ThrowOnSubmit = true;
        var order = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Last();
        Assert.Equal("BUY", order.Side); Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, order.Submission);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        f.Store.Restart();
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.NewBuysStopped); Assert.Equal(order.RequestId, recovered.Orders.Last().RequestId);
        Assert.False(recovered.Orders.Last().Reconciled); Assert.Equal(2, f.Transport.Sent);
        await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        await f.Workflow().TickAsync(f.Binding.BindingId); Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task StopWaitsForInFlightTransmissionAndTheSubmittedFillStillGetsProtection()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        Task<BrokerManagedSession>? stopping = null;
        f.Transport.BeforeSend = () =>
        {
            Assert.NotEmpty(f.Store.State!.Orders);
            stopping = f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
            Assert.False(stopping.IsCompleted);
        };
        var submitted = await f.Workflow().TickAsync(f.Binding.BindingId);
        var order = submitted.Orders.Last();
        Assert.NotNull(stopping); Assert.True((await stopping!).NewBuysStopped);
        f.Store.Restart();
        f.Facts.Add(f.Fact(order.ExternalId, "BBB", "BUY", order.Quantity, order.Quantity * 10, f.Now));
        f.Cash -= order.Quantity * 10; f.OfferBuy = false;
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.NewBuysStopped); Assert.True(recovered.Orders.Last().Reconciled);
        var position = Assert.Single(recovered.Account.Positions);
        Assert.Contains(recovered.Protection, p => p.PositionId == position.PositionId && p.EstablishedAt == position.EntryCompletedAt);
        Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task RestartAndResumeCannotExtendSessionAuthorityOrClearAnInvalidatedReview()
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        f.Store.Restart(); f.Now = f.Now.AddDays(1); f.Risk = f.Risk with { TradingDate = new(2026, 9, 24) };
        await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(BrokerManagedPhase.Paused, result.Phase); Assert.Equal(1, f.Transport.Sent);

        var g = new Fixture(); await ActivateWithCashAsync(g);
        g.Facts.Add(g.Fact("outside", "CCC", "BUY", 1, 10, g.Now));
        await g.Workflow().TickAsync(g.Binding.BindingId);
        await g.Workflow().StopNewBuysAsync(g.Binding.BindingId); g.Store.Restart();
        await g.Workflow().ResumeBuyingAsync(g.Binding.BindingId);
        result = await g.Workflow().TickAsync(g.Binding.BindingId);
        Assert.True(result.ReviewInvalidated); Assert.Equal(1, g.Transport.Sent);
    }

    [Fact]
    public async Task OlderSnapshotsKeepTheirBuyingModeAndFullPause()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        await f.Workflow().PauseAsync(f.Binding.BindingId);
        var oldJson = JsonSerializer.Serialize(f.Store.State);
        Assert.DoesNotContain("NewBuysStopped", oldJson);
        f.Store.Restart(); Assert.False(f.Store.State!.NewBuysStopped);
        Assert.Equal(oldJson, JsonSerializer.Serialize(f.Store.State));
        await f.Workflow().TickAsync(f.Binding.BindingId); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("silent-reset")] [InlineData("buy-while-stopped")] [InlineData("resume-and-buy")]
    public async Task StoreRejectsModeLossOrPurchaseReservationsThatBypassTheStop(string problem)
    {
        var f = new Fixture(); await ActivateWithCashAsync(f);
        var state = await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var next = state with { Revision = state.Revision + 1, RevisionId = Guid.NewGuid(), PreviousRevisionId = state.RevisionId,
            Event = problem == "resume-and-buy" ? "OperatorResumedBuying" : "AccountSynchronized",
            NewBuysStopped = problem == "buy-while-stopped" };
        if (problem != "silent-reset")
            next = next with { Orders = next.Orders.Add(new(Guid.NewGuid(), Guid.NewGuid(), null, "security-BBB", "BBB", "BUY", 1, 10, "Initial", null, f.Now)) };
        Assert.Throws<InvalidOperationException>(() => BrokerManagedSessionRepository.ValidateNext(state, next));
    }
}
