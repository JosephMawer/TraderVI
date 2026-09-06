#nullable enable

using Core.Calibration;
using Core.Db;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCollectionRepositoryTests
{
    [Theory]
    [InlineData("", "Clean")]
    [InlineData("short", "Clean")]
    [InlineData("abcdef0123456789", "Unrecorded")]
    public void Constructor_RejectsMissingProvenanceWithoutOpeningAConnection(string commit, string tree) =>
        Should.Throw<ArgumentException>(() => new DelphiLiveCollectionRepository(new(commit, "Test", tree)));

    [Fact]
    public async Task LeaseOperations_RejectForeignIdentityBeforeDatabaseAccess()
    {
        var repository = new DelphiLiveCollectionRepository(new("abcdef0123456789", "Test", "Dirty"));
        DateTime now = Utc(14, 0);
        var foreign = new DelphiLiveLease(Guid.NewGuid(), "other-host", 1, now, now.AddMinutes(10));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            repository.TryRenewAsync(foreign, now, now.AddMinutes(20)));
        await Should.ThrowAsync<InvalidOperationException>(() => repository.ReleaseAsync(foreign, now));
        await Should.ThrowAsync<InvalidOperationException>(() => repository.RecoverSessionAsync(Guid.NewGuid(), foreign));
    }

    [Fact]
    public void ExactReceipt_RejectsAnEndpointReceiptAndLossySqlDecimalRounding()
    {
        var request = Request();
        var valid = new OhlcvBar(request.BarStartUtc, 10m, 10.1m, 9.9m, 10m, 0);
        var receipt = new DelphiLiveMarketDataReceipt(request, valid, Utc(13, 37), "OperationalOnTime");
        DelphiLiveCollectionRepository.ClassifyReceipt(receipt).ShouldBe("OperationalOnTime");
        DelphiLiveCollectionRepository.ClassifyReceipt(receipt with
        {
            Request = request with { RequestStartedUtc = request.BarEndUtc },
            ReceivedUtc = request.BarEndUtc
        }).ShouldBe("FormingBarIgnored");
        DelphiLiveCollectionRepository.ClassifyReceipt(receipt with
        {
            ExactCompletedBar = valid with { Open = 10.0000001m }
        }).ShouldBe("StructurallyInvalid");
    }

    [Fact]
    public void Deadline_IsExclusiveAndEmptySuccessCannotInventAnEvidenceLink()
    {
        DelphiLiveMarketDataRequest request = Request();
        var bar = new OhlcvBar(request.BarStartUtc, 10m, 11m, 9m, 10.5m, 100);
        var receipt = new DelphiLiveMarketDataReceipt(request, bar, request.DeadlineUtc, "OperationalOnTime");
        DelphiLiveCollectionRepository.ClassifyReceipt(receipt).ShouldBe("LateResearchOnly");
        DelphiLiveCollectionRepository.ClassifyReceipt(receipt with { ExactCompletedBar = null })
            .ShouldBe("NoCompletedBar");
        Should.Throw<ArgumentException>(() => DelphiLiveCollectionRepository.ClassifyReceipt(
            receipt with { ExactCompletedBar = null, Disposition = "SuccessWithNoEvidence" }));
    }

    [Fact]
    public void CompleteExpectedTargets_RequireXiuAndRejectDuplicates()
    {
        var xiu = new DelphiLiveObservationTarget("XIU", DelphiLiveCollectionPriorityClass.XiuBenchmark, 0, false, false);
        var held = new DelphiLiveObservationTarget("ABC", DelphiLiveCollectionPriorityClass.HeldSymbol, 0, false, false);
        Should.Throw<ArgumentException>(() => DelphiLiveCollectionRepository.ValidateTargets([held]));
        Should.Throw<ArgumentException>(() => DelphiLiveCollectionRepository.ValidateTargets([xiu, held, held with { Symbol = "abc" }]));
        DelphiLiveCollectionRepository.ValidateTargets([xiu, held]).Select(x => x.Symbol).ShouldBe(["ABC", "XIU"]);
    }

    [Fact]
    public void LeaseAndCycleSql_EnforceServerClockFencingAndAtomicCompleteExpectedSets()
    {
        string sql = ReadSource("DelphiLiveCollectionRepository.LifecycleSql.cs");
        string repository = ReadSource("DelphiLiveCollectionRepository.cs");
        repository.ShouldContain("IsolationLevel.Serializable");
        repository.ShouldContain("@LockOwner = N'Transaction'");
        sql.ShouldContain("SYSUTCDATETIME()");
        sql.ShouldContain("FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>@Now");
        sql.ShouldContain("ExpiresUtc>=@Deadline");
        sql.ShouldContain("EXCEPT SELECT Symbol FROM @Expected");
        sql.ShouldContain("INSERT dbo.IntradayCollectionCycle");
        sql.ShouldContain("INSERT dbo.IntradayCollectionSlot");
        sql.ShouldContain("Another Delphi Live collection cycle is still running.");
        sql.ShouldContain("MAX(FencingToken),0)+1");
        sql.ShouldNotContain("DELETE ", Case.Insensitive);
    }

    [Fact]
    public void ReceiptSql_ResolvesBothCanonicalKeysAndRetainsLateConflictAuditWithoutOverwrites()
    {
        string sql = ReadSource("DelphiLiveCollectionRepository.ReceiptSql.cs");
        sql.ShouldContain("PollCycleId=@CycleId AND Symbol=@Symbol AND IntervalMinutes=5");
        sql.ShouldContain("b.Symbol=@Symbol AND b.IntervalMinutes=5 AND b.EventUtc=@Start");
        sql.ShouldContain("@SuppliedPoll IS NOT NULL AND (@Poll IS NULL OR @Poll<>@SuppliedPoll)");
        sql.ShouldContain("@SuppliedBar IS NOT NULL AND (@Evidence IS NULL OR @Evidence<>@SuppliedBar)");
        sql.ShouldContain("@Received>=@Deadline OR @Now>=@Deadline OR @LeaseValid=0");
        sql.ShouldContain("INSERT dbo.IntradayEvidenceConflict");
        sql.ShouldContain("INSERT dbo.IntradayCollectionReceipt");
        sql.ShouldContain("ReceiptSha256=@ReceiptHash");
        sql.ShouldNotContain("UPDATE dbo.IntradayEvidenceBar", Case.Insensitive);
        sql.ShouldNotContain("UPDATE dbo.IntradayPollObservation", Case.Insensitive);
        sql.ShouldContain("@CurrentDisposition NOT IN (N'OperationalOnTime',N'IdenticalDuplicate')");
        sql.ShouldContain("N'AwaitingDurabilityVerification'");
        sql.ShouldContain("@Now<@Deadline AND @LeaseValid=1 AND @CycleState=N'Collecting'");
        sql.ShouldContain("N'DurableOnTime'");
        ReadSource("DelphiLiveCollectionRepository.cs").ShouldContain("WriteAsync(VerifyDurabilitySql");
    }

    [Fact]
    public void RecoverySql_PreservesElapsedMembershipAndFlagsHostGapsWithoutPortfolioAuthority()
    {
        string sql = ReadSource("DelphiLiveCollectionRepository.LifecycleSql.cs");
        sql.ShouldContain("RequiredFromBarEndUtc<=@End AND RequiredThroughBarEndUtc>=@End");
        sql.ShouldContain("DATEADD(MINUTE,2,@End)<@Now");
        sql.ShouldContain("N'HostCoverageGap'");
        sql.ShouldContain("N'HostRestart'");
        sql.ShouldContain("@Previous IS NOT NULL OR @WasArmedAtSessionOpen=0");
        sql.ShouldNotContain("@Now>DATEADD(MINUTE,7,@Open)");
        sql.ShouldContain("\"ordinaryConfirmation\":\"Reset\"");
        sql.ShouldContain("\"portfolioRecovery\":\"RequiredByHost\"");
        sql.ShouldContain("CompletedUtc>=SessionCloseUtc");
        sql.ShouldContain("HostGapObserved,CAST(0 AS INT),@Now");
        sql.ShouldContain("DATEDIFF(MINUTE,RequiredFromBarEndUtc,RequiredThroughBarEndUtc)/5+1");
        sql.ShouldNotContain("dbo.ActivePosition", Case.Insensitive);
        sql.ShouldNotContain("dbo.ShadowOrder", Case.Insensitive);
    }

    [Fact]
    public void CompatibleV3Facts_PreserveLegacyOutcomeIntervalAndPolicyBoundaries()
    {
        string sql = ReadSource("IntradayEvidenceRepository.cs");
        sql.ShouldContain("o.[PolicyVersion] = @PolicyVersion");
        sql.ShouldContain("o.[PolicyVersion] IS NULL");
        sql.ShouldContain("o.[CollectorVersion] = N'IntradayEvidenceCollectorV3'");
        sql.ShouldContain("b.[IntervalMinutes] = 5");
        sql.ShouldContain("o.[SourceContractVersion] = @SourceContractVersion");
        sql.ShouldContain("o.[Purpose] = N'PaperMonitor'");
        sql.ShouldContain("o.[AuditState] IN (N'Valid', N'Degraded')");
        sql.ShouldContain("b.[IntervalMinutes] = @IntervalMinutes");
        sql.ShouldContain("b.[EventUtc] >= @FromUtc");
        sql.ShouldContain("b.[EventUtc] <= @ThroughUtc");
    }

    [Fact]
    public void ReplayQueries_NeverUpgradeLateFactsAndNeverIncludeConflictedSlots()
    {
        string sql = ReadSource("DelphiLiveCollectionRepository.Queries.cs");
        sql.ShouldContain("s.OperationallyUsable=1 AND r.OperationallyUsable=1");
        sql.ShouldContain("s.ReceivedUtc<s.DeadlineUtc AND s.SettledUtc<s.DeadlineUtc");
        sql.ShouldContain("AND NOT EXISTS (SELECT 1 FROM dbo.IntradayEvidenceConflict");
        sql.ShouldContain("DelphiLiveEvidenceDisposition.LateResearchOnly");
        string receipt = ReadSource("DelphiLiveCollectionRepository.ReceiptSql.cs");
        receipt.ShouldContain("IF @InsertBar=1 AND @ExistingPoll=1");
        receipt.ShouldContain("N'LateReceiptAfterPrimaryMiss'");
        receipt.ShouldContain("VALUES (@Evidence,@CanonicalPoll,@Symbol,5,@Start");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    public void ProviderBatchMapping_PreservesActualTransportCapacityAndFetchTimes(int attempts, int requests)
    {
        var request = Request();
        var bar = new OhlcvBar(request.BarStartUtc,10m,11m,9m,10.5m,100);
        var batch = new TmxIntradayBatch(request.Symbol,5,request.BarStartUtc,request.BarEndUtc,
            request.RequestStartedUtc.AddMilliseconds(1),request.RequestStartedUtc.AddSeconds(2),attempts,requests,[bar]);
        var receipt = TmxDelphiLiveMarketDataSource.FromBatch(request,batch);
        receipt.ProviderAttemptCount.ShouldBe(attempts);
        receipt.ProviderRequestCount.ShouldBe(requests);
        receipt.ProviderFetchStartedUtc.ShouldBe(batch.FetchStartedUtc);
        receipt.ReceivedUtc.ShouldBe(batch.ReceivedUtc);
        var normalized = DelphiLiveCollectionWorkflow.NormalizeReceipt(request,receipt,request.DeadlineUtc);
        normalized.ProviderAttemptCount.ShouldBe(attempts);
        normalized.ProviderRequestCount.ShouldBe(requests);
        string sql = ReadSource("DelphiLiveCollectionRepository.ReceiptSql.cs");
        sql.ShouldContain("COALESCE(@ProviderAttempts,0),COALESCE(@ProviderRequests,0)");
        sql.ShouldContain("@ReceiptHash,@ProviderAttempts,@ProviderRequests,@ProviderFetch");
    }

    [Fact]
    public void BatchDuplicateNormalization_IsIdempotentWhileConflictsRemainInvalidWithMetadata()
    {
        var request = Request();
        var bar = new OhlcvBar(request.BarStartUtc,10m,11m,9m,10.5m,100);
        var batch = new TmxIntradayBatch(request.Symbol,5,request.BarStartUtc,request.BarEndUtc,
            request.RequestStartedUtc,request.RequestStartedUtc.AddSeconds(2),3,1,[bar,bar]);
        TmxDelphiLiveMarketDataSource.FromBatch(request,batch).ExactCompletedBar.ShouldBe(bar);
        var conflicted = TmxDelphiLiveMarketDataSource.FromBatch(request,batch with { Bars = [bar,bar with { Close=10.6m }] });
        conflicted.Disposition.ShouldBe("StructurallyInvalid");
        conflicted.ExactCompletedBar.ShouldBeNull();
        conflicted.ProviderAttemptCount.ShouldBe(3);
        conflicted.ProviderRequestCount.ShouldBe(1);
        var failed = new DelphiLiveMarketDataReceipt(request,null,request.RequestStartedUtc.AddSeconds(2),"CollectionFailed");
        failed.ProviderAttemptCount.ShouldBeNull();
        failed.ProviderRequestCount.ShouldBeNull();
    }

    private static DelphiLiveMarketDataRequest Request() =>
        new(Guid.NewGuid(), "ABC", Utc(13, 30), Utc(13, 35), Utc(13, 42), Utc(13, 37), 1);

    private static DateTime Utc(int hour, int minute) => new(2026, 9, 4, hour, minute, 0, DateTimeKind.Utc);

    private static string ReadSource(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TraderVI.sln")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("TraderVI repository root was not found.");
        return File.ReadAllText(Path.Combine(directory.FullName, "Core", "Db", name));
    }
}
