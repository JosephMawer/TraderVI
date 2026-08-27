#nullable enable

using Core.Calibration;
using Core.Runtime;
using Shouldly;
using System;
using System.IO;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiWorkflowContractsTests
{
    [Fact]
    public void DefaultOptionsDescribeOfficialPersistedRun()
    {
        var options = new DelphiWorkflowOptions();

        options.Validate();

        options.Purpose.ShouldBe(CalibrationRunPurpose.OfficialPaper);
        options.AvailableCapital.ShouldBe(700m);
        options.MaxSymbolsToScan.ShouldBe(500);
        options.TopPicksToSave.ShouldBe(25);
        options.SaveToDatabase.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidTopPickCountIsRejected(int topPicks)
    {
        var options = new DelphiWorkflowOptions(TopPicksToSave: topPicks);

        Should.Throw<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void WorkflowLogWritesOnlyToTheActiveHostWriter()
    {
        using var first = new StringWriter();
        using var second = new StringWriter();

        DelphiWorkflowLog.WriteLine("ignored");
        using (DelphiWorkflowLog.Use(first))
        {
            DelphiWorkflowLog.WriteLine("first");
            using (DelphiWorkflowLog.Use(second))
                DelphiWorkflowLog.WriteLine("second");
            DelphiWorkflowLog.WriteLine("first again");
        }

        first.ToString().ShouldContain("first");
        first.ToString().ShouldContain("first again");
        first.ToString().ShouldNotContain("second");
        second.ToString().ShouldContain("second");
    }
}
