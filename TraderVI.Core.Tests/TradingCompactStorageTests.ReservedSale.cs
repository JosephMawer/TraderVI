#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    private sealed class SaleSafety(string? block) : IManualBuySafetyLease
    {
        public string? BlockReason => block;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("send")]
    [InlineData("reply lost")]
    [InlineData("same holding already sold")]
    [InlineData("permission revoked")]
    [InlineData("missing connection")]
    [InlineData("risk hold")]
    [InlineData("outside market hours")]
    public async Task ProtectiveSenderUsesTheSavedDecisionAndNeverAsksForAnotherPrice(string scenario)
    {
        var f = new ManualMarketSellWorkflowTests.Fixture();
        var security = f.Broker.Context.Security;
        string identity = security.SourceIdentity!;
        var account = f.Broker.Context.Account;
        var at = f.Clock.Now;
        var date = DateOnly.FromDateTime(at.UtcDateTime);
        var calendar = new ReviewedTsxSessionCalendar(new("test", "fixture", date.AddDays(-1), date.AddDays(1), [date]));
        var decision = Guid.NewGuid();
        var position = Position() with { SecurityId = security.Id, Symbol = security.Symbol, Quantity = 10, ReservedSellQuantity = 10,
            ExitDecisionId = decision, FirstEntryUtc = at.UtcDateTime.AddDays(-1), ExitTriggeredUtc = at.UtcDateTime.AddSeconds(-5) };
        var order = new TradingOrder { BindingId = Binding, PositionId = position.PositionId, PermissionId = Permission,
            DecisionId = decision, RequestId = BrokerManagedStrategy.StableId(decision.ToString("D")), Side = "SELL",
            Quantity = 10, ReservedQuantity = 10, SecurityId = position.SecurityId, Symbol = position.Symbol };
        var state = new TradingCurrentState(Account with { Phase = "Active", ExecutionEnabled = true, BrokerObservedUtc = at.UtcDateTime.AddDays(-1) },
            Settings with { ApprovedUtc = at.UtcDateTime.AddDays(-2), SellAllowed = scenario != "permission revoked" },
            [position], [], [order], [], []);
        using var temp = new ManualMarketSellWorkflowTests.TempDirectory();
        using var journal = new ManualBuyFileJournal(temp.Path, new ManualMarketSellWorkflowTests.Protector());
        journal.PrepareRestartSellScopes(identity, account.Id, new ManualBuyFileJournal.RestartHolding(security.Id, position.PositionId, position.FirstEntryUtc));
        if (scenario == "same holding already sold")
            journal.SaveSell(new(new(Guid.NewGuid(), identity, account.Id, security.Id, account.DisplayName, security.Symbol,
                security.Exchange, 10, at, f.Broker.Context.Quote, at), null, []));
        if (scenario == "missing connection") f.Broker.IsConnected = false;
        if (scenario == "outside market hours") f.Clock.Now = at.AddHours(7);
        using var gate = new SemaphoreSlim(1, 1);
        var sender = new ReservedProtectiveSale(f.Broker, account, (_, _) => Task.FromResult(
            new WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>(WealthsimpleReadStatus.Success, [security])),
            journal, gate, calendar, f.Clock, (_, _) => Task.FromResult<IManualBuySafetyLease>(new SaleSafety(scenario == "risk hold" ? "Hold" : null)));
        Task Send() => sender.SendAsync(state, position, order, identity, default);
        bool dispatch = scenario is "send" or "reply lost";
        if (dispatch)
        {
            f.Broker.ThrowSubmit = scenario == "reply lost";
            f.Broker.OnSubmit = sale => Assert.Equal(order.RequestId, journal.FindSell(sale.RequestId)!.Intent.RequestId);
            await Send();
            Assert.Equal(decision, journal.FindSell(order.RequestId)!.Intent.ProtectiveSale!.DecisionId);
            Assert.Null(journal.FindSell(order.RequestId)!.Intent.CheckedQuote);
            Assert.Equal(scenario == "reply lost" ? WealthsimpleSubmissionStatus.OutcomeUnknown :
                WealthsimpleSubmissionStatus.Acknowledged, journal.FindSell(order.RequestId)!.Submission);
            await Assert.ThrowsAsync<InvalidOperationException>(Send);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(Send);
        Assert.Equal(dispatch ? 1 : 0, f.Broker.Submissions);
        Assert.Equal(0, f.Broker.ContextReads);
    }
}
