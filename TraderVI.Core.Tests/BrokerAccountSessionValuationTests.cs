#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerAccountSessionValuationTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private static readonly DateTime Close = ReviewedTsxSessionCalendar.At(Date, new(16, 0));
    private static readonly DateTimeOffset Now = Open.AddHours(2);
    private static readonly Guid Binding = Guid.NewGuid();
    private static readonly ITsxSessionCalendar Calendar = new ReviewedTsxSessionCalendar(new(
        "synthetic", "offline fixture", Date.AddDays(-1), Date.AddDays(1), [Date]));
    private static BrokerRiskBoundaryBook Book() => new(Guid.NewGuid(), Binding, Open, Open.AddMinutes(1),
        1000m, [new(Guid.NewGuid(), "ABC", 10, 80m, Guid.NewGuid(), Open.AddDays(-3))]);
    private static BrokerRiskHoldingBar Bar() => new("ABC", new(Guid.NewGuid(), Open, Open.AddMinutes(5),
        5, 100m, 111m, 99m, 110m));
    private static BrokerRiskValuationResult Calculate(BrokerRiskBoundaryBook? book = null,
        ImmutableArray<BrokerRiskHoldingBar>? bars = null, TradingStrategyFamily family = TradingStrategyFamily.DelphiLive,
        BrokerRiskBoundaryKind kind = BrokerRiskBoundaryKind.Opening, DateTimeOffset? now = null) =>
        BrokerAccountSessionValuation.Calculate(Guid.NewGuid(), family, kind, Date, book ?? Book(), bars ?? [Bar()], Calendar, now ?? Now);

    [Fact]
    public void LiveUsesFirstBarOpenWithTheActualOpeningBookEvenWhenReadLater()
    {
        var original = Book();
        var result = Calculate(original, [Bar() with { Bar = Bar().Bar with { ReceivedUtc = Now.UtcDateTime } }]);
        Assert.True(result.Complete);
        Assert.Equal(2000m, result.Value!.Value);
        Assert.Equal(Open, result.Value.At.UtcDateTime);
        Assert.Equal(Now, result.Value.ObservedAt);
        Assert.Equal(BrokerAccountSessionValuation.LiveOpening, result.Value.ValuationContract);
        Assert.Equal(10, Assert.Single(original.Holdings).Quantity);
    }

    [Fact]
    public void ShadowPreservesItsSavedPriceDefinitionInsteadOfQuietlyBecomingLive()
    {
        var result = Calculate(family: TradingStrategyFamily.DailyShadow);
        Assert.True(result.Complete);
        Assert.Equal(1800m, result.Value!.Value);
        Assert.Equal(BrokerAccountSessionValuation.ShadowOpening, result.Value.ValuationContract);
    }

    [Fact]
    public void LiveClosingUsesExactFinalBarCloseAndCannotBeRecordedEarly()
    {
        var book = Book() with { At = Close, VerifiedAt = Close.AddSeconds(1) };
        var bar = Bar() with { Bar = Bar().Bar with { StartUtc = Close.AddMinutes(-5), ReceivedUtc = Close.AddSeconds(2) } };
        var result = Calculate(book, [bar], kind: BrokerRiskBoundaryKind.Closing, now: Close.AddMinutes(1));
        Assert.True(result.Complete);
        Assert.Equal(2100m, result.Value!.Value);
        Assert.Equal(Close, result.Value.At.UtcDateTime);
        Assert.Equal(BrokerAccountSessionValuation.SessionClosing, result.Value.ValuationContract);
        Assert.Throws<ArgumentException>(() => Calculate(book, [bar], kind: BrokerRiskBoundaryKind.Closing, now: Close.AddSeconds(-1)));
    }

    [Fact]
    public void ShadowClosingKeepsItsSavedBookDefinition()
    {
        var book = Book() with { At = Close, VerifiedAt = Close.AddMinutes(1) };
        var result = Calculate(book, [], TradingStrategyFamily.DailyShadow, BrokerRiskBoundaryKind.Closing, Close.AddMinutes(1));
        Assert.True(result.Complete);
        Assert.Equal(BrokerAccountSessionValuation.ShadowClosing, result.Value!.ValuationContract);
        Assert.Equal(1800m, result.Value.Value);
    }

    [Theory]
    [InlineData("wrong date")]
    [InlineData("later book")]
    [InlineData("unverified")]
    [InlineData("future")]
    [InlineData("negative cash")]
    [InlineData("duplicate position")]
    [InlineData("duplicate symbol")]
    [InlineData("quantity")]
    [InlineData("identity")]
    [InlineData("no holdings array")]
    public void UnreconciledOrWrongBoundaryBookCannotBecomeAnOpening(string change)
    {
        var book = Book();
        book = change switch
        {
            "wrong date" => book with { At = Open.AddDays(-1) },
            "later book" => book with { At = Open.AddMinutes(1) },
            "unverified" => book with { VerifiedAt = Open.AddTicks(-1) },
            "future" => book with { VerifiedAt = Now.AddTicks(1) },
            "negative cash" => book with { Cash = -1m },
            "duplicate position" => book with { Holdings = book.Holdings.Add(book.Holdings[0] with { Symbol = "DEF" }) },
            "duplicate symbol" => book with { Holdings = book.Holdings.Add(book.Holdings[0] with { PositionId = Guid.NewGuid(), Symbol = "abc" }) },
            "quantity" => book with { Holdings = book.Holdings.SetItem(0, book.Holdings[0] with { Quantity = 0 }) },
            "identity" => book with { BindingId = Guid.Empty },
            _ => book with { Holdings = default }
        };
        Assert.Throws<ArgumentException>(() => Calculate(book));
    }

    [Theory]
    [InlineData("wrong date")]
    [InlineData("later bar")]
    [InlineData("wrong duration")]
    [InlineData("duplicate")]
    [InlineData("future receipt")]
    [InlineData("unfinished bar")]
    [InlineData("price")]
    [InlineData("identity")]
    [InlineData("wrong symbol")]
    public void MissingOrAmbiguousBarsProduceNoSubstituteValue(string change)
    {
        var item = Bar();
        item = change switch
        {
            "wrong date" => item with { Bar = item.Bar with { StartUtc = Open.AddDays(-1) } },
            "later bar" => item with { Bar = item.Bar with { StartUtc = Open.AddMinutes(5) } },
            "wrong duration" => item with { Bar = item.Bar with { Minutes = 15 } },
            "future receipt" => item with { Bar = item.Bar with { ReceivedUtc = Now.AddTicks(1).UtcDateTime } },
            "unfinished bar" => item with { Bar = item.Bar with { ReceivedUtc = Open.AddMinutes(4) } },
            "price" => item with { Bar = item.Bar with { Open = 0m } },
            "identity" => item with { Bar = item.Bar with { EvidenceId = Guid.Empty } },
            "wrong symbol" => item with { Symbol = "DEF" },
            _ => item
        };
        var result = Calculate(bars: change == "duplicate" ? [item, item] : [item]);
        Assert.False(result.Complete);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.MissingEvidence);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("identity")]
    [InlineData("date")]
    [InlineData("future")]
    public void ShadowRequiresIdentifiedSavedMarks(string change)
    {
        var book = Book();
        var holding = book.Holdings[0];
        holding = change switch
        {
            "price" => holding with { SavedPrice = null },
            "identity" => holding with { SavedPriceEvidenceId = null },
            "date" => holding with { SavedPriceAt = null },
            _ => holding with { SavedPriceAt = Open.AddMinutes(1) }
        };
        Assert.False(Calculate(book with { Holdings = [holding] }, family: TradingStrategyFamily.DailyShadow).Complete);
    }

    [Fact]
    public void ARealCashOnlyAccountNeedsNoFictitiousPriceBars()
    {
        var result = Calculate(Book() with { Holdings = [] }, []);
        Assert.True(result.Complete);
        Assert.Equal(1000m, result.Value!.Value);
        Assert.False(Calculate(Book() with { Holdings = [], Cash = 0m }, []).Complete);
    }

    [Fact]
    public void CalendarHandlesWinterOffsetAndRejectsUncoveredDates()
    {
        var winter = new DateOnly(2026, 1, 5);
        var calendar = new ReviewedTsxSessionCalendar(new("winter", "fixture", winter, winter, [winter]));
        var at = calendar.GetSessionBounds(winter).OpenUtc;
        var book = Book() with { At = at, VerifiedAt = at.AddSeconds(1), Holdings = [] };
        var result = BrokerAccountSessionValuation.Calculate(Guid.NewGuid(), TradingStrategyFamily.DelphiLive,
            BrokerRiskBoundaryKind.Opening, winter, book, [], calendar, at.AddMinutes(1));
        Assert.Equal(14, result.Value!.At.UtcDateTime.Hour);
        Assert.Throws<InvalidOperationException>(() => BrokerAccountSessionValuation.Calculate(Guid.NewGuid(),
            TradingStrategyFamily.DelphiLive, BrokerRiskBoundaryKind.Opening, Date, Book(), [Bar()], calendar, Now));
    }

    [Fact]
    public void GraphComparisonUsesTheSelectedAccountAndExactDateOnly()
    {
        var value = Calculate().Value!;
        var account = new WealthsimpleTfsaAccount("synthetic-tfsa", "TFSA");
        var graph = new WealthsimpleAccountDayValues(account, Now, new(Open.AddDays(-3), value.Value, null),
            [new(Open.AddMinutes(5), value.Value, 0m)]);
        Assert.Null(BrokerAccountSessionValuation.CompareGraph(value, graph, account));
        graph = new(account, Now, null, [new(Open, value.Value, 0m)]);
        Assert.True(BrokerAccountSessionValuation.CompareGraph(value, graph, account));
        Assert.False(BrokerAccountSessionValuation.CompareGraph(value with { Value = value.Value + 1m }, graph, account));
        Assert.Throws<ArgumentException>(() => BrokerAccountSessionValuation.CompareGraph(value, graph,
            new WealthsimpleTfsaAccount("other", "Other")));
    }

    [Fact]
    public void SourceFingerprintIsStableAcrossInputOrderingAndChangesWithEvidence()
    {
        var book = Book();
        var bar = Bar();
        var original = Calculate(book, [bar]).Value!;
        Assert.Equal(original.SourceFingerprint, Calculate(book, [bar]).Value!.SourceFingerprint);
        Assert.NotEqual(original.SourceFingerprint, Calculate(book with { Cash = 999m }, [bar]).Value!.SourceFingerprint);
        Assert.NotEqual(original.SourceFingerprint, Calculate(book, [bar with { Bar = bar.Bar with { EvidenceId = Guid.NewGuid() } }]).Value!.SourceFingerprint);
        Assert.DoesNotContain("2000", original.ToString());
    }
}
