#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;
using Core.Oracle;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperAiPortfolioRiskReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private readonly Guid portfolio = Guid.NewGuid();
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new("test", "synthetic", Date, Date, [Date])));
    private AiPortfolioRiskJournal Journal() => new(directory, new(portfolio, portfolio, AiRiskAccountMode.Paper), policy, true);

    private SystemShadowSavedRiskBook Book()
    {
        var generation = Guid.NewGuid();
        var json = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1);
        return new(new(generation, SystemShadowVersions.Policy, SystemShadowGenerationStatus.Active, 10000m, 10000m,
                Open.AddDays(-1), Open.AddDays(-1), Open.AddDays(-1)),
            new(portfolio, generation, "BreakoutTop3", "Breakout", 3, "Active", 5000m, 10000m, Open.AddDays(-1)),
            new(Guid.NewGuid(), portfolio, Date.ToDateTime(TimeOnly.MinValue), null, "Active", null, 10000m, false),
            [new(Guid.NewGuid(), portfolio, "TEST", "Open", 50, 100m, 5000m, 5000m, Open.AddMinutes(1), Date.ToDateTime(TimeOnly.MinValue),
                0, 0, null, null, null, false, 100m, Open.AddMinutes(5), 0m, null, null)],
            [new("TEST", new(Guid.NewGuid(), Open.AddMinutes(5), Open.AddMinutes(12), 5, 100m, 100m, 100m, 100m))],
            Open.AddMinutes(12), Open.AddMinutes(1), false, Guid.NewGuid(), json, EngineStrategySettings.Hash(json), Open.AddDays(-1), null,
            EngineStrategySettings.Shadow);
    }

    [Fact]
    public async Task SavedShadowBookFeedsRiskAndUnchangedReadsStayIdempotent()
    {
        using var journal = Journal();
        var book = Book();
        var source = new ShadowSource((id, date, _) => Task.FromResult(book.Build(id, date)));
        var clock = new Clock(Open.AddMinutes(12));
        var reader = new PaperAiPortfolioRiskReader(source, new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        var started = await reader.StartAsync(Date, true, []);
        Assert.True(started.Accepted);
        Assert.Equal(10000m, started.Snapshot.State!.RoundPeak);
        Assert.Equal(portfolio, source.RequestedPortfolio);
        Assert.Equal(Date, source.RequestedDate);
        Assert.Equal(book.AssignedVersion, book.Build(portfolio, Date)!.Portfolio.StrategyVersionId);
        Assert.False((await reader.ObserveAsync(Date, true, [])).Advanced);
        clock.Now = Open.AddMinutes(14);
        var stale = await reader.ObserveAsync(Date, true, []);
        Assert.False(stale.Accepted);
        Assert.Equal(0m, stale.Assessment.NewBuyAllowanceMultiplier);
        Assert.Single(journal.ReadHistory());
        Assert.Equal(started.Snapshot, journal.ReadCurrent());
    }

    [Fact]
    public async Task SavedShadowDropLatchesClosureAndMissingRecordsPreserveIt()
    {
        using var journal = Journal();
        var book = Book();
        ShadowPaperRiskObservation? observation = book.Build(portfolio, Date);
        var clock = new Clock(Open.AddMinutes(12));
        var reader = new PaperAiPortfolioRiskReader(new ShadowSource((_, _, _) => Task.FromResult(observation)),
            new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        await reader.StartAsync(Date, false, []);
        book = book with { UpdatedUtc = Open.AddMinutes(17),
            Positions = [book.Positions[0] with { LastPrice = 90m, LastPriceEventUtc = Open.AddMinutes(10) }],
            Prices = [new("TEST", new(Guid.NewGuid(), Open.AddMinutes(10), Open.AddMinutes(17), 5, 90m, 90m, 90m, 90m))] };
        observation = book.Build(portfolio, Date);
        clock.Now = Open.AddMinutes(17);
        var hit = await reader.ObserveAsync(Date, false, []);
        Assert.True(hit.Accepted);
        Assert.True(hit.Assessment.RequiresManagedLiquidation);
        observation = null;
        var missing = await reader.ObserveAsync(Date, true, []);
        Assert.False(missing.Accepted);
        Assert.True(missing.Assessment.RequiresManagedLiquidation);
        Assert.Equal(hit.Snapshot, missing.Snapshot);
    }

    [Theory]
    [InlineData("MissingAssignment")]
    [InlineData("MissingVersion")]
    [InlineData("EmptyVersion")]
    [InlineData("SettingsHash")]
    [InlineData("InvalidSettings")]
    [InlineData("MissingComparison")]
    [InlineData("ExtraComparison")]
    [InlineData("MissingPrice")]
    [InlineData("WrongPriceSymbol")]
    [InlineData("LaterFill")]
    [InlineData("LaterAddon")]
    [InlineData("WrongDate")]
    [InlineData("WrongPortfolio")]
    public void IncompleteOrMixedSavedBooksProduceNoObservation(string defect)
    {
        var book = Book();
        book = defect switch
        {
            "MissingAssignment" => book with { AssignedUtc = null },
            "MissingVersion" => book with { AssignedVersion = null },
            "EmptyVersion" => book with { AssignedVersion = Guid.Empty },
            "SettingsHash" => book with { SettingsHash = "wrong" },
            "InvalidSettings" => book with { SettingsJson = "{bad}", SettingsHash = EngineStrategySettings.Hash("{bad}") },
            "MissingComparison" => book with { IsGateComparison = true },
            "ExtraComparison" => book with { Comparison = Comparison(book) },
            "MissingPrice" => book with { Prices = [] },
            "WrongPriceSymbol" => book with { Prices = [book.Prices[0] with { Symbol = "OTHER" }] },
            "LaterFill" => book with { LastFillUtc = Open.AddMinutes(11) },
            // EntryUtc alone cannot detect an addition made after the valuation bar.
            "LaterAddon" => book with { LastFillUtc = Open.AddMinutes(11), Positions = [book.Positions[0] with { AddOnCount = 1, Shares = 60 }] },
            "WrongDate" => book with { Session = book.Session with { TradingDate = Date.AddDays(-1).ToDateTime(TimeOnly.MinValue) } },
            "WrongPortfolio" => book with { Portfolio = book.Portfolio with { PortfolioId = Guid.NewGuid() } },
            _ => throw new InvalidOperationException()
        };
        Assert.Null(book.Build(portfolio, Date));
    }

    private PaperComparisonAccount Comparison(SystemShadowSavedRiskBook book) => new(portfolio, Guid.NewGuid(), Guid.NewGuid(),
        "BreakoutTop3", PaperGateProfile.PortfolioRulesOnlyV1, Open.AddDays(-1), Guid.NewGuid(), null, Guid.NewGuid(), book.SettingsJson);

    [Fact]
    public void ComparisonUsesItsPinnedRulesAndIdentityInsteadOfOrdinaryAssignment()
    {
        var book = Book();
        var comparison = Comparison(book);
        book = book with { IsGateComparison = true, Comparison = comparison, AssignedVersion = null, AssignedUtc = null,
            SettingsJson = "invalid ordinary settings", SettingsHash = null };
        var observation = book.Build(portfolio, Date)!;
        Assert.Equal(comparison, observation.Portfolio.ComparisonAccount);
        Assert.Equal(comparison.ShadowStrategyVersionId, observation.Portfolio.StrategyVersionId);
        using var journal = Journal();
        Assert.True(new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3))
            .Start(observation, Open.AddMinutes(12), true, []).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteComparisonCannotFallBackToOrdinaryRules(bool missingVersion)
    {
        var book = Book();
        var comparison = Comparison(book);
        comparison = missingVersion ? comparison with { ShadowStrategyVersionId = null,
            ShadowSettingsJson = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default) } : comparison with { ShadowSettingsJson = null };
        Assert.Null((book with { IsGateComparison = true, Comparison = comparison }).Build(portfolio, Date));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyOrdinaryAndPinnedComparisonsFeedTheSameFrozenPolicyToRisk(int profile)
    {
        var book = Book() with { AssignedVersion = null, AssignedFamily = null, AssignedUtc = null,
            SettingsJson = null, SettingsHash = null };
        if (profile >= 0) book = book with { IsGateComparison = true,
            Comparison = Comparison(Book()) with { Profile = (PaperGateProfile)profile, ShadowStrategyVersionId = null } };
        var observation = book.Build(portfolio, Date)!;
        Assert.NotNull(observation);
        Assert.Equal(AiShadowPolicyIdentity.LegacyId, observation.Portfolio.StrategyVersionId);
        Assert.Equal(SystemShadowPolicyConfig.Version1, observation.Portfolio.Settings);
        using var journal = Journal();
        var result = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3))
            .Start(observation, Open.AddMinutes(12), false, []);
        Assert.True(result.Accepted);
        Assert.Equal(10000m, result.Snapshot.State!.LastValuation.Value);
        Assert.Equal(book.Comparison, observation.Portfolio.ComparisonAccount);
    }

    [Fact]
    public void WrongAssignedFamilyCannotUseLegacyFallback()
        => Assert.Null((Book() with { AssignedFamily = "Other" }).Build(portfolio, Date));

    [Fact]
    public void LegacyPinnedPolicyUsesItsMeaningRatherThanJsonFormatting()
    {
        var book = Book();
        var comparison = Comparison(book) with { ShadowStrategyVersionId = null,
            ShadowSettingsJson = "\n " + book.SettingsJson + "\n" };
        var observation = (book with { IsGateComparison = true, Comparison = comparison }).Build(portfolio, Date)!;
        using var journal = Journal();
        var monitor = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3));
        Assert.True(monitor.Start(observation, Open.AddMinutes(12), false, []).Accepted);
        var bad = observation with { Portfolio = observation.Portfolio with { ComparisonAccount = comparison with
            { ShadowSettingsJson = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default) } } };
        Assert.False(monitor.Observe(bad, Open.AddMinutes(12), false, []).Accepted);
    }

    [Fact]
    public void CashBookUsesSavedTimeAndKeepsClosedHistory()
    {
        var book = Book();
        book = book with { Positions = [book.Positions[0] with { Status = "Closed", ExitUtc = Open.AddMinutes(11), RealizedProfitLoss = -100m }],
            Prices = [], LastFillUtc = Open.AddMinutes(11), Portfolio = book.Portfolio with { CashBalance = 9900m } };
        var first = book.Build(portfolio, Date)!;
        Assert.Equal(book.UpdatedUtc, first.At);
        Assert.Equal(first.EvidenceId, book.Build(portfolio, Date)!.EvidenceId);
        Assert.Single(first.Positions);
        using var journal = Journal();
        var monitor = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3));
        Assert.True(monitor.Start(first, Open.AddMinutes(12), true, []).Accepted);
        Assert.False(monitor.Observe(book.Build(portfolio, Date), Open.AddMinutes(16), true, []).Accepted);
    }

    [Fact]
    public async Task VerifiedCashReadsAdvanceFreshnessWithoutResettingRiskOrHistory()
    {
        var book = Book();
        book = book with { Positions = [book.Positions[0] with { Status = "Closed", ExitUtc = Open.AddMinutes(11), RealizedProfitLoss = -100m }],
            Prices = [], LastFillUtc = Open.AddMinutes(11), Portfolio = book.Portfolio with { CashBalance = 9900m },
            VerifiedReadUtc = Open.AddMinutes(20), Session = book.Session with { DailyLossGuardActive = true } };
        using var journal = Journal();
        var clock = new Clock(Open.AddMinutes(20));
        var reader = new PaperAiPortfolioRiskReader(new ShadowSource((id, date, _) => Task.FromResult(book.Build(id, date))),
            new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        var first = await reader.StartAsync(Date, true, []);
        Assert.True(first.Accepted);
        Assert.Equal(9900m, first.Snapshot.State!.LastValuation.Value);
        Assert.Equal(book.VerifiedReadUtc, book.Build(portfolio, Date)!.CashVerifiedUtc);
        Assert.Contains("DailyBuyingPaused", first.Assessment.NewBuyBlocks);
        Assert.False((await reader.ObserveAsync(Date, true, [])).Advanced);
        clock.Now = Open.AddMinutes(24);
        Assert.False((await reader.ObserveAsync(Date, true, [])).Accepted); // Replaying the old receipt expires.
        book = book with { VerifiedReadUtc = clock.Now };
        var next = await reader.ObserveAsync(Date, true, []);
        Assert.True(next.Accepted);
        Assert.True(next.Advanced);
        Assert.Equal(first.Snapshot.State.RoundPeak, next.Snapshot.State!.RoundPeak);
        Assert.Contains("DailyBuyingPaused", next.Assessment.NewBuyBlocks);
        Assert.Single(book.Build(portfolio, Date)!.Positions);
        Assert.Equal(2, journal.ReadHistory().Length);
        book = book with { Portfolio = book.Portfolio with { CashBalance = 10000m } };
        Assert.False((await reader.ObserveAsync(Date, true, [])).Accepted); // A new receipt cannot excuse changed cash.
        Assert.Equal(next.Snapshot, journal.ReadCurrent());
    }

    [Fact]
    public void VerifiedReadCannotRefreshInvestedBooksOrInventMissingPrices()
    {
        var book = Book();
        var saved = book.Build(portfolio, Date)!;
        var reread = book with { VerifiedReadUtc = Open.AddMinutes(20) };
        var observation = reread.Build(portfolio, Date)!;
        Assert.Equal(saved.EvidenceId, observation.EvidenceId);
        Assert.Equal(saved.At, observation.At);
        Assert.Null(observation.CashVerifiedUtc);
        Assert.Null((reread with { Prices = [] }).Build(portfolio, Date));
        using var journal = Journal();
        var monitor = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3));
        Assert.False(monitor.Start(observation, Open.AddMinutes(20), true, []).Accepted);
        Assert.False(monitor.Start(observation with { At = Open.AddMinutes(20), ObservedUtc = Open.AddMinutes(20),
            CashVerifiedUtc = Open.AddMinutes(20) }, Open.AddMinutes(20), true, []).Accepted);
    }

    [Theory]
    [InlineData("NonUtc")]
    [InlineData("BeforeBook")]
    [InlineData("BeforeGeneration")]
    [InlineData("BeforeFill")]
    public void InvalidCashVerificationProducesNoObservation(string defect)
    {
        var book = Book() with { Positions = [], Prices = [], LastFillUtc = null, VerifiedReadUtc = Open.AddMinutes(20) };
        book = defect switch
        {
            "NonUtc" => book with { VerifiedReadUtc = DateTime.SpecifyKind(Open.AddMinutes(20), DateTimeKind.Unspecified) },
            "BeforeBook" => book with { VerifiedReadUtc = Open.AddMinutes(11) },
            "BeforeGeneration" => book with { Generation = book.Generation with { UpdatedUtc = Open.AddMinutes(21) } },
            "BeforeFill" => book with { LastFillUtc = Open.AddMinutes(21) },
            _ => throw new InvalidOperationException()
        };
        Assert.Null(book.Build(portfolio, Date));
    }

    [Fact]
    public void CashVerificationCannotRecreateYesterdayOrArriveFromTheFuture()
    {
        var book = Book();
        book = book with { Positions = [], Prices = [], LastFillUtc = null, Portfolio = book.Portfolio with { CashBalance = 10000m },
            VerifiedReadUtc = Open.AddDays(1) };
        var old = book.Build(portfolio, Date)!;
        Assert.Null(old.CashVerifiedUtc);
        Assert.Equal(book.UpdatedUtc, old.At);
        using var journal = Journal();
        var monitor = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3));
        var future = (book with { VerifiedReadUtc = Open.AddMinutes(20) }).Build(portfolio, Date)!;
        Assert.False(monitor.Start(future, Open.AddMinutes(19), true, []).Accepted);
        Assert.False(monitor.Start(future with { CashVerifiedUtc = Open.AddMinutes(19) }, Open.AddMinutes(20), true, []).Accepted);
        Assert.Empty(journal.ReadHistory());
    }

    [Fact]
    public void MixedHoldingTimesAreUnavailableEvenWhenEachHasAPrice()
    {
        var book = Book();
        book = book with { Positions = book.Positions.Add(book.Positions[0] with { PositionId = Guid.NewGuid(), Symbol = "OTHER", LastPriceEventUtc = Open }),
            Prices = book.Prices.Add(book.Prices[0] with { Symbol = "OTHER", Bar = book.Prices[0].Bar with { StartUtc = Open } }) };
        Assert.Null(book.Build(portfolio, Date));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFailuresAndCancellationCannotInitializeRisk(bool delphi)
    {
        using var journal = Journal();
        var clock = new Clock(Open.AddMinutes(12));
        var source = new FailingSources();
        var reader = delphi
            ? new PaperAiPortfolioRiskReader(source, new DelphiLiveAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock)
            : new PaperAiPortfolioRiskReader(source, new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        await Assert.ThrowsAsync<IOException>(() => reader.StartAsync(Date, true, []));
        source.Cancel = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ObserveAsync(Date, true, []));
        Assert.Null(journal.ReadCurrent().State);
        Assert.Empty(journal.ReadHistory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowReadChecksFreshnessAfterIo(bool delphi)
    {
        using var journal = Journal();
        var clock = new Clock(Open.AddMinutes(12));
        var book = Book();
        var snapshot = CashSnapshot();
        var reader = delphi
            ? new PaperAiPortfolioRiskReader(new LiveSource((_, _) => { clock.Now = Open.AddMinutes(14); return Task.FromResult<DelphiLivePortfolioSnapshot?>(snapshot); }),
                new DelphiLiveAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock)
            : new PaperAiPortfolioRiskReader(new ShadowSource((id, date, _) => { clock.Now = Open.AddMinutes(14); return Task.FromResult(book.Build(id, date)); }),
                new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        Assert.False((await reader.StartAsync(Date, true, [])).Accepted);
        Assert.Empty(journal.ReadHistory());
    }

    private DelphiLivePortfolioSnapshot CashSnapshot()
    {
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), portfolio,
            DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 10000m, "CAD",
            Date, Open, Open.AddDays(-1), "Test", "Synthetic"));
        return snapshot with { Revision = 1, UpdatedUtc = Open.AddMinutes(12), CurrentSession = Date,
            Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, Open.AddMinutes(10), true, 10000m, [], "Complete")] };
    }

    [Fact]
    public async Task DelphiReadsExactPortfolioAndPreservesHoldsAndRevision()
    {
        using var journal = Journal();
        var snapshot = CashSnapshot();
        var source = new LiveSource((id, _) => { Assert.Equal(portfolio, id); return Task.FromResult<DelphiLivePortfolioSnapshot?>(snapshot); });
        var reader = new PaperAiPortfolioRiskReader(source, new DelphiLiveAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), new Clock(Open.AddMinutes(12)));
        var first = await reader.StartAsync(Date, true, ["OperatorPause"]);
        Assert.True(first.Accepted);
        Assert.Contains("OperatorPause", first.Assessment.NewBuyBlocks);
        Assert.False((await reader.ObserveAsync(Date, true, [])).Advanced);
        Assert.False((await reader.ObserveAsync(Date.AddDays(1), true, [])).Accepted);
        Assert.Equal(first.Snapshot, journal.ReadCurrent());
    }

    [Fact]
    public void RecordOrderingDoesNotCreateNewEvidence()
    {
        var book = Book();
        book = book with { Portfolio = book.Portfolio with { CashBalance = 0m },
            Positions = book.Positions.Add(book.Positions[0] with { PositionId = Guid.NewGuid(), Symbol = "OTHER" }),
            Prices = book.Prices.Add(book.Prices[0] with { Symbol = "OTHER", Bar = book.Prices[0].Bar with { EvidenceId = Guid.NewGuid() } }) };
        var original = book.Build(portfolio, Date)!;
        var reordered = book with { Positions = [book.Positions[1], book.Positions[0]], Prices = [book.Prices[1], book.Prices[0]] };
        Assert.Equal(original.EvidenceId, reordered.Build(portfolio, Date)!.EvidenceId);
        using var journal = Journal();
        var monitor = new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3));
        Assert.True(monitor.Start(original, Open.AddMinutes(12), true, []).Accepted);
        Assert.False(monitor.Observe(reordered.Build(portfolio, Date), Open.AddMinutes(12), true, []).Advanced);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterReadPreventsJournalWrite(bool delphi)
    {
        using var journal = Journal();
        using var cancellation = new CancellationTokenSource();
        var clock = new Clock(Open.AddMinutes(12));
        var book = Book();
        var snapshot = CashSnapshot();
        var reader = delphi
            ? new PaperAiPortfolioRiskReader(new LiveSource((_, ct) => { Assert.Equal(cancellation.Token, ct); cancellation.Cancel(); return Task.FromResult<DelphiLivePortfolioSnapshot?>(snapshot); }),
                new DelphiLiveAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock)
            : new PaperAiPortfolioRiskReader(new ShadowSource((id, date, ct) => { Assert.Equal(cancellation.Token, ct); cancellation.Cancel(); return Task.FromResult(book.Build(id, date)); }),
                new SystemShadowAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)), clock);
        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.StartAsync(Date, true, [], cancellation.Token));
        Assert.Empty(journal.ReadHistory());
    }

    private sealed class Clock(DateTime at) : TimeProvider
    {
        internal DateTime Now { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class ShadowSource(Func<Guid, DateOnly, CancellationToken, Task<ShadowPaperRiskObservation?>> read) : ISystemShadowRiskReader
    {
        internal Guid RequestedPortfolio;
        internal DateOnly RequestedDate;
        public Task<ShadowPaperRiskObservation?> ReadRiskObservationAsync(Guid id, DateOnly date, CancellationToken ct = default)
        { RequestedPortfolio = id; RequestedDate = date; return read(id, date, ct); }
    }
    private sealed class LiveSource(Func<Guid, CancellationToken, Task<DelphiLivePortfolioSnapshot?>> read) : IDelphiLivePortfolioReader
    {
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid id, CancellationToken ct = default) => read(id, ct);
    }
    private sealed class FailingSources : IDelphiLivePortfolioReader, ISystemShadowRiskReader
    {
        internal bool Cancel;
        private Exception Error => Cancel ? new OperationCanceledException() : new IOException("Unavailable saved store");
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid id, CancellationToken ct = default) => throw Error;
        public Task<ShadowPaperRiskObservation?> ReadRiskObservationAsync(Guid id, DateOnly date, CancellationToken ct = default) => throw Error;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
