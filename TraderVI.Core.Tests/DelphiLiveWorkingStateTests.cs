#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveWorkingStateTests
{
    [Theory]
    [InlineData(2)] [InlineData(10_000)]
    public void CompletedHistoryRollsIntoTotalsWithoutChangingCapitalCashValuationOrLastExit(int count)
    {
        var full = History(count);
        var selected = DelphiLiveWorkingState.Select(full);
        var state = selected.Snapshot;
        state.Positions.Length.ShouldBe(1);
        state.Fills.Length.ShouldBe(1);
        state.StartingCapital.ShouldBe(full.StartingCapital);
        state.Cash.ShouldBe(full.Cash);
        state.Positions.Max(p => p.ClosedUtc).ShouldBe(full.Positions.Max(p => p.ClosedUtc));
        state.WorkingTotals!.ArchivedCashNet.ShouldBe(count - 11m);
        state.WorkingTotals.ArchivedRealizedProfitLoss.ShouldBe(count - 1m);
        var smallValue = DelphiLiveAccountValuation.From(state, state.CurrentSession!.Value);
        var fullValue = DelphiLiveAccountValuation.From(full, full.CurrentSession!.Value);
        smallValue.RealizedProfitLoss.ShouldBe(fullValue.RealizedProfitLoss);
        smallValue.NetAssetValue.ShouldBe(fullValue.NetAssetValue);
        smallValue.TotalReturn.ShouldBe(fullValue.TotalReturn);
        var again = DelphiLiveWorkingState.Select(state).Snapshot;
        again.WorkingTotals.ShouldBe(state.WorkingTotals);
        DelphiLiveLedgerIntegrity.ValidateTransition(state, state with { Revision = state.Revision + 1 });
        Should.Throw<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(state,
            state with { Revision = state.Revision + 1, Cash = state.Cash + 1 }));
        Should.Throw<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(state,
            state with { Revision = state.Revision + 1, WorkingTotals = state.WorkingTotals with { ArchivedCashNet = 0m } }));
    }

    [Fact]
    public void SameSessionEntriesAndPendingQuoteCausalitySurvivePruning()
    {
        var full = History(4);
        var today = full.CurrentSession!.Value;
        var last = full.Positions[^1];
        var buy = full.Fills[^2] with { TradingDate = today };
        var intent = new DelphiLiveActionIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "AAA", DelphiLiveActionSide.Buy,
            full.UpdatedUtc, full.UpdatedUtc, null, null, 100m);
        var action = new DelphiLiveLedgerAction(intent, null, Guid.NewGuid(), today.AddDays(-1), Guid.NewGuid(), "Test", "{}",
            "Pending", 2, Guid.NewGuid(), null, null, []);
        DelphiLiveLedgerQuote Quote(int attempt) => new(Guid.NewGuid(), intent.ActionId, null, "FillObservation", Guid.NewGuid(),
            new(Guid.NewGuid(), intent.DecisionId, "AAA", attempt, full.UpdatedUtc, full.UpdatedUtc.AddSeconds(1), 10m, 10m, 10m, "Fixture"), "Test", "{}");
        full = full with { Fills = full.Fills.SetItem(full.Fills.Length - 2, buy), Actions = [action], Quotes = [Quote(1), Quote(2)] };
        var state = DelphiLiveWorkingState.Select(full).Snapshot;
        state.Fills.Count(f => f.Side == DelphiLiveActionSide.Buy && f.TradingDate == today).ShouldBe(1);
        state.PendingActions.Single().ShouldBe(action);
        state.Quotes.Single().Observation.AttemptNumber.ShouldBe(2);
        state.Positions.Single().ExitActionId.ShouldBe(last.ExitActionId);
    }

    private static DelphiLivePortfolioSnapshot History(int count)
    {
        var now = new DateTime(2026, 10, 22, 14, 0, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", new(2026, 1, 1), now.AddYears(-1), now.AddYears(-2), "Fixture", "Test"));
        var positions = ImmutableArray.CreateBuilder<DelphiLiveLedgerPosition>();
        var fills = ImmutableArray.CreateBuilder<DelphiLiveLedgerFill>();
        for (int i = 0; i < count; i++)
        {
            Guid position = Guid.NewGuid(), buy = Guid.NewGuid(), sell = Guid.NewGuid();
            DateTime opened = now.AddDays(-count + i - 1);
            positions.Add(new(position, "AAA", 1, 10m, opened, buy, "{}",
                DelphiLiveProfitProtectionState.Open(position, 10m), opened.AddHours(1), sell));
            fills.Add(new(Guid.NewGuid(), buy, position, Guid.NewGuid(), "AAA", DelphiLiveActionSide.Buy, 1, 10m,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, opened, DateOnly.FromDateTime(opened)));
            fills.Add(new(Guid.NewGuid(), sell, position, Guid.NewGuid(), "AAA", DelphiLiveActionSide.Sell, 1, 11m,
                DelphiLiveQuoteField.Bid, DelphiLiveFillConfidence.SideSpecific, opened.AddHours(1), DateOnly.FromDateTime(opened)));
        }
        return state with { CurrentSession = new(2026, 10, 22), UpdatedUtc = now, Positions = positions.ToImmutable(),
            Fills = fills.ToImmutable(), Cash = 1000m + count };
    }
}
