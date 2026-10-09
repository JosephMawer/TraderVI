#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace Core.Oracle;

/// <summary>Explicit process-only desktop qualification. Never persists enabled settings or changes policy.</summary>
public sealed record AiReviewTestSession
{
    public const string EnvironmentVariable = "TRADERVI_AI_REVIEW_TEST_UNTIL_UTC";
    public const int MaximumAttempts = 2;
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    private AiReviewTestSession(DateTimeOffset startedUtc, DateTimeOffset deadlineUtc)
        => (StartedUtc, DeadlineUtc) = (startedUtc, deadlineUtc);

    public AiPaperReviewSettings EnableForThisLaunch(AiPaperReviewSettings? settings)
    {
        if (settings is null || settings.Enabled)
            throw new InvalidOperationException("A bounded test requires existing disabled review settings.");
        return settings with { Enabled = true };
    }

    public static AiReviewTestSession? Parse(string? value, DateTimeOffset now)
    {
        if (value is null) return null;
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end) ||
            end <= now || end > now.AddMinutes(10))
            throw new InvalidDataException("A desktop review test requires an explicit UTC deadline within ten minutes.");
        return new(now, end);
    }
}
