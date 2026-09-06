using Core.Calibration;
using Shouldly;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CalibrationRunAuditPolicyTests
{
    [Theory]
    [InlineData("Clean")]
    [InlineData("Dirty")]
    [InlineData("Unknown")]
    public void CompleteProvenance_IsValidRegardlessOfWorkingTreeState(string workingTreeState)
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("abc123", "Git", workingTreeState),
            loadedModelCount: 4,
            expectedModelCount: 4);

        decision.State.ShouldBe(CalibrationAuditState.Valid);
        decision.Message.ShouldBeNull();
    }

    [Fact]
    public void MissingCodeCommit_IsInvalid()
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("unavailable", "Unavailable", "Unknown"),
            loadedModelCount: 4,
            expectedModelCount: 4);

        decision.State.ShouldBe(CalibrationAuditState.Invalid);
        decision.Message.ShouldBe("Code commit is unavailable.");
    }

    [Fact]
    public void IncompleteModelProvenance_IsInvalid()
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("abc123", "Git", "Dirty"),
            loadedModelCount: 3,
            expectedModelCount: 4);

        decision.State.ShouldBe(CalibrationAuditState.Invalid);
        decision.Message.ShouldContain("Loaded model provenance count 3");
    }
}
