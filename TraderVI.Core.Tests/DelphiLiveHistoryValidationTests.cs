using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveHistoryValidationTests
{
    [Fact]
    public void AppendingRetainedImmutableHistoryDoesNotCopyHistoricalReports()
    {
        var prior = State(3000);
        var next = Advance(prior) with { Quotes = prior.Quotes.Add(Quote("new report")) };
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, next); // Warm shared code.
        long before = GC.GetAllocatedBytesForCurrentThread();
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, next);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 100_000, $"Retained history validation allocated {allocated:N0} bytes.");
        Assert.Equal(3000, prior.Quotes.Length);
        Assert.Same(prior.Quotes[0], next.Quotes[0]);
    }

    [Fact]
    public void ReopenedHistoryStillAcceptsOnlyExactSavedContent()
    {
        var prior = State(2);
        var reopened = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(DelphiLiveLedgerJson.Serialize(prior));
        Assert.NotSame(prior.Quotes[0], reopened.Quotes[0]);
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, Advance(reopened));
        var changed = reopened.Quotes.SetItem(0, reopened.Quotes[0] with { DossierJson = reopened.Quotes[0].DossierJson + " " });
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, Advance(reopened) with { Quotes = changed }));
    }

    [Fact]
    public void NumericEqualityDoesNotBypassExactHistoricalDecimalRepresentation()
    {
        var prior = State(1);
        var original = prior.Quotes[0] with { Observation = prior.Quotes[0].Observation with { Bid = 10.0m } };
        prior = prior with { Quotes = [original] };
        var changed = original with { Observation = original.Observation with { Bid = 10.00m } };
        Assert.Equal(original, changed); // Value equality alone would weaken the old JSON contract.
        Assert.NotEqual(DelphiLiveLedgerJson.Serialize(original), DelphiLiveLedgerJson.Serialize(changed));
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, Advance(prior) with { Quotes = [changed] }));
    }

    [Fact]
    public void ReorderingDroppingOrReplacingHistoricalQuotesStillFails()
    {
        var prior = State(2);
        var next = Advance(prior);
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, next with { Quotes = prior.Quotes.Reverse().ToImmutableArray() }));
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, next with { Quotes = [prior.Quotes[0]] }));
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, next with { Quotes = prior.Quotes.SetItem(0, Quote("replacement")) }));
    }

    private static readonly DateTime Open = new(2026, 10, 20, 13, 30, 0, DateTimeKind.Utc);
    private static DelphiLivePortfolioSnapshot State(int count)
    {
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", new(2026, 10, 20), Open, Open.AddDays(-1), "Fixture", "Offline"));
        string report = new('x', 20_000);
        return state with { Quotes = Enumerable.Range(0, count).Select(_ => Quote(report)).ToImmutableArray() };
    }
    private static DelphiLivePortfolioSnapshot Advance(DelphiLivePortfolioSnapshot prior) =>
        prior with { Revision = prior.Revision + 1, UpdatedUtc = Open.AddMinutes(1) };
    private static DelphiLiveLedgerQuote Quote(string report) => new(Guid.NewGuid(), null, null, "Protection", Guid.NewGuid(),
        new(Guid.NewGuid(), Guid.NewGuid(), "ABC", 1, Open, Open.AddSeconds(1), 10m, 9.9m, 10.1m, "Fixture"), "Observed", report);
}
