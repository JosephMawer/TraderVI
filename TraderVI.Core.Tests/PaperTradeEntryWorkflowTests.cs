using Core.Trader;
using Core.Db;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperTradeEntryWorkflowTests
{
    [Theory]
    [InlineData(TrackedExecutionMode.Ghost)]
    [InlineData(TrackedExecutionMode.Real)]
    public void BuyPick_RetainsDelphiAttribution(TrackedExecutionMode executionMode)
    {
        DailyPickInfo pick = Pick("Buy");

        PaperTradeEntryAttribution attribution =
            PaperTradeEntryWorkflow.ResolveAttribution(
                pick,
                executionMode,
                confirmNonBuyRealOverride: false);

        attribution.OriginalPickId.ShouldBe(pick.PickId);
        attribution.EntryComposite.ShouldBe(pick.CompositeScore);
        attribution.IsDiscretionaryOverride.ShouldBeFalse();
    }

    [Fact]
    public void NonBuyGhostEntry_RemainsRejectedEvenWithOverrideFlag()
    {
        DailyPickInfo pick = Pick("Hold");

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            PaperTradeEntryWorkflow.ResolveAttribution(
                pick,
                TrackedExecutionMode.Ghost,
                confirmNonBuyRealOverride: true));

        error.Message.ShouldContain("Ghost entries require a saved Buy recommendation");
    }

    [Fact]
    public void NonBuyRealEntry_RequiresExplicitOverrideConfirmation()
    {
        DailyPickInfo pick = Pick("Hold");

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            PaperTradeEntryWorkflow.ResolveAttribution(
                pick,
                TrackedExecutionMode.Real,
                confirmNonBuyRealOverride: false));

        error.Message.ShouldContain("requires explicit confirmation");
    }

    [Fact]
    public void ConfirmedNonBuyRealEntry_IsUnlinkedDiscretionaryHolding()
    {
        PaperTradeEntryAttribution attribution =
            PaperTradeEntryWorkflow.ResolveAttribution(
                Pick("Hold"),
                TrackedExecutionMode.Real,
                confirmNonBuyRealOverride: true);

        attribution.OriginalPickId.ShouldBeNull();
        attribution.EntryComposite.ShouldBeNull();
        attribution.IsDiscretionaryOverride.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 15.34)]
    [InlineData(-1, 15.34)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public async Task OpenAsync_RejectsInvalidOperatorFillBeforeDatabaseAccess(
        int shares,
        decimal price)
    {
        var workflow = new PaperTradeEntryWorkflow();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => workflow.OpenAsync(Guid.NewGuid(), shares, price));
    }

    [Fact]
    public async Task OpenAsync_RequiresSavedPickBeforeDatabaseAccess()
    {
        var workflow = new PaperTradeEntryWorkflow();

        await Should.ThrowAsync<ArgumentException>(
            () => workflow.OpenAsync(Guid.Empty, 5, 15.34m));
    }

    private static DailyPickInfo Pick(string direction) => new()
    {
        PickId = Guid.NewGuid(),
        PickDate = new DateTime(2026, 9, 3),
        Symbol = "GGD",
        Lens = "Continuation",
        Rank = 2,
        Direction = direction,
        CompositeScore = 0.505
    };
}
