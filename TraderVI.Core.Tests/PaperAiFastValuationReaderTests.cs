#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperAiFastValuationReaderTests
{
    private static readonly DateOnly Date = new(2026, 9, 28);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private readonly Guid portfolio = Guid.NewGuid();
    private readonly Clock clock = new(Open.AddMinutes(10).AddSeconds(10));
    private readonly ITsxSessionCalendar calendar = new ReviewedTsxSessionCalendar(new("test", "synthetic", Date, Date, [Date]));
    private int requests;

    private SystemShadowSavedRiskBook ShadowBook()
    {
        var generation = Guid.NewGuid();
        var json = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1);
        return new(new(generation, SystemShadowVersions.Policy, SystemShadowGenerationStatus.Active, 10000m, 10000m,
                Open.AddDays(-1), Open.AddDays(-1), Open.AddDays(-1)),
            new(portfolio, generation, "BreakoutTop3", "Breakout", 3, "Active", 5000m, 10000m, Open.AddDays(-1)),
            new(Guid.NewGuid(), portfolio, Date.ToDateTime(TimeOnly.MinValue), null, "Active", null, 10000m, true),
            [new(Guid.NewGuid(), portfolio, "TEST", "Open", 50, 100m, 5000m, 5000m, Open.AddMinutes(1), Date.ToDateTime(TimeOnly.MinValue),
                0, 0, null, null, 95m, false, 100m, Open, 0m, null, null)], [],
            Open.AddMinutes(1), Open.AddMinutes(1), false, Guid.NewGuid(), json, EngineStrategySettings.Hash(json), Open.AddDays(-1), null,
            EngineStrategySettings.Shadow);
    }

    private DelphiLivePortfolioSnapshot LiveBook()
    {
        var position = Guid.NewGuid();
        var buy = Guid.NewGuid();
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), portfolio,
            DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 10000m, "CAD",
            Date, Open, Open.AddDays(-1), "Test", "Synthetic"));
        return snapshot with { Revision = 1, UpdatedUtc = Open.AddMinutes(1), CurrentSession = Date, Cash = 5000m,
            Guards = snapshot.Guards with { DailyBuyingPaused = true },
            Positions = [new(position, "TEST", 50, 100m, Open.AddMinutes(1), buy, "{}", DelphiLiveProfitProtectionState.Open(position, 100m))],
            Fills = [new(Guid.NewGuid(), buy, position, Guid.NewGuid(), "TEST", DelphiLiveActionSide.Buy, 50, 100m,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, Open.AddMinutes(1), Date)], Marks = [] };
    }

    private TmxAiRiskPriceSource Prices(Func<string, OhlcvBar[]>? bars = null, Action? during = null) =>
        new((symbol, start, end, _) =>
        {
            requests++;
            var began = clock.Now;
            during?.Invoke();
            return Task.FromResult(new TmxIntradayBatch(symbol, 1, start, end, began, clock.Now, 1, 1,
                bars?.Invoke(symbol) ?? [new(Open.AddMinutes(9), 110m, 110m, 110m, 110m, 100)]));
        }, calendar, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), clock);

    private PaperAiFastValuationReader Shadow(Func<SystemShadowSavedRiskBook?> read, TmxAiRiskPriceSource? prices = null) =>
        new(portfolio, new ShadowSource((id, date, _) =>
        {
            Assert.Equal(portfolio, id);
            var book = read();
            return Task.FromResult(book is null ? null : (book with { VerifiedReadUtc = clock.Now }).BuildAccount(id, date));
        }), prices ?? Prices(), calendar, TimeSpan.FromSeconds(45), clock);

    private PaperAiFastValuationReader Live(Func<DelphiLivePortfolioSnapshot?> read, TmxAiRiskPriceSource? prices = null) =>
        new(portfolio, new LiveSource((id, _) => { Assert.Equal(portfolio, id); return Task.FromResult(read()); }),
            prices ?? Prices(), calendar, TimeSpan.FromSeconds(45), clock);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CombinesActualCashAndSharesWithNewPricesWithoutRewritingHistory(bool live)
    {
        var shadow = ShadowBook();
        var delphi = LiveBook();
        string original = JsonSerializer.Serialize(live ? (object)delphi : shadow);
        int reads = 0;
        var reader = live ? Live(() => { reads++; return delphi; }) : Shadow(() => { reads++; return shadow; });
        var result = await reader.ReadAsync(Date);
        Assert.Null(result.BlockReason);
        var value = Assert.IsType<AiPaperAccountValuation>(result.Valuation);
        Assert.Equal(5000m, value.Cash);
        Assert.Equal(5500m, value.HoldingsValue);
        Assert.Equal(10500m, value.Value);
        Assert.Equal(Open.AddMinutes(10), value.AtUtc);
        Assert.Equal(clock.Now.AddSeconds(30), value.ValidUntilUtc);
        Assert.Contains("DailyBuyingPaused", value.SavedBlocks);
        Assert.True(value.IsFreshAt(clock.Now));
        Assert.False(value.IsFreshAt(value.ValidUntilUtc));
        Assert.Equal(2, reads);
        Assert.Equal(1, requests);
        Assert.Equal(original, JsonSerializer.Serialize(live ? (object)delphi : shadow));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.EvidenceJson))), value.EvidenceSha256);
        using var evidence = JsonDocument.Parse(value.EvidenceJson);
        Assert.Equal(portfolio, evidence.RootElement.GetProperty("PortfolioId").GetGuid());
        Assert.Contains("TEST", evidence.RootElement.GetProperty("After").GetProperty("BookJson").GetString());
        Assert.Single(evidence.RootElement.GetProperty("Prices").GetProperty("Results").EnumerateArray());
        Assert.DoesNotContain("10500", value.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashOnlyUsesVerifiedBookWithoutAnyPriceRequest(bool live)
    {
        var s = ShadowBook();
        s = s with { Positions = [], Portfolio = s.Portfolio with { CashBalance = 10000m }, LastFillUtc = null };
        var d = LiveBook() with { Cash = 10000m, Positions = [], Fills = [] };
        var read = await (live ? Live(() => d) : Shadow(() => s)).ReadAsync(Date);
        Assert.Null(read.BlockReason);
        Assert.Equal(10000m, read.Valuation!.Value);
        Assert.Equal(0m, read.Valuation.HoldingsValue);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false, "cash")]
    [InlineData(true, "cash")]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    [InlineData(false, "history")]
    [InlineData(true, "history")]
    public async Task RejectsUnreconciledCashWrongOwnerAndMissingHistoryBeforeCollection(bool live, string defect)
    {
        var s = ShadowBook();
        var d = LiveBook();
        s = defect switch
        {
            "cash" => s with { Portfolio = s.Portfolio with { CashBalance = 6000m } },
            "owner" => s with { Portfolio = s.Portfolio with { PortfolioId = Guid.NewGuid() } },
            _ => s with { Positions = [] }
        };
        d = defect switch
        {
            "cash" => d with { Cash = 6000m },
            "owner" => d with { PortfolioId = Guid.NewGuid() },
            _ => d with { Fills = [] }
        };
        Assert.Null((await (live ? Live(() => d) : Shadow(() => s)).ReadAsync(Date)).Valuation);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsBookChangeWhilePricesAreCollected(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        var prices = Prices(during: () =>
        {
            s = s with { Session = s.Session with { DailyLossGuardActive = false } };
            d = d with { Revision = d.Revision + 1 };
        });
        var read = await (live ? Live(() => d, prices) : Shadow(() => s, prices)).ReadAsync(Date);
        Assert.Equal("AccountChangedDuringPrices", read.BlockReason);
        Assert.Null(read.Valuation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowSecondReadCannotRenewExpiredPrice(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        int calls = 0;
        void SlowRead() { if (++calls == 2) clock.Now = clock.Now.AddSeconds(31); }
        var reader = live ? Live(() => { SlowRead(); return d; }) : Shadow(() => { SlowRead(); return s; });
        Assert.Equal("PricesIncompleteOrExpired", (await reader.ReadAsync(Date)).BlockReason);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("mixed")]
    public async Task EveryHeldStockNeedsAFreshPriceAtTheSameMinute(string defect)
    {
        var s = ShadowBook();
        s = s with { Portfolio = s.Portfolio with { CashBalance = 0m },
            Positions = s.Positions.Add(s.Positions[0] with { PositionId = Guid.NewGuid(), Symbol = "OTHER" }) };
        var prices = Prices(symbol => symbol == "TEST" && defect == "missing" ? [] :
            [new(Open.AddMinutes(symbol == "TEST" ? defect == "stale" ? 6 : defect == "mixed" ? 8 : 9 : 9), 110, 110, 110, 110, 100)]);
        var read = await Shadow(() => s, prices).ReadAsync(Date);
        Assert.Null(read.Valuation);
        Assert.Equal(defect == "mixed" ? "PricesHaveDifferentTimes" : "PricesIncompleteOrExpired", read.BlockReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TradeAfterMarketPriceBlocksValueEvenWhenBookIsStable(bool live)
    {
        var s = ShadowBook() with { LastFillUtc = Open.AddMinutes(10).AddSeconds(5) };
        var d = LiveBook();
        d = d with { Positions = [d.Positions[0] with { OpenedUtc = Open.AddMinutes(10).AddSeconds(5) }],
            Fills = [d.Fills[0] with { FilledUtc = Open.AddMinutes(10).AddSeconds(5) }] };
        Assert.Null((await (live ? Live(() => d) : Shadow(() => s)).ReadAsync(Date)).Valuation);
    }

    [Fact]
    public async Task RepeatedPriceKeepsItsMarketTimeAndExpiry()
    {
        var s = ShadowBook();
        var prices = Prices();
        var reader = Shadow(() => s, prices);
        var first = (await reader.ReadAsync(Date)).Valuation!;
        clock.Now = Open.AddMinutes(11).AddSeconds(45);
        var repeat = (await reader.ReadAsync(Date)).Valuation!;
        Assert.Equal(first.AtUtc, repeat.AtUtc);
        Assert.Equal(Open.AddMinutes(12), repeat.ValidUntilUtc); // Market age wins over the newer receipt.
        clock.Now = Open.AddMinutes(12).AddSeconds(1);
        Assert.Null((await reader.ReadAsync(Date)).Valuation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidTradeDuringCollectionStillRequiresANewRead(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        var prices = Prices(during: () =>
        {
            s = s with { Portfolio = s.Portfolio with { CashBalance = 4000m },
                Positions = [s.Positions[0] with { Shares = 60, CostBasis = 6000m, AddOnCount = 1 }],
                LastFillUtc = clock.Now, UpdatedUtc = clock.Now };
            d = d with { Revision = 2, Cash = 10500m, UpdatedUtc = clock.Now,
                Positions = [d.Positions[0] with { ClosedUtc = clock.Now, ExitActionId = Guid.NewGuid() }] };
            d = d with { Fills = d.Fills.Add(new(Guid.NewGuid(), d.Positions[0].ExitActionId!.Value, d.Positions[0].PositionId,
                Guid.NewGuid(), "TEST", DelphiLiveActionSide.Sell, 50, 110m, DelphiLiveQuoteField.Bid,
                DelphiLiveFillConfidence.SideSpecific, clock.Now, Date)) };
        });
        var read = await (live ? Live(() => d, prices) : Shadow(() => s, prices)).ReadAsync(Date);
        Assert.Null(read.Valuation);
        Assert.Equal("AccountChangedDuringPrices", read.BlockReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashWithCompletedTradeHistoryReconcilesAcrossSeparateReadReceipts(bool live)
    {
        var s = ShadowBook();
        s = s with { Portfolio = s.Portfolio with { CashBalance = 10500m }, LastFillUtc = Open.AddMinutes(2),
            Positions = [s.Positions[0] with { Status = "Closed", ExitUtc = Open.AddMinutes(2), RealizedProfitLoss = 500m }] };
        var d = LiveBook();
        var sell = Guid.NewGuid();
        d = d with { Cash = 10500m, Positions = [d.Positions[0] with { ClosedUtc = Open.AddMinutes(2), ExitActionId = sell }],
            Fills = d.Fills.Add(new(Guid.NewGuid(), sell, d.Positions[0].PositionId, Guid.NewGuid(), "TEST", DelphiLiveActionSide.Sell,
                50, 110m, DelphiLiveQuoteField.Bid, DelphiLiveFillConfidence.SideSpecific, Open.AddMinutes(2), Date)) };
        var reader = live ? Live(() => { clock.Now = clock.Now.AddSeconds(1); return d; }) :
            Shadow(() => { clock.Now = clock.Now.AddSeconds(1); return s; });
        var read = await reader.ReadAsync(Date);
        Assert.Null(read.BlockReason);
        Assert.Equal(10500m, read.Valuation!.Value);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSecondAccountCannotReuseFirstRead(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        int reads = 0;
        var reader = live ? Live(() => ++reads == 1 ? d : null) : Shadow(() => ++reads == 1 ? s : null);
        Assert.Equal("AccountUnavailable", (await reader.ReadAsync(Date)).BlockReason);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowCashReadsExpireEvenWithoutMarketPrices(bool live)
    {
        var s = ShadowBook();
        s = s with { Positions = [], Portfolio = s.Portfolio with { CashBalance = 10000m }, LastFillUtc = null };
        var d = LiveBook() with { Positions = [], Fills = [], Cash = 10000m };
        var reader = live ? Live(() => { clock.Now = clock.Now.AddSeconds(25); return d; }) :
            Shadow(() => { clock.Now = clock.Now.AddSeconds(25); return s; });
        Assert.Equal("AccountOrPricesExpired", (await reader.ReadAsync(Date)).BlockReason);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task CorporateActionIsNotBypassedByFasterPrices()
    {
        var d = LiveBook();
        d = d with { Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, Open.AddMinutes(5),
            false, null, [], "CorporateActionUnsupported")] };
        Assert.Equal("InvalidAccountSession", (await Live(() => d).ReadAsync(Date)).BlockReason);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndStorageFailurePropagateWithoutValue(bool live)
    {
        var reader = live ? Live(() => throw new IOException("Synthetic read failure")) : Shadow(() => throw new IOException("Synthetic read failure"));
        await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync(Date));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(Date, cancellation.Token));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task OutsideSessionDoesNotReadOrCollect()
    {
        clock.Now = Open.AddHours(7);
        var reader = Shadow(() => throw new Exception("Must not read"));
        Assert.Equal("OutsideSession", (await reader.ReadAsync(Date)).BlockReason);
        Assert.Equal(0, requests);
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class ShadowSource(Func<Guid, DateOnly, CancellationToken, Task<ShadowPaperAccountRead?>> read) : ISystemShadowAccountReader
    {
        public Task<ShadowPaperAccountRead?> ReadAccountAsync(Guid id, DateOnly date, CancellationToken ct = default) => read(id, date, ct);
    }
    private sealed class LiveSource(Func<Guid, CancellationToken, Task<DelphiLivePortfolioSnapshot?>> read) : IDelphiLivePortfolioReader
    {
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid id, CancellationToken ct = default) => read(id, ct);
    }
}
