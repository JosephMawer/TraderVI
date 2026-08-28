#nullable enable

using Core.Trader;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TrackedExecutionModeTests
{
    [Fact]
    public void GhostPosition_CanAutoExitWhenEnabledAndPolicyAlerts()
    {
        PaperTradingMonitor.ShouldExecuteAutomaticExit(
            TrackedExecutionMode.Ghost,
            automaticGhostExitsEnabled: true,
            IntradaySwingDirective.ExitAlert).ShouldBeTrue();
    }

    [Fact]
    public void RealPosition_CannotAutoExitEvenWhenEnabledAndPolicyAlerts()
    {
        PaperTradingMonitor.ShouldExecuteAutomaticExit(
            TrackedExecutionMode.Real,
            automaticGhostExitsEnabled: true,
            IntradaySwingDirective.ExitAlert).ShouldBeFalse();
    }

    [Fact]
    public void GhostPosition_RejectsBrokerAccountLabel()
    {
        Should.Throw<ArgumentException>(() =>
            TrackedExecutionModeContract.NormalizeAccountLabel(
                TrackedExecutionMode.Ghost,
                "TFSA"));
    }

    [Fact]
    public void RealPosition_RequiresAndNormalizesAccountLabel()
    {
        TrackedExecutionModeContract.NormalizeAccountLabel(
            TrackedExecutionMode.Real,
            "  TFSA  ").ShouldBe("TFSA");
        Should.Throw<ArgumentException>(() =>
            TrackedExecutionModeContract.NormalizeAccountLabel(
                TrackedExecutionMode.Real,
                "  "));
    }

    [Fact]
    public async Task Reconciliation_RejectsInvalidRequestsBeforeDatabaseAccess()
    {
        var workflow = new RealPositionReconciliationWorkflow();

        await Should.ThrowAsync<ArgumentException>(() =>
            workflow.MarkAsRealAsync(Guid.Empty, "TFSA"));
        await Should.ThrowAsync<ArgumentException>(() =>
            workflow.MarkAsRealAsync(Guid.NewGuid(), " "));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            workflow.RecordManualExitAsync(Guid.NewGuid(), 0m, DateTime.Now));
    }
}
