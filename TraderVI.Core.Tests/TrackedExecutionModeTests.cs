#nullable enable

using Core.Db;
using Core.Trader;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TrackedExecutionModeTests
{
    [Theory]
    [InlineData(TrackedExecutionMode.Ghost, true, true)]
    [InlineData(TrackedExecutionMode.Real, true, true)]
    [InlineData(TrackedExecutionMode.Real, false, true)]
    [InlineData(TrackedExecutionMode.Ghost, false, false)]
    public void PositionScope_IncludesLinkedPositionsAndUnlinkedRealHoldings(
        TrackedExecutionMode executionMode,
        bool hasOriginalPick,
        bool expected)
    {
        var position = new ActivePositionInfo
        {
            ExecutionMode = executionMode,
            OriginalPickId = hasOriginalPick ? Guid.NewGuid() : null
        };

        TrackedPositionScope.Includes(position).ShouldBe(expected);
    }

    [Theory]
    [InlineData(TrackedExecutionMode.Ghost, true, true)]
    [InlineData(TrackedExecutionMode.Real, true, true)]
    [InlineData(TrackedExecutionMode.Real, false, false)]
    public void FreshDelphiLossException_RequiresOriginalPickProvenance(
        TrackedExecutionMode executionMode,
        bool hasOriginalPick,
        bool expected)
    {
        var position = new ActivePositionInfo
        {
            ExecutionMode = executionMode,
            OriginalPickId = hasOriginalPick ? Guid.NewGuid() : null
        };

        TrackedPositionScope.AllowsFreshDelphiLossException(position).ShouldBe(expected);
    }

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
            workflow.RecordManualExitAsync(
                Guid.NewGuid(),
                0m,
                DateTime.Now,
                confirmAllSharesZeroCommission: true));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            workflow.RecordManualExitAsync(
                Guid.NewGuid(),
                10m,
                DateTime.Now,
                confirmAllSharesZeroCommission: false));
    }
}
