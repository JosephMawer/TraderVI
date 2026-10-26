using Core.Runtime;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineRecoveryMaintenanceTests
{
    [Fact]
    public async Task RecoveryHostRunsOnlyContinuityAndExplicitRealAuditAndDrainsNormally()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false;
        var engine = new IndependentEngine(EngineRecoveryMaintenance.Work(
            () => { continuity.TrySetResult(); return Task.CompletedTask; },
            async () => { entered.TrySetResult(); await release.Task; }),
            () => Task.CompletedTask, () => { closed = true; return Task.CompletedTask; });
        try
        {
            await engine.StartAsync();
            await Task.WhenAll(entered.Task, continuity.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { "Engine continuity", "Real recovery audit" }, engine.ReadStatus().Work.Select(w => w.Name));
            var stop = engine.StopAsync();
            Assert.False(stop.IsCompleted);
            Assert.False(closed);
            release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(closed);
        }
        finally { release.TrySetResult(); await engine.StopAsync(); }
    }

    [Fact]
    public void MaintenanceRejectsBrokerActionsPaperCommandsAndUnscopedRecovery()
    {
        EngineOperation[] allowed = [EngineOperation.Status, EngineOperation.Stop, EngineOperation.IntegrityStatus,
            EngineOperation.Connection, EngineOperation.ExecutionStatus, EngineOperation.VerifyRealRecovery, EngineOperation.ClearIntegrityHolds];
        foreach (var operation in Enum.GetValues<EngineOperation>())
        {
            if (allowed.Contains(operation)) EngineRecoveryMaintenance.RequireOperation(operation);
            else Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireOperation(operation));
        }
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireOperation((EngineOperation)int.MaxValue));
        EngineRecoveryMaintenance.RequireClearScope(EngineRecoveryScope.Real);
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireClearScope(EngineRecoveryScope.All));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireClearScope(EngineRecoveryScope.Paper));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireClearScope((EngineRecoveryScope)int.MaxValue));
    }
}
