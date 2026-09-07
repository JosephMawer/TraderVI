using Core.Calibration;
using Shouldly;
using System;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CalibrationRunAuditPolicyTests
{
    private static readonly string[] Tasks = ["Up", "Down", "Setup", "Confirmation"];
    private static ModelArtifactProvenance[] Artifacts() => Tasks.Select(task =>
        new ModelArtifactProvenance(Guid.NewGuid(), task, "BinaryClassification", "ProfitWindow", "synthetic", null, null, new string('A', 64))).ToArray();

    [Theory]
    [InlineData("Clean")]
    [InlineData("Dirty")]
    [InlineData("Unknown")]
    public void CompleteProvenance_IsValidRegardlessOfWorkingTreeState(string workingTreeState)
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("abc123", "Git", workingTreeState),
            Artifacts(), Tasks, Tasks);

        decision.State.ShouldBe(CalibrationAuditState.Valid);
        decision.Message.ShouldBeNull();
    }

    [Fact]
    public void MissingCodeCommit_IsInvalid()
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("unavailable", "Unavailable", "Unknown"),
            Artifacts(), Tasks, Tasks);

        decision.State.ShouldBe(CalibrationAuditState.Invalid);
        decision.Message.ShouldBe("Code commit is unavailable.");
    }

    [Fact]
    public void IncompleteModelProvenance_IsInvalid()
    {
        CalibrationRunAuditDecision decision = CalibrationRunAuditPolicy.Evaluate(
            new CodeProvenance("abc123", "Git", "Dirty"),
            Artifacts().Take(3).ToArray(), Tasks.Take(3).ToArray(), Tasks);

        decision.State.ShouldBe(CalibrationAuditState.Invalid);
        decision.Message.ShouldContain("Loaded model provenance count 3");
    }

    [Fact]
    public void MatchingCountsCannotHideWrongTaskOrMissingLoadedInstance()
    {
        var artifacts = Artifacts();
        artifacts[3] = artifacts[3] with { TaskType = "Other" };
        CalibrationRunAuditPolicy.Evaluate(new("abc123", "Git", "Clean"), artifacts, Tasks, Tasks)
            .State.ShouldBe(CalibrationAuditState.Invalid);
        CalibrationRunAuditPolicy.Evaluate(new("abc123", "Git", "Clean"), Artifacts(), ["Up", "Down", "Setup", "Other"], Tasks)
            .State.ShouldBe(CalibrationAuditState.Invalid);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-task")]
    [InlineData("bad-hash")]
    public void UnverifiableArtifactIdentityCannotPassWithACompleteCount(string defect)
    {
        var artifacts = Artifacts();
        artifacts[3] = defect switch
        {
            "missing-id" => artifacts[3] with { ModelId = Guid.Empty },
            "duplicate-id" => artifacts[3] with { ModelId = artifacts[0].ModelId },
            "duplicate-task" => artifacts[3] with { TaskType = artifacts[0].TaskType },
            _ => artifacts[3] with { ArtifactSha256 = new string('Z', 64) }
        };
        CalibrationRunAuditPolicy.Evaluate(new("abc123", "Git", "Clean"), artifacts, Tasks, Tasks)
            .State.ShouldBe(CalibrationAuditState.Invalid);
    }
}
