#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Calibration;

public sealed record CalibrationRunAuditDecision(
    CalibrationAuditState State,
    string? Message);

public static class CalibrationRunAuditPolicy
{
    public static CalibrationRunAuditDecision Evaluate(
        CodeProvenance code,
        IReadOnlyList<ModelArtifactProvenance> artifacts,
        IReadOnlyList<string> loadedTaskTypes,
        IReadOnlyList<string> expectedTaskTypes)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(loadedTaskTypes);
        ArgumentNullException.ThrowIfNull(expectedTaskTypes);
        var countAudit = EvaluateCounts(code, artifacts.Count, expectedTaskTypes.Count);
        var messages = new List<string>();
        if (countAudit.Message is not null) messages.Add(countAudit.Message);
        var comparer = StringComparer.OrdinalIgnoreCase;
        bool SameTasks(IEnumerable<string> actual) => actual.OrderBy(x => x, comparer)
            .SequenceEqual(expectedTaskTypes.OrderBy(x => x, comparer), comparer);
        if (expectedTaskTypes.Distinct(comparer).Count() != expectedTaskTypes.Count ||
            !SameTasks(loadedTaskTypes) || !SameTasks(artifacts.Select(x => x.TaskType)))
            messages.Add("Loaded model instances, captured artifacts and expected profit tasks do not correspond exactly.");
        if (artifacts.Any(x => x.ModelId == Guid.Empty || string.IsNullOrWhiteSpace(x.ModelKind) ||
            string.IsNullOrWhiteSpace(x.InputSchema) || x.ArtifactSha256 is not { Length: 64 } ||
            !x.ArtifactSha256.All(Uri.IsHexDigit)) || artifacts.Select(x => x.ModelId).Distinct().Count() != artifacts.Count)
            messages.Add("Loaded model provenance contains missing, duplicate or invalid artifact identity.");
        return messages.Count == 0
            ? new(CalibrationAuditState.Valid, null)
            : new(CalibrationAuditState.Invalid, string.Join(" ", messages));
    }

    private static CalibrationRunAuditDecision EvaluateCounts(
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
