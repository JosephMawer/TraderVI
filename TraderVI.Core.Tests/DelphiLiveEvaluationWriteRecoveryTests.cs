#nullable enable
using Core.Db;
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveEvaluationWriteRecoveryTests
{
    [Theory]
    [InlineData(-2)][InlineData(1205)][InlineData(1222)]
    public void OnlyTransientDatabaseFailuresAreEligible(int code)
    {
        Assert.True(DelphiLiveEvaluationWriteRecovery.CanRetry([code]));
        Assert.True(DelphiLiveEvaluationWriteRecovery.CanRetry([code,3621]));
        Assert.False(DelphiLiveEvaluationWriteRecovery.CanRetry([code,51247]));
    }

    [Theory]
    [InlineData(51241)][InlineData(51242)][InlineData(51243)][InlineData(51244)][InlineData(51245)]
    [InlineData(51246)][InlineData(51247)][InlineData(2627)][InlineData(229)][InlineData(207)]
    public void FinancialIdentityLeaseEvidenceAndPermissionFailuresAreNotRetried(int code) =>
        Assert.False(DelphiLiveEvaluationWriteRecovery.CanRetry([code]));

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task TimeoutBeforeOrAfterCommitReplaysSameCheckpointWithoutDuplicateOrTimestampRefresh(bool committed)
    {
        Guid id = Guid.NewGuid(); var observed = new DateTime(2026,10,21,13,35,0,DateTimeKind.Utc);
        const string input = "immutable original evidence", result = "immutable original result";
        var durable = new Dictionary<Guid,(DateTime,string,string)>(); int attempts = 0, disposals = 0, reports = 0;
        var error = new IOException("synthetic SQL timeout");
        await DelphiLiveEvaluationWriteRecovery.RunAsync(async (number, token) =>
        {
            Assert.Equal(number-1, disposals); // prior failed connection/transaction must already be gone
            try
            {
                attempts++;
                if (number == 1 && !committed) throw error;
                if (durable.TryGetValue(id,out var prior)) Assert.Equal((observed,input,result),prior);
                else durable.Add(id,(observed,input,result));
                if (number == 1) throw error; // lost acknowledgement after durable save
                await Task.CompletedTask;
            }
            finally { disposals++; }
        }, _ => [-2], (_, e, retry) => { Assert.Same(error,e); Assert.True(retry); reports++; return Task.CompletedTask; },
            (duration, _) => { Assert.Equal(TimeSpan.FromMilliseconds(250),duration); return Task.CompletedTask; }, default);
        Assert.Equal(2,attempts); Assert.Equal(2,disposals); Assert.Equal(1,reports);
        Assert.Single(durable); Assert.Equal((observed,input,result),durable[id]);
    }

    [Fact]
    public async Task PersistentTimeoutFailsAfterOneReplayAndPreservesTheOriginalException()
    {
        var error = new IOException("synthetic"); int attempts=0, reports=0;
        var actual = await Assert.ThrowsAsync<IOException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_, _) => { attempts++; throw error; }, _ => [-2], (number, _, retry) =>
            { reports++; Assert.Equal(number == 1,retry); return Task.CompletedTask; }, (_, _) => Task.CompletedTask, default));
        Assert.Same(error,actual); Assert.Equal(2,attempts); Assert.Equal(2,reports);
    }

    [Fact]
    public async Task ValidationOrCancellationCannotReplayAnEvaluation()
    {
        int calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_, _) => { calls++; throw new InvalidOperationException("lease/evidence changed"); }, _ => [51242],
            (_, _, _) => Task.CompletedTask, (_, _) => throw new Exception("unexpected delay"), default));
        Assert.Equal(1,calls);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_, _) => { calls++; return Task.CompletedTask; }, _ => [-2], (_, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.Equal(1,calls);
    }

    [Fact]
    public async Task CancellationDuringBackoffAndFailedDiagnosticCannotCreateAnExtraAttempt()
    {
        int calls=0; using var cancellation=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_, _) => { calls++; throw new IOException("synthetic"); }, _ => [-2],
            (_, _, _) => throw new IOException("diagnostic unavailable"),
            (_, token) => { cancellation.Cancel(); return Task.FromCanceled(token); }, cancellation.Token));
        Assert.Equal(1,calls);
    }

    [Fact]
    public void SaveDiagnosticContainsNoExceptionMessageOrPayload()
    {
        string json=DelphiLiveFailureDiagnostics.DescribeEvaluationSave(1,DelphiLiveEvaluationSaveStage.Evidence,TimeSpan.FromSeconds(25),TimeSpan.FromSeconds(30),
            new InvalidOperationException("secret account and provider payload"),true);
        Assert.DoesNotContain("secret",json); Assert.DoesNotContain("payload",json);
        Assert.Contains("Evidence",json); Assert.Contains("WillRetrySameEvaluation",json);
        Assert.Contains("StageElapsedMs",json); Assert.Contains("AttemptElapsedMs",json);
    }

    [Theory]
    [InlineData("EvaluationSave:Lease")][InlineData("EvaluationSave:Evidence")][InlineData("EvaluationSave:Commit")]
    public void OnlyFixedSqlStageMarkersBecomeDiagnostics(string stage) =>
        Assert.NotNull(DelphiLiveFailureDiagnostics.ReadEvaluationStage(stage));

    [Theory]
    [InlineData("credential=secret")][InlineData("EvaluationSave:Commit account=private")][InlineData("Evidence")]
    public void OtherServerMessagesAreNeverRecordedAsStageNames(string message) =>
        Assert.Null(DelphiLiveFailureDiagnostics.ReadEvaluationStage(message));

    [Theory]
    [InlineData(0)][InlineData(233)][InlineData(4060)][InlineData(52232)][InlineData(51241)]
    public async Task OtherFailuresPropagateWithoutReplay(int number)
    {
        int calls = 0; var expected = new IOException("synthetic");
        var actual = await Assert.ThrowsAsync<IOException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_,_) => { calls++; throw expected; }, _ => [number], (_,_,retry) => { Assert.False(retry); return Task.CompletedTask; },
            (_,_) => throw new Exception("unexpected backoff"),default));
        Assert.Same(expected,actual); Assert.Equal(1,calls);
    }

    [Fact]
    public async Task SecondAttemptRechecksActiveLeaseRatherThanTreatingFirstFailureAsAuthority()
    {
        int calls = 0; var timeout = new IOException("synthetic timeout"); var lostLease = new InvalidOperationException("synthetic lost lease");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => DelphiLiveEvaluationWriteRecovery.RunAsync(
            (_,_) => { calls++; if (calls == 1) throw timeout; throw lostLease; }, e => ReferenceEquals(e,timeout) ? [-2] : [51242],
            (_,_,_) => Task.CompletedTask, (_,_) => Task.CompletedTask,default));
        Assert.Same(lostLease,actual); Assert.Equal(2,calls);
    }
}
