#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Core.Trader;
using Core.Oracle;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class PaperAiFastValuationReaderTests : IDisposable
{
    private readonly string journalDirectory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private AiPortfolioRiskPolicy Policy => new(calendar);
    private AiPortfolioRiskJournal Journal(bool initialize = true) => new(journalDirectory, new(portfolio, portfolio, AiRiskAccountMode.Paper), Policy, initialize);
    private PaperAiFastRiskMonitor Monitor(AiPortfolioRiskJournal journal) => new(journal, Policy, calendar,
        TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectedRiskReaderSavesPeakAndClosureAndRecoversWithoutReset(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        int minute = 9;
        decimal price = 100m;
        var prices = Prices(_ => [new(Open.AddMinutes(minute), price, price, price, price, 100)]);
        var source = live ? Live(() => d, prices) : Shadow(() => s, prices);
        AiRiskJournalSnapshot saved;
        using (var journal = Journal())
        {
            var reader = new PaperAiPortfolioRiskReader(source, Monitor(journal), clock);
            var first = await reader.StartAsync(Date, true, []);
            Assert.True(first.Accepted);
            Assert.Equal(10000m, first.Snapshot.State!.RoundOpeningCapital);
            Assert.Contains("DailyBuyingPaused", first.Assessment.NewBuyBlocks);
            minute++;
            price = 120m;
            clock.Now = clock.Now.AddMinutes(1);
            Assert.True((await reader.ObserveAsync(Date, false, [])).Accepted);
            minute++;
            price = 100m;
            clock.Now = clock.Now.AddMinutes(1);
            var hit = await reader.ObserveAsync(Date, false, []);
            Assert.True(hit.Accepted);
            Assert.True(hit.Assessment.RequiresManagedLiquidation);
            Assert.Equal(11000m, hit.Snapshot.State!.RoundPeak);
            Assert.Equal(first.Snapshot.State.RoundId, hit.Snapshot.State.RoundId);
            Assert.Equal(3, journal.ReadHistory().Length);
            Assert.False((await reader.StartAsync(Date, true, [])).Accepted);
            saved = journal.ReadCurrent();
        }
        using var recovered = Journal(false);
        Assert.Equal(saved, recovered.ReadCurrent());
        var missing = Monitor(recovered).Observe(null, clock.Now, true, []);
        Assert.False(missing.Accepted);
        Assert.True(missing.Assessment.RequiresManagedLiquidation);
        Assert.Equal(saved, missing.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewReceiptForSameBarDoesNotRenewJournalEvidenceOrExpiry(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        var source = live ? Live(() => d) : Shadow(() => s);
        using var journal = Journal();
        var monitor = Monitor(journal);
        var first = monitor.Start((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.True(first.Accepted);
        clock.Now = clock.Now.AddSeconds(10);
        var repeat = monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.True(repeat.Accepted);
        Assert.False(repeat.Advanced);
        Assert.Equal(first.Snapshot, repeat.Snapshot);
        clock.Now = first.Snapshot.State!.LastValuation.ValidUntil.UtcDateTime;
        var expired = monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.False(expired.Accepted);
        Assert.Equal(0m, expired.Assessment.NewBuyAllowanceMultiplier);
        Assert.Single(journal.ReadHistory());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConflictingPriceLatchesDurableBlockAndPreservesRequiredClosure(bool live, bool closing)
    {
        var s = ShadowBook();
        var d = LiveBook();
        int minute = 9;
        decimal price = 100m;
        var prices = Prices(_ => [new(Open.AddMinutes(minute), price, price, price, price, 100)]);
        var source = live ? Live(() => d, prices) : Shadow(() => s, prices);
        AiPortfolioRiskState state;
        using (var journal = Journal())
        {
            var monitor = Monitor(journal);
            Assert.True(monitor.Start((await source.ReadAsync(Date)).Valuation, clock.Now, true, []).Accepted);
            if (closing)
            {
                minute++;
                price = 80m;
                clock.Now = clock.Now.AddMinutes(1);
                Assert.True(monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []).Accepted);
            }
            state = journal.ReadCurrent().State!;
            price += 1m;
            clock.Now = clock.Now.AddSeconds(1);
            var conflict = monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
            Assert.False(conflict.Accepted);
            Assert.Equal(state, conflict.Snapshot.State);
            Assert.Equal(closing, conflict.Assessment.RequiresManagedLiquidation);
            Assert.Contains(AiPortfolioRiskJournal.SourceConflictBlock, conflict.Assessment.NewBuyBlocks);
        }
        using var recovered = Journal(false);
        Assert.Equal(state, recovered.ReadCurrent().State);
        Assert.Equal(AiPortfolioRiskJournal.SourceConflictBlock, recovered.ReadCurrent().SourceBlock);
        minute++;
        clock.Now = clock.Now.AddMinutes(1);
        var next = Monitor(recovered).Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.False(next.Accepted);
        Assert.Equal(0m, next.Assessment.NewBuyAllowanceMultiplier);
        Assert.Contains(AiPortfolioRiskJournal.SourceConflictBlock, next.Assessment.NewBuyBlocks);
        var ordinary = new SystemShadowAiPortfolioRiskMonitor(recovered, Policy, TimeSpan.FromMinutes(3)).Observe(null, clock.Now, true, []);
        Assert.Contains(AiPortfolioRiskJournal.SourceConflictBlock, ordinary.Assessment.NewBuyBlocks);
        if (closing)
        {
            var closure = new AiRoundClosure(portfolio, state.RoundId, Guid.NewGuid(), clock.Now, 0, 0, state.LastValuation.Value, true);
            recovered.Apply(new(Guid.NewGuid(), recovered.ReadCurrent().Revision, AiRiskTransitionKind.ConfirmClosure, Closure: closure), clock.Now);
            Assert.Equal(AiRiskRoundPhase.AwaitingEntry, recovered.ReadCurrent().State!.Phase);
            Assert.Equal(AiPortfolioRiskJournal.SourceConflictBlock, recovered.ReadCurrent().SourceBlock);
        }
    }

    [Theory]
    [InlineData(false, "capital")]
    [InlineData(true, "capital")]
    [InlineData(false, "history")]
    [InlineData(true, "history")]
    [InlineData(false, "strategy")]
    [InlineData(true, "strategy")]
    public async Task ConsistentNewBookCannotReplacePriorCapitalHistoryOrStrategy(bool live, string defect)
    {
        var s = ShadowBook();
        var d = LiveBook();
        int minute = 9;
        var prices = Prices(_ => [new(Open.AddMinutes(minute), 100, 100, 100, 100, 100)]);
        var source = live ? Live(() => d, prices) : Shadow(() => s, prices);
        using var journal = Journal();
        var monitor = Monitor(journal);
        var initial = monitor.Start((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.True(initial.Accepted);
        s = defect switch
        {
            "capital" => s with { Generation = s.Generation with { TotalAccountValue = 11000m }, Portfolio = s.Portfolio with { CashBalance = 6000m } },
            "history" => s with { Positions = [], LastFillUtc = null, Portfolio = s.Portfolio with { CashBalance = 10000m } },
            _ => s with { AssignedVersion = Guid.NewGuid() }
        };
        d = defect switch
        {
            "capital" => d with { StartingCapital = 11000m, Cash = 6000m, Revision = 2 },
            "history" => d with { Positions = [], Fills = [], Cash = 10000m, Revision = 2 },
            _ => d with { PolicyVersionId = Guid.NewGuid(), Revision = 2 }
        };
        minute++;
        clock.Now = clock.Now.AddMinutes(1);
        var candidate = (await source.ReadAsync(Date)).Valuation;
        Assert.NotNull(candidate); // Valid in isolation; unacceptable continuation of the saved account.
        Assert.False(monitor.Observe(candidate, clock.Now, true, []).Accepted);
        Assert.Equal(initial.Snapshot, journal.ReadCurrent());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("hash")]
    [InlineData("expiry")]
    [InlineData("holds")]
    [InlineData("price")]
    public async Task RevalidatesPreparedEvidenceAtTheJournalBoundary(string defect)
    {
        var s = ShadowBook();
        var value = (await Shadow(() => s).ReadAsync(Date)).Valuation!;
        value = defect switch
        {
            "amount" => value with { Cash = 6000m },
            "hash" => value with { EvidenceSha256 = "wrong" },
            "expiry" => Rewrite(value, j => j["ValidUntilUtc"] = clock.Now.AddHours(1)),
            "holds" => Rewrite(value, j => j["SavedBlocks"] = new JsonArray()),
            _ => Rewrite(value, j => j["Prices"]!["Results"]![0]!["Evidence"]!["Bar"]!["Close"] = 120m)
        };
        using var journal = Journal();
        Assert.False(Monitor(journal).Start(value, clock.Now, true, []).Accepted);
        Assert.Empty(journal.ReadHistory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSavedPriceHandoverRetainsRoundPeakAndIdentity(bool live)
    {
        var s = ShadowBook();
        s = s with { Positions = [s.Positions[0] with { LastPriceEventUtc = Open, LastPrice = 100m }],
            Prices = [new("TEST", new(Guid.NewGuid(), Open, Open.AddMinutes(6), 5, 100, 100, 100, 100))], UpdatedUtc = Open.AddMinutes(6) };
        var d = LiveBook();
        d = d with { UpdatedUtc = Open.AddMinutes(6), Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint,
            Open.AddMinutes(5), true, 10000m, [new(d.Positions[0].PositionId, "TEST", 50, 100m, Open.AddMinutes(5))], "Complete")] };
        using var journal = Journal();
        var first = live ? new DelphiLiveAiPortfolioRiskMonitor(journal, Policy, TimeSpan.FromMinutes(3)).Start(d, Open.AddMinutes(6), true, []) :
            new SystemShadowAiPortfolioRiskMonitor(journal, Policy, TimeSpan.FromMinutes(3)).Start(s.Build(portfolio, Date), Open.AddMinutes(6), true, []);
        Assert.True(first.Accepted);
        var source = live ? Live(() => d) : Shadow(() => s);
        var value = (await source.ReadAsync(Date)).Valuation!;
        var monitor = Monitor(journal);
        Assert.False(monitor.Observe(value, clock.Now, true, []).Accepted);
        Assert.Equal(first.Snapshot, journal.ReadCurrent());
        var handover = monitor.Handover(value, clock.Now, true, []);
        Assert.True(handover.Accepted);
        Assert.Equal(first.Snapshot.State!.RoundId, handover.Snapshot.State!.RoundId);
        Assert.Equal(first.Snapshot.State.OriginalCapital, handover.Snapshot.State.OriginalCapital);
        Assert.Equal(10500m, handover.Snapshot.State.RoundPeak);
        Assert.Equal(AiPaperAccountValuation.Contract, handover.Snapshot.State.LastValuation.Source!.Contract);
        Assert.Equal(2, journal.ReadHistory().Length);
    }

    private static AiPaperAccountValuation Rewrite(AiPaperAccountValuation v, Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(v.EvidenceJson)!.AsObject();
        edit(node);
        string json = node.ToJsonString();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return v with { EvidenceJson = json, EvidenceSha256 = Convert.ToHexString(hash), EvidenceId = new Guid(hash.AsSpan(0, 16)),
            ValidUntilUtc = node["ValidUntilUtc"]!.GetValue<DateTime>(),
            SavedBlocks = JsonSerializer.Deserialize<ImmutableArray<string>>(node["SavedBlocks"]!.ToJsonString()) };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconciledSaleToCashAdvancesSameRoundAndKeepsItsHistory(bool live)
    {
        var s = ShadowBook();
        var d = LiveBook();
        var source = live ? Live(() => d) : Shadow(() => s);
        using var journal = Journal();
        var monitor = Monitor(journal);
        var initial = monitor.Start((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        var exit = clock.Now.AddSeconds(5);
        s = s with { Portfolio = s.Portfolio with { CashBalance = 10500m }, LastFillUtc = exit, UpdatedUtc = exit,
            Positions = [s.Positions[0] with { Status = "Closed", ExitUtc = exit, RealizedProfitLoss = 500m }] };
        var sell = Guid.NewGuid();
        d = d with { Revision = 2, Cash = 10500m, UpdatedUtc = exit,
            Positions = [d.Positions[0] with { ClosedUtc = exit, ExitActionId = sell }],
            Fills = d.Fills.Add(new(Guid.NewGuid(), sell, d.Positions[0].PositionId, Guid.NewGuid(), "TEST", DelphiLiveActionSide.Sell,
                50, 110m, DelphiLiveQuoteField.Bid, DelphiLiveFillConfidence.SideSpecific, exit, Date)) };
        clock.Now = clock.Now.AddMinutes(1);
        var cash = monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []);
        Assert.True(cash.Accepted);
        Assert.Equal(initial.Snapshot.State!.RoundId, cash.Snapshot.State!.RoundId);
        Assert.Equal(initial.Snapshot.State.RoundPeak, cash.Snapshot.State.RoundPeak);
        Assert.Equal(10500m, cash.Snapshot.State.LastValuation.Value);
        Assert.Equal(2, journal.ReadHistory().Length);
    }

    [Fact]
    public async Task CombinedRiskValueFeedsAiPacketButConflictCannotReuseAcceptedAssessment()
    {
        var s = ShadowBook();
        decimal price = 110m;
        var source = Shadow(() => s, Prices(_ => [new(Open.AddMinutes(9), price, price, price, price, 100)]));
        using var journal = Journal();
        var monitor = Monitor(journal);
        var initial = monitor.Start((await source.ReadAsync(Date)).Valuation, clock.Now, false, []);
        var choice = new AiPortfolioCatalogEntry(Guid.NewGuid(), "Synthetic choice", "DailyShadow", TradingStrategyResolutionTests.Strategy(), null, []);
        var catalog = new AiPortfolioCatalog(clock.Now, clock.Now.AddMinutes(1), [choice]);
        var packet = AiPortfolioPacketBuilder.Build(catalog, journal, initial, null, [], clock.Now);
        using var evidence = JsonDocument.Parse(packet.Evidence.Single(e => e.Id == "managed-account").Description);
        Assert.Equal(10500m, evidence.RootElement.GetProperty("Value").GetDecimal());
        Assert.Equal(AiPaperAccountValuation.Contract, evidence.RootElement.GetProperty("Contract").GetString());
        price = 111m;
        Assert.False(monitor.Observe((await source.ReadAsync(Date)).Valuation, clock.Now, true, []).Accepted);
        Assert.Throws<InvalidDataException>(() => AiPortfolioPacketBuilder.Build(catalog, journal, initial, null, [], clock.Now));
        Assert.Throws<InvalidDataException>(() => AiPortfolioPacketBuilder.Build(catalog, journal,
            initial with { Snapshot = journal.ReadCurrent() }, null, [], clock.Now));
    }

    public void Dispose()
    {
        if (Directory.Exists(journalDirectory)) Directory.Delete(journalDirectory, true);
    }
}
