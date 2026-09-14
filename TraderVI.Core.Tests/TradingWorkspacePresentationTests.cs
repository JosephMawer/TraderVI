#nullable enable
using System;
using System.Collections.Immutable;
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingWorkspacePresentationTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Position = Guid.NewGuid();
    private static TradingAccountView Account(bool installed = true) => new(installed, "TFSA", "ledger", [new(Position, "EX1", 2, 20m, new(2026, 9, 1))], null, null, null);
    private static ActivePositionInfo Mark(string account = "TFSA", TrackedExecutionMode mode = TrackedExecutionMode.Real, int quantity = 2, decimal? price = 12m) =>
        new() { PositionId = Position, Symbol = "EX1", Shares = quantity, CostBasis = 20, AccountLabel = account, ExecutionMode = mode, IsActive = true, CurrentPrice = price, LastUpdatedUtc = Now.AddMinutes(-5) };

    [Theory]
    [InlineData("RRSP", TrackedExecutionMode.Real)]
    [InlineData("TFSA", TrackedExecutionMode.Ghost)]
    public void AnotherAccountOrPaperMarkCannotValueARealHolding(string account, TrackedExecutionMode mode)
    {
        var result = TradingAccountOverview.Create(Account(), [Mark(account, mode)], Now);
        Assert.Null(result.HoldingsValue); Assert.Null(result.OpenGain); Assert.Null(Assert.Single(result.Holdings).Value);
    }
    [Fact]
    public void MissingOrQuantityMismatchedMarksNeverBecomeZeroGains()
    {
        foreach (var mark in new[] { Mark(price: null), Mark(quantity: 3) })
        {
            var result = TradingAccountOverview.Create(Account(), [mark], Now);
            Assert.Null(result.OpenGain); Assert.Null(result.HoldingsValue);
        }
    }
    [Fact]
    public void SavedPricesHaveExplicitTimesAndDoNotEstablishTotalEarnings()
    {
        var result = TradingAccountOverview.Create(Account(), [Mark()], Now);
        Assert.Equal(24m, result.HoldingsValue); Assert.Equal(4m, result.OpenGain);
        Assert.Contains("Toronto", result.Holdings[0].ValuedAt);
        Assert.Contains("unavailable", result.TotalGainExplanation);
        Assert.Null(result.ReportedCash); Assert.Null(result.ReportedValue);
    }
    [Fact]
    public void UninstalledStorageIsNotAnEmptyFundedAccount()
    {
        var result = TradingAccountOverview.Create(Account(false) with { Holdings = [] }, [], Now, []);
        Assert.Null(result.OpenGain); Assert.Null(result.RecordedSaleGain); Assert.Null(result.HoldingsValue);
    }
    [Fact]
    public void AChangedLedgerDisclosesThatReportedValuesNeedReconciliation()
    {
        var account = Account();
        var report = new ReportedTradingAccount(Guid.NewGuid(), "TFSA", "CAD", new(2026,9,9), Now.AddHours(-1), "fixture", "previous-ledger", 100m, 120m, 110m, 120m, 0, false, false, account.Holdings);
        var result = TradingAccountOverview.Create(account with { Report = report }, [Mark()], Now);
        Assert.Equal(120m, result.ReportedValue); Assert.Contains("reconcile", result.ReportContext);
        Assert.Null(TradingAccountOverview.Create(account with { Report = report with { AccountLabel = "RRSP" } }, [Mark()], Now).ReportedValue);
    }
    [Fact]
    public void SaleGainsAreAccountModeAndPeriodScopedAndIncompleteSalesRemainUnavailable()
    {
        TradeLogInfo Sale(string account, TrackedExecutionMode mode, DateTime date, decimal? gain) =>
            new() { AccountLabel = account, ExecutionMode = mode, TradeType = "SELL", TradeDate = date, RealizedPnL = gain };
        var today = new DateTime(2026, 9, 9, 10, 0, 0);
        var sales = new[] { Sale("TFSA", TrackedExecutionMode.Real, today, 10), Sale("RRSP", TrackedExecutionMode.Real, today, 500),
            Sale("TFSA", TrackedExecutionMode.Ghost, today, 900), Sale("TFSA", TrackedExecutionMode.Real, today.AddDays(-35), 700) };
        Assert.Equal(10m, TradingAccountOverview.Create(Account(), [Mark()], Now, sales).RecordedSaleGain);
        Assert.Null(TradingAccountOverview.Create(Account(), [Mark()], Now, [Sale("TFSA", TrackedExecutionMode.Real, today, null)]).RecordedSaleGain);
    }
    [Fact]
    public void SameSymbolCanHaveDifferentStatesInDifferentLiveAccounts()
    {
        var normal = Guid.NewGuid(); var experimental = Guid.NewGuid();
        var blocked = Observation(normal, "DailyGateBlocked"); var held = Observation(experimental, "Held");
        Assert.Same(blocked, Assert.Single(DelphiLiveWatchlistScope.ForAccount([blocked, held], normal)));
        Assert.Same(held, Assert.Single(DelphiLiveWatchlistScope.ForAccount([blocked, held], experimental)));
        Assert.Empty(DelphiLiveWatchlistScope.ForAccount([blocked, held], Guid.NewGuid()));
        Assert.Empty(DelphiLiveWatchlistScope.ForAccount([blocked, held], null));
    }
    private static DelphiLiveObservationRow Observation(Guid account, string state) =>
        new("fixture", Guid.NewGuid(), 1, "EX1", "Continuation #1", null, "Neutral", "Normal", state, "Quiet", "", "", "", "", null, "13:55", state == "Held", false, "{}", state)
        { PortfolioId = account };
}
