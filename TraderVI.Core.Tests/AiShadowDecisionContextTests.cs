#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiShadowDecisionContextTests
{
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTime At = ReviewedTsxSessionCalendar.At(Date, new(10, 0));
    private static readonly DateTimeOffset Now = new(At.AddMinutes(1));
    private static readonly TimeSpan Age = TimeSpan.FromMinutes(5);
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("test", "synthetic", Date.AddDays(-1), Date,
        [Date.AddDays(-1), Date]));
    private static AiPortfolioCatalogEntry Choice() => new(Guid.NewGuid(), "Continuation 3", "DailyShadow",
        TradingStrategyResolutionTests.Strategy(), null, []);
    private static AiShadowSavedContext Saved(AiPortfolioCatalogEntry choice)
    {
        var strategy = choice.Strategy!;
        Guid session = Guid.NewGuid(), candidate = Guid.NewGuid();
        return new(choice.PaperAccountId, session, Date, strategy.PolicyVersionId, strategy.ShadowPolicy!, "Continuation", 3,
            choice.Comparison, Guid.NewGuid(), strategy.Daily.StrategyVersionId, strategy.Daily.Configuration, strategy.Daily.Models,
            Date.AddDays(-1), At.AddHours(-2), At.AddMinutes(-30), At.AddDays(-1), "OfficialPaper", "Valid",
            [new(candidate, "TEST", 1, "Buy", true, 1.25, null, [new("Breadth", true, "Saved pass")],
                new(Guid.NewGuid(), session, candidate, "TEST", 1, 10m, "Qualified", "Qualified", At))],
            [new(Date.AddDays(-1), 1000m, 1020m, At.AddDays(-1), true, "Saved close"),
                new(Date, 1020m, null, null, false, "No saved closing value")]);
    }
    private static AiStrategyDecisionContext Build(AiPortfolioCatalogEntry choice, AiShadowSavedContext? saved,
        DateTimeOffset? now = null) => AiShadowDecisionContext.Build(choice, saved, Calendar, Date, Age, now ?? Now);

    [Fact]
    public void SavedFactsRetainTimesGatesAndDailyReturnsWithoutManagedFunds()
    {
        var choice = Choice(); var saved = Saved(choice); var result = Build(choice, saved);
        Assert.True(result.CanPropose);
        Assert.Equal(choice.Version, result.StrategyVersion);
        Assert.Equal(At, result.ObservedUtc);
        Assert.Equal(At.Add(Age), result.ExpiresUtc);
        using var performance = JsonDocument.Parse(result.Evidence.Single(e => e.Id == "shadow-performance").Description);
        var days = performance.RootElement.GetProperty("Days");
        Assert.Equal(.02m, days[0].GetProperty("Return").GetDecimal());
        Assert.Equal(JsonValueKind.Null, days[1].GetProperty("Return").ValueKind);
        Assert.DoesNotContain("AvailableCash", JsonSerializer.Serialize(result));
        Assert.False(result.Rules.Single(r => r.Id.StartsWith("daily:")).IsHardBoundary);
        Assert.True(result.Rules.Single(r => r.Id == "stock-protection").IsHardBoundary);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Build(choice, saved, Now.AddSeconds(10))));
    }

    [Theory]
    [InlineData("BelowPreviousSessionClose", true)]
    [InlineData("FallingFromPreviousFiveMinuteClose", true)]
    [InlineData("MissingEvidence", false)]
    [InlineData("LateEvidence", false)]
    [InlineData("ConflictingEvidence", false)]
    [InlineData("UnknownFutureReason", false)]
    public void DiscretionaryNegativeGuidanceIsDistinctFromUnusableData(string reason, bool usable)
    {
        var choice = Choice(); var saved = Saved(choice); var candidate = saved.Candidates[0];
        saved = saved with { Candidates = [candidate with { Decision = candidate.Decision! with { State = "Blocked", ReasonCode = reason } }] };
        Assert.Equal(usable, Build(choice, saved).CanPropose);
    }

    [Theory]
    [InlineData(PaperGateProfile.NormalV1, false)]
    [InlineData(PaperGateProfile.PortfolioRulesOnlyV1, true)]
    public void FixedGateVariantPreservesOriginalFailureAndAdmission(PaperGateProfile profile, bool admitted)
    {
        var choice = Choice(); var strategy = choice.Strategy!;
        choice = choice with { Comparison = new(choice.PaperAccountId, Guid.NewGuid(), Guid.NewGuid(), "ContinuationTop3", profile,
            At.AddDays(-1), strategy.Daily.StrategyVersionId, null, strategy.PolicyVersionId, JsonSerializer.Serialize(strategy.ShadowPolicy)) };
        var saved = Saved(choice); var candidate = saved.Candidates[0];
        saved = saved with { Candidates = [candidate with { Direction = "Hold", Eligible = false, FirstFailedGate = "Breadth",
            Gates = [new("Breadth", false, "Below threshold")], Decision = admitted ? candidate.Decision : null }] };
        var result = Build(choice, saved);
        using var signal = JsonDocument.Parse(result.Evidence.Single(e => e.Id == "shadow-signals").Description);
        Assert.Equal(admitted, signal.RootElement[0].GetProperty("AdmittedByPaperProfile").GetBoolean());
        Assert.False(signal.RootElement[0].GetProperty("Gates")[0].GetProperty("Passed").GetBoolean());
        Assert.Equal(admitted, result.CanPropose);
    }

    [Theory]
    [InlineData("account")][InlineData("session")][InlineData("date")][InlineData("policy")]
    [InlineData("settings")][InlineData("model")][InlineData("daily")][InlineData("lens")]
    [InlineData("prior-date")][InlineData("purpose")][InlineData("audit")][InlineData("future-source")]
    [InlineData("future-decision")][InlineData("old-decision")][InlineData("duplicate")]
    [InlineData("decision-identity")][InlineData("future-history")][InlineData("unknown-state")]
    public void MismatchedOrAmbiguousEvidenceCannotBecomeAContext(string change)
    {
        var choice = Choice(); var saved = Saved(choice); var candidate = saved.Candidates[0];
        saved = change switch
        {
            "account" => saved with { PortfolioId = Guid.NewGuid() },
            "session" => saved with { SessionId = Guid.NewGuid() },
            "date" => saved with { TradingDate = Date.AddDays(-1) },
            "policy" => saved with { PolicyId = Guid.NewGuid() },
            "settings" => saved with { Policy = saved.Policy with { HardLossFraction = .06m } },
            "model" => saved with { Models = saved.Models.SetItem(0, saved.Models[0] with { ArtifactSha256 = new string('B', 64) }) },
            "daily" => saved with { DailyStrategyId = Guid.NewGuid() },
            "lens" => saved with { Lens = "Breakout" },
            "prior-date" => saved with { MarketDataAsOf = Date.AddDays(-2) },
            "purpose" => saved with { RunPurpose = "ExploratoryReplay" },
            "audit" => saved with { AuditState = "Degraded" },
            "future-source" => saved with { EffectiveUtc = At.AddMinutes(2) },
            "future-decision" => saved with { Candidates = [candidate with { Decision = candidate.Decision! with { LastEvaluatedUtc = At.AddMinutes(2) } }] },
            "old-decision" => saved with { Candidates = [candidate with { Decision = candidate.Decision! with { LastEvaluatedUtc = At.AddHours(-1) } }] },
            "duplicate" => saved with { Candidates = [candidate, candidate] },
            "decision-identity" => saved with { Candidates = [candidate with { Decision = candidate.Decision! with { CalibrationCandidateId = Guid.NewGuid() } }] },
            "future-history" => saved with { History = [new(Date, 100m, 110m, At.AddDays(1), true, "Bad")] },
            _ => saved with { Candidates = [candidate with { Decision = candidate.Decision! with { State = "FutureState" } }] }
        };
        Assert.Throws<InvalidDataException>(() => Build(choice, saved));
    }

    [Fact]
    public void MissingStaleOrPartiallyEvaluatedCandidatesStayUnavailable()
    {
        var choice = Choice(); var saved = Saved(choice); var candidate = saved.Candidates[0];
        Assert.False(Build(choice, null).CanPropose);
        Assert.False(Build(choice, saved, At.Add(Age)).CanPropose);
        Assert.False(Build(choice, saved with { Candidates = [] }).CanPropose);
        Assert.False(Build(choice, saved with { Candidates = [candidate with { Decision = null }] }).CanPropose);
        var second = candidate with { CandidateId = Guid.NewGuid(), Symbol = "OTHER", Rank = 2, Decision = null };
        Assert.False(Build(choice, saved with { Candidates = [candidate, second] }).CanPropose);
        Assert.False(Build(choice, saved, ReviewedTsxSessionCalendar.At(Date, new(16, 0))).CanPropose);
    }

    [Fact]
    public void UnknownGateIsHardAndConflictingPerformanceIsUnavailableNotZero()
    {
        var choice = Choice(); var saved = Saved(choice); var candidate = saved.Candidates[0];
        var result = Build(choice, saved with { Candidates = [candidate with { Gates = [new("FutureDataRule", false, "Unknown")] }],
            History = [saved.History[0], saved.History[0]] });
        Assert.True(result.Rules.Single(r => r.Id.StartsWith("daily:")).IsHardBoundary);
        Assert.False(result.CanPropose);
        using var doc = JsonDocument.Parse(result.Evidence.Single(e => e.Id == "shadow-performance").Description);
        var day = doc.RootElement.GetProperty("Days")[0];
        Assert.Equal(JsonValueKind.Null, day.GetProperty("Return").ValueKind);
        Assert.Equal("Conflicting daily observations", day.GetProperty("Evidence").GetString());
    }

    [Fact]
    public void ReadsPublishedCamelCaseTraceAndRejectsUnknownContract()
    {
        const string json = """{"schemaVersion":1,"gates":[{"gateName":"Breadth","passed":false,"reason":"Below threshold"}]}""";
        var trace = AiShadowDecisionRepository.ReadGateTrace(1, json);
        Assert.Equal("Breadth", trace.Single().GateName);
        Assert.False(trace.Single().Passed);
        Assert.Throws<InvalidDataException>(() => AiShadowDecisionRepository.ReadGateTrace(2, json));
        Assert.Throws<InvalidDataException>(() => AiShadowDecisionRepository.ReadGateTrace(1, "{}"));
    }

    [Fact]
    public void OldestDecisionBoundsExpiryAndLaterPerformanceCannotRenewIt()
    {
        var choice = Choice(); var saved = Saved(choice); var first = saved.Candidates[0];
        Guid otherId = Guid.NewGuid();
        var second = first with { CandidateId = otherId, Symbol = "OTHER", Rank = 2,
            Decision = first.Decision! with { CandidateTrackingId = Guid.NewGuid(), CalibrationCandidateId = otherId,
                Symbol = "OTHER", Rank = 2, LastEvaluatedUtc = At.AddMinutes(-2) } };
        var result = Build(choice, saved with { Candidates = [first, second],
            History = [new(Date, 100m, 101m, At.AddSeconds(30), false, "Partial day")] });
        Assert.Equal(At.AddMinutes(3), result.ExpiresUtc);
        Assert.Equal(At.AddSeconds(30), result.ObservedUtc);
    }

    [Fact]
    public async Task ReaderChecksAgeAfterIoAndPropagatesFailuresAndCancellation()
    {
        var choice = Choice(); var saved = Saved(choice); var clock = new Clock(Now);
        var source = new Source(() => { clock.Now = At.AddMinutes(10); return saved; });
        Assert.False((await new AiShadowDecisionContextReader(source, Calendar, Age, clock).ReadAsync(choice, Date)).CanPropose);
        await Assert.ThrowsAsync<IOException>(() => new AiShadowDecisionContextReader(new Source(() => throw new IOException("Unavailable")), Calendar, Age)
            .ReadAsync(choice, Date));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AiShadowDecisionContextReader(new Source(() => { cancellation.Cancel(); return saved; }), Calendar, Age)
            .ReadAsync(choice, Date, cancellation.Token));
    }

    [Fact]
    public void SavedContextFeedsReviewerWithoutCopyingPaperFundsOrPermittingStopOverrides()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ai-shadow-context-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var choice = Choice(); Guid managed = Guid.NewGuid(); var policy = new AiPortfolioRiskPolicy(Calendar);
            using var journal = new AiPortfolioRiskJournal(directory, new(managed, managed, AiRiskAccountMode.Paper), policy, true);
            var book = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), managed, DelphiLivePolicyDefinition.Version1.PolicyVersionId,
                "OperationalChampion", null, 5000m, "CAD", Date, At.AddMinutes(-30), At.AddDays(-1), "Test", "Synthetic"));
            book = book with { Revision = 1, CurrentSession = Date, UpdatedUtc = At,
                Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, At, true, 5000m, [], "Complete")] };
            var observation = new DelphiLiveAiPortfolioRiskMonitor(journal, policy, Age).Start(book, Now, false, []);
            var context = Build(choice, Saved(choice));
            var packet = AiPortfolioPacketBuilder.Build(new(Now, Now.AddMinutes(10), [choice]), journal, observation, null, [context], Now);
            Assert.True(packet.Candidates.Single().Eligible);
            Assert.Contains("5000", packet.Evidence.Single(e => e.Id == "managed-account").Description);
            var proposal = new AiPortfolioProposal(packet.PacketId, "select", choice.Id, choice.Version, "Saved facts",
                [choice.Id + ":shadow-signals"], [new("stock-protection", "Loosen")], "Later", []);
            var json = JsonSerializer.Serialize(proposal, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(json, packet));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Source(Func<AiShadowSavedContext?> read) : IAiShadowDecisionSource
    {
        public Task<AiShadowSavedContext?> ReadAsync(Guid id, DateOnly date, CancellationToken ct = default) => Task.FromResult(read());
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
