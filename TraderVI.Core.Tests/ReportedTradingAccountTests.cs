#nullable enable
using Core.Trader.Promotion;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ReportedTradingAccountTests
{
    [Fact]
    public void MissingAccountStateCannotBeReplacedByPaperCapital() =>
        Assert.False(Check(null).CanConsiderNewRisk);

    [Fact]
    public void AReconciledCurrentAccountCanBeConsideredWithoutGrantingAnOrder() =>
        Assert.True(Check(Account()).CanConsiderNewRisk);

    [Fact]
    public void AccountIsolationAndChangedFinancialFactsBlockNewRisk()
    {
        Assert.False(Check(Account() with { AccountLabel = "RRSP" }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { LedgerFingerprint = new string('B',64) }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { Currency = "USD" }).CanConsiderNewRisk);
    }

    [Fact]
    public void MissingCashRiskBaselineOrOrderReportIsNotAZeroValue()
    {
        Assert.False(Check(Account() with { AvailableCash = null }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { SessionOpeningValue = null }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { HighestClosingValue = null }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { PendingOrderCount = null }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { PendingOrderCount = 1 }).CanConsiderNewRisk);
    }

    [Fact]
    public void StaleAndFutureReportsCannotEnableNewRisk()
    {
        Assert.False(Check(Account() with { TradingDate = Date.AddDays(-1) }).CanConsiderNewRisk);
        Assert.False(Check(Account() with { ReportedUtc = TradingStrategyResolutionTests.Utc.AddSeconds(1) }).CanConsiderNewRisk);
    }

    [Fact]
    public void PauseAndRiskHoldsPreserveReportedHoldings()
    {
        var holding = new ReportedTradingHolding(Guid.NewGuid(), "ABC", 10, 120m, Date);
        var account = Account() with { Holdings = [holding], DailyLossHold = true, CapitalReviewRequired = true };
        var readiness = TradingAccountReadinessPolicy.Evaluate(account, "TFSA", Date,
            TradingStrategyResolutionTests.Utc, Hash, true);
        Assert.False(readiness.CanConsiderNewRisk);
        Assert.Contains("Trading entries are paused.", readiness.NewRiskBlocks);
        Assert.Contains("The account's daily loss hold remains active.", readiness.NewRiskBlocks);
        Assert.Equal(holding, Assert.Single(account.Holdings));
    }

    private static readonly DateOnly Date = new(2026,9,8);
    private static readonly string Hash = new('A',64);
    private static ReportedTradingAccount Account() => new(Guid.NewGuid(), "TFSA", "CAD", Date,
        TradingStrategyResolutionTests.Utc, "operator", Hash, 1000m, 1000m, 1000m, 1000m, 0, false, false, []);
    private static TradingAccountReadiness Check(ReportedTradingAccount? state) =>
        TradingAccountReadinessPolicy.Evaluate(state, "TFSA", Date, TradingStrategyResolutionTests.Utc, Hash, false);
}
