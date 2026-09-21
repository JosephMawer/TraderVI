#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerAccountRiskBaselineTests
{
    [Fact]
    public async Task PreviewDoesNotSaveAndMissingSchemaStillAllowsReview()
    {
        var store = new BaselineStore { State = new(false, Imported(), EmptyHistory) };
        var view = await new BrokerAccountRiskBaselineWorkflow(store, store.Clock).PreviewAsync(Guid.NewGuid());
        Assert.False(view.Installed);
        Assert.True(view.Preparation.CanEstablish);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Saved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BrokerAccountRiskBaselineWorkflow(store, store.Clock)
            .ApplyAsync(new(view.Preparation.Baseline!, "fixture")));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SameRequestRecoversALostCommitResponseWithoutRepeatingSetupOrNeedingFreshBrokerData()
    {
        var store = new BaselineStore { LoseResponse = true };
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var reviewed = (await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!;
        var request = new BrokerRiskBaselineRequest(reviewed, "fixture");
        await Assert.ThrowsAsync<TimeoutException>(() => workflow.ApplyAsync(request));
        Assert.Equal(1, store.Writes);
        store.Clock.Now = Now.AddDays(5);
        store.FailRead = true;
        var recovered = await workflow.ApplyAsync(request);
        Assert.Same(store.Saved, recovered);
        Assert.Equal(1, store.Writes);
        Assert.Equal(reviewed.BaselineId, recovered.Baseline.BaselineId);
    }

    [Theory]
    [InlineData("reviewer")]
    [InlineData("high")]
    [InlineData("protection")]
    public async Task AnExistingRequestCannotBeReusedForChangedContent(string change)
    {
        var store = new BaselineStore();
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var request = new BrokerRiskBaselineRequest((await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!, "fixture");
        await workflow.ApplyAsync(request);
        var changed = change switch
        {
            "reviewer" => request with { ReviewedBy = "different reviewer" },
            "high" => request with { Reviewed = request.Reviewed with { DrawdownReference = request.Reviewed.DrawdownReference with { Value = 1m } } },
            _ => request with { Reviewed = request.Reviewed with { CarriedMemory = request.Reviewed.CarriedMemory with { CapitalReviewRequired = true } } }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(changed));
        Assert.Equal(1, store.Writes);
    }

    [Theory]
    [InlineData("new import")]
    [InlineData("new risk report")]
    [InlineData("unknown history")]
    [InlineData("existing baseline")]
    [InlineData("stale")]
    public async Task ChangedSourceOrRiskStateRequiresAnotherReview(string change)
    {
        var store = new BaselineStore();
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var reviewed = (await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!;
        store.State = change switch
        {
            "new import" => store.State with { Import = store.State.Import with { Latest = store.State.Import.Latest! with { RequestId = Guid.NewGuid() } } },
            "new risk report" => store.State with { RiskHistory = EmptyHistory with { Report = Report() } },
            "unknown history" => store.State with { RiskHistory = EmptyHistory with { ReadComplete = false } },
            "existing baseline" => store.State with { RiskHistory = EmptyHistory with { ExistingBaseline = reviewed } },
            _ => store.State
        };
        if (change == "stale") store.Clock.Now = Now.AddMinutes(3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(new(reviewed, "fixture")));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task AChangeAfterWorkflowPreflightIsRejectedByTheStoreRecheck()
    {
        var store = new BaselineStore { ChangeBeforeSave = true };
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var reviewed = (await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(new(reviewed, "fixture")));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task PreparationTimeMayAdvanceWhileTheReviewedEvidenceBoundaryRemainsFixed()
    {
        var store = new BaselineStore();
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var reviewed = (await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!;
        store.Clock.Now = Now.AddSeconds(15);
        var saved = await workflow.ApplyAsync(new(reviewed, "fixture"));
        Assert.Equal(Now.AddSeconds(15), saved.Baseline.EstablishedAt);
        Assert.Equal(reviewed.SessionReference, saved.Baseline.SessionReference);
        Assert.Equal(reviewed.DrawdownReference, saved.Baseline.DrawdownReference);
    }

    [Fact]
    public async Task ReadFailureAndCancellationDoNotCreateAnEmptyReplacement()
    {
        var store = new BaselineStore();
        var workflow = new BrokerAccountRiskBaselineWorkflow(store, store.Clock);
        var reviewed = (await workflow.PreviewAsync(Guid.NewGuid())).Preparation.Baseline!;
        store.FailRead = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(new(reviewed, "fixture")));
        store.FailRead = false;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.ApplyAsync(new(reviewed, "fixture"), cancellation.Token));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void StoredBaselineRecoversFullPrecisionAndRequestIdentity()
    {
        var baseline = Prepare().Baseline! with { EstablishedAt = Now.AddTicks(12345) };
        var row = Stored(baseline);
        var recovered = BrokerAccountRiskBaselineRepository.Decode(row)!;
        Assert.Equal(baseline.EstablishedAt, recovered.Baseline.EstablishedAt);
        Assert.Equal(row.RequestHash, recovered.RequestHash);
        Assert.Equal(baseline.DrawdownReference, recovered.Baseline.DrawdownReference);
        Assert.Null(BrokerAccountRiskBaselineRepository.Decode(null));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("binding")]
    [InlineData("account")]
    [InlineData("import")]
    [InlineData("report")]
    [InlineData("batch")]
    [InlineData("date")]
    [InlineData("timestamp")]
    [InlineData("reviewer")]
    [InlineData("request hash")]
    [InlineData("content hash")]
    [InlineData("json")]
    public void StoredBaselineRejectsChangedIdentityContentOrTimestamp(string change)
    {
        var baseline = Prepare().Baseline!;
        var row = Stored(baseline);
        row = change switch
        {
            "id" => row with { BaselineId = Guid.NewGuid() },
            "binding" => row with { BindingId = Guid.NewGuid() },
            "account" => row with { AccountLabel = "RRSP" },
            "import" => row with { ImportRequestId = Guid.NewGuid() },
            "report" => row with { AccountReportId = Guid.NewGuid() },
            "batch" => row with { RecommendationBatchId = Guid.NewGuid() },
            "date" => row with { TradingDate = row.TradingDate.AddDays(1) },
            "timestamp" => row with { EstablishedUtc = row.EstablishedUtc.AddTicks(1) },
            "reviewer" => row with { ReviewedBy = "changed" },
            "request hash" => row with { RequestHash = new string('B', 64) },
            "content hash" => row with { ContentHash = new string('B', 64) },
            _ => row with { BaselineJson = JsonSerializer.Serialize(baseline with { CarriedMemory = baseline.CarriedMemory with { CapitalReviewRequired = true } }) }
        };
        Assert.Throws<InvalidOperationException>(() => BrokerAccountRiskBaselineRepository.Decode(row));
    }

    [Fact]
    public void RequestIdentityIsStableAcrossSymbolMapOrderAndJsonRecovery()
    {
        var baseline = Prepare().Baseline!;
        var symbol = new TradingSymbolState(Guid.NewGuid(), null, null, null, null, null, null);
        var symbols = ImmutableDictionary<string, TradingSymbolState>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
            .Add("ZED", symbol).Add("ABC", symbol with { PositionId = Guid.NewGuid() });
        baseline = baseline with { CarriedMemory = baseline.CarriedMemory with { Symbols = symbols } };
        var reordered = baseline with { CarriedMemory = baseline.CarriedMemory with
            { Symbols = ImmutableDictionary<string, TradingSymbolState>.Empty.Add("ABC", symbols["ABC"]).Add("ZED", symbols["ZED"]) } };
        Assert.Equal(new BrokerRiskBaselineRequest(baseline, "fixture").RequestHash,
            new BrokerRiskBaselineRequest(reordered, "fixture").RequestHash);
        var recovered = BrokerAccountRiskBaselineRepository.Decode(Stored(baseline))!;
        Assert.Equal(symbols.Count, recovered.Baseline.CarriedMemory.Symbols.Count);
    }

    private static BrokerAccountRiskBaselineRepository.Row Stored(BrokerAccountRiskBaseline baseline)
    {
        string json = JsonSerializer.Serialize(baseline);
        var request = new BrokerRiskBaselineRequest(baseline, "fixture");
        return new(baseline.BaselineId, baseline.BindingId, baseline.AccountLabel, baseline.ImportRequestId,
            baseline.AccountReportId, baseline.RecommendationBatchId, baseline.TradingDate.ToDateTime(TimeOnly.MinValue),
            baseline.EstablishedAt.UtcDateTime, request.ReviewedBy, request.RequestHash, BrokerAccountImport.Hash(json), json);
    }

    private sealed class BaselineClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BrokerAccountRiskBaselineTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BaselineStore : IBrokerRiskBaselineStore
    {
        public BaselineClock Clock { get; } = new();
        public BrokerRiskBaselineState State { get; set; } = new(true, Imported(), EmptyHistory);
        public BrokerRiskBaselineReceipt? Saved { get; private set; }
        public bool LoseResponse { get; set; }
        public bool FailRead { get; set; }
        public bool ChangeBeforeSave { get; set; }
        public int Writes { get; private set; }
        public Task<BrokerRiskBaselineState> ReadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailRead) throw new InvalidOperationException("Synthetic unavailable risk-history read.");
            return Task.FromResult(State);
        }
        public Task<BrokerRiskBaselineReceipt?> FindAsync(Guid baselineId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Saved?.Baseline.BaselineId == baselineId ? Saved : null);
        }
        public Task<BrokerRiskBaselineReceipt> SaveAsync(BrokerRiskBaselineRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ChangeBeforeSave) State = State with { RiskHistory = EmptyHistory with { Report = Report() } };
            var baseline = BrokerAccountRiskBaselineWorkflow.Recheck(request, State, Clock.Now);
            Saved = new(baseline, request.ReviewedBy, request.RequestHash);
            State = State with { RiskHistory = State.RiskHistory with { ExistingBaseline = baseline } };
            Writes++;
            if (LoseResponse) throw new TimeoutException("Synthetic lost commit response.");
            return Task.FromResult(Saved);
        }
    }
}
