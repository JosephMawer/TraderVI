using Core.WSTrade.Orders;
using Sandbox.Probes;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class WealthsimpleQuoteTimingProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);
    private static ManualBuyQuote Quote => new(10, 9, Now.AddSeconds(-5), "OPEN", Now.AddHours(-1), Now.AddHours(1), Now);

    [Fact]
    public void CurrentTwoSidedRegularQuotePassesDiagnosticOnly() => Assert.True(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote));

    [Fact]
    public void ReceiptDoesNotRenewOldOrFuturePrices()
    {
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { QuotedAt = Now.AddSeconds(-121) }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { QuotedAt = Now.AddSeconds(1) }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { QuotedAt = Now.AddDays(-1) }));
    }

    [Fact]
    public void MissingCrossedClosedOrOutOfSessionQuotesDoNotPass()
    {
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(null));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { Bid = 0 }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { Bid = 11 }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { MarketStatus = "CLOSED" }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { SessionOpen = Now }));
        Assert.False(WealthsimpleQuoteTimingProbe.IsRecentRegularQuote(Quote with { SessionClose = Now }));
    }
}
