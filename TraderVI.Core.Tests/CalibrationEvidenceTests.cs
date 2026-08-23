using Core.Calibration;
using Core.Db;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public class CalibrationEvidenceTests
{
    [Fact]
    public void OfficialRunRequiresStrategyVersion()
    {
        var run = ValidRun() with { StrategyVersionId = null };
        var batch = new CalibrationEvidenceBatch(run, [], []);

        Should.Throw<ArgumentException>(() => CalibrationEvidenceRepository.Validate(batch))
            .Message.ShouldContain("strategy version");
    }

    [Fact]
    public void DuplicateCandidateSymbolsAreRejectedCaseInsensitively()
    {
        var run = ValidRun() with { SymbolsModelEvaluated = 2 };
        var first = Candidate(run.RunId, "RY");
        var second = Candidate(run.RunId, "ry");

        Should.Throw<ArgumentException>(() =>
            CalibrationEvidenceRepository.Validate(new CalibrationEvidenceBatch(run, [first, second], [])))
            .Message.ShouldContain("duplicate candidate symbols");
    }

    private static CalibrationRunEvidence ValidRun() => new(
        Guid.NewGuid(), CalibrationRunPurpose.OfficialPaper, new DateTime(2026, 8, 23),
        new DateTime(2026, 8, 21), DateTime.UtcNow, Guid.NewGuid(), "{}", "[]", "{}",
        new CodeProvenance("abc123", "Git", "Clean"), CalibrationAuditState.Valid, null,
        0, 0, 0, 0, 0, 0, 0, 0);

    private static CalibrationCandidateEvidence Candidate(Guid runId, string symbol) => new(
        Guid.NewGuid(), runId, symbol, new DateTime(2026, 8, 21), 10, 11, 9, 10, 100000,
        .5, .2, .4, .3, .3, .4, .1, .2, "Rising", .1, "{}");
}
