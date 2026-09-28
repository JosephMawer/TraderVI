#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiLiveDecisionContextTests
{
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private static readonly DateTime End = Open.AddMinutes(30);
    private static readonly DateTimeOffset Now = new(End.AddMinutes(3));
    private static readonly TimeSpan Age = TimeSpan.FromMinutes(10);
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("test", "synthetic", Date.AddDays(-1), Date, [Date.AddDays(-1), Date]));
    private static AiPortfolioCatalogEntry Choice()
    {
        var policy = DelphiLivePolicyDefinition.Version1;
        var strategy = TradingStrategyResolutionTests.Strategy() with { Family = TradingStrategyFamily.DelphiLive,
            PolicyVersionId = policy.PolicyVersionId, ShadowPolicy = null, LivePolicy = policy,
            Selection = CompleteTradingStrategy.LiveSelection, MaximumHoldings = policy.MaximumHoldings };
        return new(Guid.NewGuid(), "Live", "DelphiLive", strategy, null, []);
    }
    private static AiLiveSavedContext Saved(AiPortfolioCatalogEntry choice)
    {
        Guid session = Guid.NewGuid(), run = Guid.NewGuid(), candidate = Guid.NewGuid();
        DelphiLiveTrueRangeRulerMeasurement Ruler(int count) => new(count, Date.AddDays(-1), DelphiLiveScalarMeasurement.Available(.04m));
        DelphiLiveFiveMinuteSeries Series(string symbol) => new(symbol, Date, Open, Open,
            Enumerable.Range(0, 6).Select(i => new DelphiLiveFiveMinuteBar(Guid.NewGuid(), symbol, Date,
                Open.AddMinutes(5 * i), Open.AddMinutes(5 * (i + 1)), 100 + i, 102 + i, 99 + i, 101 + i, 100,
                Open.AddMinutes(5 * (i + 1) + 2), "TMX", 1, DelphiLiveEvidenceDisposition.OperationalOnTime)));
        var input = new DelphiLiveEvaluationInput { EvaluationId = Guid.NewGuid(), SessionId = session,
            BarEndUtc = End, EvaluatedUtc = End.AddMinutes(2), Stock = Series("TEST"), Xiu = Series("XIU"),
            VolatilityRulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)), PreviousState = DelphiLiveEvaluationState.Initial(true),
            Policy = choice.Strategy!.ReadLivePolicy(), PreviousStockSessionClose = 100m, PreviousXiuSessionClose = 100m,
            MedianFullDayVolume20 = 10000m, ExactPairPersistedOnTime = true,
            DailySetup = new(run, candidate, choice.Strategy.Daily.StrategyVersionId, .5m,
                [new(DelphiLiveSourceLens.Continuation, true, true, 1, .5m, "Saved reasons", "Saved gates")]) };
        var result = DelphiLiveEvaluationEngine.Evaluate(input);
        var accountState = new DelphiLivePortfolioCandidateState(new(DelphiLiveRecommendationState.Dismissed, 0, 2,
            End, "Account-specific dismissal"), 1, input.EvaluationId, input.EvaluatedUtc.AddSeconds(1));
        return new(choice.PaperAccountId, Guid.NewGuid(), 4, Date, input.EvaluatedUtc.AddSeconds(2), session,
            input.Policy, choice.Comparison, null, null, run, choice.Strategy.Daily.StrategyVersionId,
            choice.Strategy.Daily.Configuration, choice.Strategy.Daily.Models, Date.AddDays(-1), Open.AddHours(-1), Open,
            1, Open, true, [new("TEST", candidate, accountState, [new(input, result, 1)], false)],
            [new(Date.AddDays(-1), 1000m, 1020m, Open.AddDays(-1), true, "Saved close"), new(Date, 1020m, null, null, false, "Incomplete")]);
    }
    private static AiStrategyDecisionContext Build(AiPortfolioCatalogEntry choice, AiLiveSavedContext? saved, DateTimeOffset? now = null) =>
        AiLiveDecisionContext.Build(choice, saved, Calendar, Date, Age, now ?? Now);

    [Fact]
    public void PreservesAccountSpecificConfirmationAndOriginalTimesWithoutPaperFunds()
    {
        var choice = Choice(); var saved = Saved(choice); var context = Build(choice, saved);
        Assert.True(context.CanPropose);
        Assert.Equal(choice.Version, context.StrategyVersion);
        Assert.Equal(End.Add(Age), context.ExpiresUtc);
        Assert.Equal(JsonSerializer.Serialize(context), JsonSerializer.Serialize(Build(choice, saved, Now.AddSeconds(10))));
        using var signal = JsonDocument.Parse(context.Evidence.Single(e => e.Id == "live-signal:TEST").Description);
        Assert.Equal("Account-specific dismissal", signal.RootElement.GetProperty("PaperAccountConfirmation").GetProperty("Lifecycle").GetProperty("ReasonCode").GetString());
        Assert.DoesNotContain("AvailableCash", JsonSerializer.Serialize(context));
        Assert.DoesNotContain("SafetyInput", JsonSerializer.Serialize(context));
        Assert.True(context.Rules.Single(r => r.Id == "stock-protection").IsHardBoundary);
        using var performance = JsonDocument.Parse(context.Evidence.Single(e => e.Id == "live-performance").Description);
        Assert.Equal(.02m, performance.RootElement.GetProperty("Days")[0].GetProperty("Return").GetDecimal());
        Assert.Equal(JsonValueKind.Null, performance.RootElement.GetProperty("Days")[1].GetProperty("Return").ValueKind);
    }

    [Theory]
    [InlineData("restart")][InlineData("epoch-ended")][InlineData("conflict")][InlineData("source-change")]
    [InlineData("missing-state")][InlineData("missing-checkpoint")][InlineData("latest-invalid")]
    [InlineData("confidence")][InlineData("old-daily")][InlineData("carry-rank")][InlineData("reset-state")]
    public void UnsafeOrMismatchedLatestContextCannotEnableChoice(string change)
    {
        var choice = Choice(); var saved = Saved(choice); var signal = saved.Signals[0]; var latest = signal.Checkpoints[0];
        saved = change switch
        {
            "restart" => saved with { CurrentEpoch = 2, EpochStartedUtc = End.AddMinutes(1) },
            "epoch-ended" => saved with { EpochActive = false },
            "conflict" => saved with { Signals = [signal with { HasSourceConflict = true }] },
            "source-change" => saved with { SourceRevisionId = Guid.NewGuid() },
            "missing-state" => saved with { Signals = [signal with { AccountState = null }] },
            "missing-checkpoint" => saved with { Signals = [signal with { Checkpoints = [] }] },
            "latest-invalid" => saved with { Signals = [signal with { Checkpoints = [latest with { Result = latest.Result with { ObservationIsValid = false, ConfirmedLiveEligible = false } }] }] },
            "confidence" => saved with { Signals = [signal with { Checkpoints = [latest with { Result = latest.Result with {
                NextState = latest.Result.NextState with { Confidence = new(DelphiLiveDataConfidenceState.Degraded, 2) } } }] }] },
            "old-daily" => saved with { RunId = Guid.NewGuid() },
            "carry-rank" => saved with { Signals = [signal with { DailyCandidateId = null }] },
            _ => saved with { Signals = [signal with { AccountState = signal.AccountState! with { Lifecycle = DelphiLiveLifecycleSnapshot.NewSession(true) } }] }
        };
        Assert.False(Build(choice, saved).CanPropose);
    }

    [Theory]
    [InlineData("account")][InlineData("policy")][InlineData("model")][InlineData("prior-date")]
    [InlineData("future")][InlineData("duplicate-symbol")][InlineData("duplicate-checkpoint")][InlineData("evaluation-id")]
    public void InvalidEnvelopesAreRejected(string change)
    {
        var choice = Choice(); var saved = Saved(choice); var signal = saved.Signals[0]; var latest = signal.Checkpoints[0];
        saved = change switch
        {
            "account" => saved with { PortfolioId = Guid.NewGuid() },
            "policy" => saved with { Policy = saved.Policy with { HardLossFraction = .06m } },
            "model" => saved with { Models = saved.Models.SetItem(0, saved.Models[0] with { ArtifactSha256 = new string('B', 64) }) },
            "prior-date" => saved with { MarketDataAsOf = Date.AddDays(-2) },
            "future" => saved with { UpdatedUtc = End.AddDays(1) },
            "duplicate-symbol" => saved with { Signals = [signal, signal] },
            "duplicate-checkpoint" => saved with { Signals = [signal with { Checkpoints = [latest, latest] }] },
            _ => saved with { Signals = [signal with { Checkpoints = [latest with { Result = latest.Result with { EvaluationId = Guid.NewGuid() } }] }] }
        };
        Assert.Throws<InvalidDataException>(() => Build(choice, saved));
    }

    [Theory]
    [InlineData(PaperGateProfile.NormalV1)]
    [InlineData(PaperGateProfile.PortfolioRulesOnlyV1)]
    public void ComparisonRequiresExactAccountProfileAndSourceAdmission(PaperGateProfile profile)
    {
        var choice = Choice(); var strategy = choice.Strategy!;
        choice = choice with { Comparison = new(choice.PaperAccountId, Guid.NewGuid(), Guid.NewGuid(), "DelphiLive", profile,
            Open, strategy.Daily.StrategyVersionId, strategy.PolicyVersionId, null, null) };
        var saved = Saved(choice); var signal = saved.Signals[0]; var latest = signal.Checkpoints[0]; Guid revision = Guid.NewGuid();
        var comparison = choice.Comparison!;
        var admission = new PaperGateAdmission(choice.PaperAccountId, comparison.PairId, comparison.BatchId, profile,
            revision, saved.RunId, saved.DailyStrategyId, saved.Policy.PolicyVersionId, Open);
        saved = saved with { SourceRevisionId = revision, AccountSourceRevisionId = revision, Signals = [signal with {
            Checkpoints = [latest with { Input = latest.Input with { DailySetup = latest.Input.DailySetup! with { ComparisonAdmission = admission } } }] }] };
        Assert.True(Build(choice, saved).CanPropose);
        signal = saved.Signals[0]; latest = signal.Checkpoints[0];
        Assert.False(Build(choice, saved with { Signals = [signal with { Checkpoints = [latest with {
            Input = latest.Input with { DailySetup = latest.Input.DailySetup! with { ComparisonAdmission = admission with { AccountId = Guid.NewGuid() } } } }] }] }).CanPropose);
    }

    [Fact]
    public void MissingStaleOrLateAccountSaveNeverRenewsMarketEvidence()
    {
        var choice = Choice(); var saved = Saved(choice);
        Assert.False(Build(choice, null).CanPropose);
        Assert.False(Build(choice, saved, End.Add(Age)).CanPropose);
        Assert.False(Build(choice, saved with { UpdatedUtc = End.AddMinutes(20) }, End.AddMinutes(21)).CanPropose);
        Assert.False(Build(choice, saved with { Signals = [] }).CanPropose);
        Assert.False(Build(choice, saved, ReviewedTsxSessionCalendar.At(Date, new(16, 0))).CanPropose);
    }

    [Fact]
    public void SupersededUnheldCandidateStateDoesNotExpandCurrentAccountScope()
    {
        var saved = Saved(Choice());
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), saved.Policy.PolicyVersionId,
            "OperationalChampion", null, 1000m, "CAD", Date, Open, Open.AddDays(-1), "Test", "Synthetic"));
        snapshot = snapshot with { CandidateStates = ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty
            .Add("OLD", saved.Signals[0].AccountState!) };
        Assert.Equal(["NEW"], AiLiveDecisionRepository.RequiredSymbols(snapshot, ["NEW"], Date));
    }

    [Fact]
    public void StoredCheckpointReaderVerifiesColumnsAgainstJson()
    {
        var latest = Saved(Choice()).Signals[0].Checkpoints[0]; var input = latest.Input;
        string json = DelphiLiveLedgerJson.Serialize(input), result = DelphiLiveLedgerJson.Serialize(latest.Result);
        var parsed = AiLiveDecisionRepository.ReadCheckpoint(input.EvaluationId, input.SessionId, input.Policy.PolicyVersionId,
            "TEST", input.BarEndUtc, input.EvaluatedUtc, 1, json, result);
        Assert.Equal(input.EvaluationId, parsed.Input.EvaluationId);
        Assert.Throws<InvalidDataException>(() => AiLiveDecisionRepository.ReadCheckpoint(Guid.NewGuid(), input.SessionId,
            input.Policy.PolicyVersionId, "TEST", input.BarEndUtc, input.EvaluatedUtc, 1, json, result));
    }

    [Fact]
    public void LatestInvalidCheckpointIsRetainedAlongsideEarlierValidHistory()
    {
        var choice = Choice(); var saved = Saved(choice); var signal = saved.Signals[0]; var prior = signal.Checkpoints[0];
        Guid id = Guid.NewGuid();
        var next = prior with { Input = prior.Input with { EvaluationId = id, BarEndUtc = End.AddMinutes(5), EvaluatedUtc = End.AddMinutes(7) },
            Result = prior.Result with { EvaluationId = id, ObservationIsValid = false, ConfirmedLiveEligible = false } };
        var context = Build(choice, saved with { UpdatedUtc = End.AddMinutes(7), Signals = [signal with {
            Checkpoints = [prior, next], AccountState = signal.AccountState! with { EvaluationId = id, EvaluatedUtc = End.AddMinutes(7),
                Lifecycle = signal.AccountState.Lifecycle with { LastScheduledBarEndUtc = End.AddMinutes(5) } } }] }, End.AddMinutes(8));
        Assert.False(context.CanPropose);
        using var json = JsonDocument.Parse(context.Evidence.Single(e => e.Id == "live-signal:TEST").Description);
        var checkpoints = json.RootElement.GetProperty("Checkpoints");
        Assert.Equal(2, checkpoints.GetArrayLength());
        Assert.True(checkpoints[0].GetProperty("ObservationIsValid").GetBoolean());
        Assert.False(checkpoints[1].GetProperty("ObservationIsValid").GetBoolean());
    }

    [Fact]
    public void IncompleteLatestMarkDoesNotFallBackToAnEarlierCompleteGain()
    {
        var choice = Choice(); var saved = Saved(choice);
        ImmutableArray<DelphiLiveLedgerMark> marks = [
            new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Opening, Open, true, 1000m, [], "Opening"),
            new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, End.AddMinutes(-5), true, 1010m, [], "Complete"),
            new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, End, false, null, [], "MissingPrice")];
        var context = Build(choice, saved with { History = PortfolioDailyPerformance.FromLiveMarks(marks).ToImmutableArray() });
        using var json = JsonDocument.Parse(context.Evidence.Single(e => e.Id == "live-performance").Description);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Days")[0].GetProperty("Return").ValueKind);
        Assert.Contains("MissingPrice", json.RootElement.GetProperty("Days")[0].GetProperty("Evidence").GetString());
    }

    [Fact]
    public void LiveContextBuildsAccountScopedPacketAndRejectsProtectiveOverride()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ai-live-context-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var choice = Choice(); Guid managed = Guid.NewGuid(); var policy = new AiPortfolioRiskPolicy(Calendar);
            using var journal = new AiPortfolioRiskJournal(directory, new(managed, managed, AiRiskAccountMode.Paper), policy, true);
            var book = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), managed, DelphiLivePolicyDefinition.Version1.PolicyVersionId,
                "OperationalChampion", null, 5432m, "CAD", Date, Open, Open.AddDays(-1), "Test", "Synthetic"));
            book = book with { Revision = 1, CurrentSession = Date, UpdatedUtc = End,
                Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, End, true, 5432m, [], "Complete")] };
            var observation = new DelphiLiveAiPortfolioRiskMonitor(journal, policy, Age).Start(book, Now, false, []);
            var packet = AiPortfolioPacketBuilder.Build(new(Now, Now.AddMinutes(20), [choice]), journal, observation, null,
                [Build(choice, Saved(choice))], Now);
            Assert.True(packet.Candidates.Single().Eligible);
            Assert.Equal(End.Add(Age), packet.ExpiresUtc);
            Assert.Contains("5432", packet.Evidence.Single(e => e.Id == "managed-account").Description);
            var proposal = new AiPortfolioProposal(packet.PacketId, "select", choice.Id, choice.Version, "Saved evidence",
                [choice.Id + ":live-signal:TEST"], [], "Recheck", []);
            string Serialize(AiPortfolioProposal p) => JsonSerializer.Serialize(p, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Assert.Equal("select", AiPortfolioReviewer.ParseAndValidate(Serialize(proposal), packet).Action);
            Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Serialize(proposal with {
                RuleOverrides = [new("stock-protection", "Loosen")] }), packet));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReaderUsesPostReadClockAndPropagatesCancellationAndFailures()
    {
        var choice = Choice(); var saved = Saved(choice); var clock = new Clock(Now);
        var reader = new AiLiveDecisionContextReader(new Source(() => { clock.Now = End.AddMinutes(11); return saved; }), Calendar, Age, clock);
        Assert.False((await reader.ReadAsync(choice, Date)).CanPropose);
        await Assert.ThrowsAsync<IOException>(() => new AiLiveDecisionContextReader(new Source(() => throw new IOException()), Calendar, Age).ReadAsync(choice, Date));
        using var ct = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AiLiveDecisionContextReader(new Source(() => { ct.Cancel(); return saved; }), Calendar, Age).ReadAsync(choice, Date, ct.Token));
    }
    private sealed class Source(Func<AiLiveSavedContext?> read) : IAiLiveDecisionSource
    {
        public Task<AiLiveSavedContext?> ReadAsync(Guid id, DateOnly date, CancellationToken ct = default) => Task.FromResult(read());
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
