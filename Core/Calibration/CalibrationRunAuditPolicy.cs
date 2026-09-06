#nullable enable

using System;
using System.Collections.Generic;

namespace Core.Calibration;

public sealed record CalibrationRunAuditDecision(
    CalibrationAuditState State,
    string? Message);

public static class CalibrationRunAuditPolicy
{
    public static CalibrationRunAuditDecision Evaluate(
        CodeProvenance code,
        int loadedModelCount,
        int expectedModelCount)
    {
        ArgumentNullException.ThrowIfNull(code);

        var messages = new List<string>();
        if (string.Equals(code.Commit, "unavailable", StringComparison.OrdinalIgnoreCase))
            messages.Add("Code commit is unavailable.");

        if (loadedModelCount != expectedModelCount)
        {
            messages.Add(
                $"Loaded model provenance count {loadedModelCount} does not match enabled code registry count {expectedModelCount}.");
        }

        return messages.Count == 0
            ? new CalibrationRunAuditDecision(CalibrationAuditState.Valid, null)
            : new CalibrationRunAuditDecision(CalibrationAuditState.Invalid, string.Join(" ", messages));
    }
}
