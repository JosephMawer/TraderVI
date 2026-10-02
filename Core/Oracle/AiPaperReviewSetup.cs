#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;

namespace Core.Oracle;

/// <summary>Explicit initial/session preparation. Never called by startup or used to replace existing history.</summary>
public static class AiPaperReviewSetup
{
    /// <summary>Separately authorized session preparation; does not reset any history or enable reviews.</summary>
    public static AiLearningSnapshot ContinueEmptyLearning(AiPaperReviewSettings settings, DateTimeOffset now)
    {
        if (settings.Enabled) throw new InvalidDataException("Stop automatic reviews before explicit session preparation.");
        (settings with { Enabled = true }).Validate(now);
        var calendar = ReviewedTsxSessionCalendar.Load(settings.CalendarPath);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
        var bounds = calendar.GetSessionBounds(today);
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc || today <= settings.InitialSession)
            throw new InvalidDataException("Empty learning continuation requires a later open reviewed session.");
        using var learning = new AiLearningJournal(Path.Combine(settings.JournalRoot, "shared"));
        return learning.ContinueEmptySnapshot(now, bounds.CloseUtc);
    }

    public static void ValidateOpening(AiPaperReviewSettings settings, ShadowPaperRiskObservation? book, DateTimeOffset now)
    {
        (settings with { Enabled = true }).Validate(now);
        var calendar = ReviewedTsxSessionCalendar.Load(settings.CalendarPath);
        var bounds = calendar.GetSessionBounds(settings.InitialSession);
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc || book?.TradingDate != settings.InitialSession)
            throw new InvalidDataException("First preparation requires today's open reviewed session and current book.");
        var value = SystemShadowAiPortfolioRiskMonitor.Qualify(settings.PortfolioId, book, null, settings.RiskMaximumAge, now);
        _ = new AiPortfolioRiskPolicy(calendar).Start(value, now);
    }

    public static void InitializeNew(AiPaperReviewSettings settings, string settingsPath,
        ShadowPaperRiskObservation book, DateTimeOffset now)
    {
        if (settings.Enabled) throw new InvalidDataException("First preparation must leave automatic reviews disabled.");
        ValidateOpening(settings, book, now); // No files are created if the input cannot qualify.
        if (!Path.IsPathFullyQualified(settingsPath) || File.Exists(settingsPath))
            throw new InvalidDataException("A new absolute settings path is required; existing setup must be preserved.");
        string shared = Path.Combine(settings.JournalRoot, "shared");
        string account = Path.Combine(settings.JournalRoot, "paper", settings.PortfolioId.ToString("D"));
        if (Directory.Exists(shared) || Directory.Exists(account))
            throw new InvalidDataException("AI history already exists or preparation was interrupted; reviewed recovery is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        // Exclusive first-preparation marker. Interrupted attempts retain it and all partial files.
        using var marker = new FileStream(settingsPath + ".preparing", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var calendar = ReviewedTsxSessionCalendar.Load(settings.CalendarPath);
        var policy = new AiPortfolioRiskPolicy(calendar);
        using (var calls = new AiCallJournal(shared, initializeNew: true))
        using (var learning = new AiLearningJournal(shared, initializeNew: true))
        using (var risk = new AiPortfolioRiskJournal(account,
            new(settings.PortfolioId, settings.PortfolioId, AiRiskAccountMode.Paper), policy, initializeNew: true))
        using (var schedule = new AiReviewScheduleJournal(account,
            new(settings.PortfolioId, settings.PortfolioId, "paper"), calendar, settings.InitialSession,
            AiPaperReviewSettings.AgreedTiming, now, initializeNew: true))
        {
            var started = new SystemShadowAiPortfolioRiskMonitor(risk, policy, settings.RiskMaximumAge)
                .Start(book, now, false, settings.IndependentBlocks);
            if (!started.Accepted) throw new InvalidDataException("Opening risk observation was not saved; preserve preparation files.");
            // No lessons exist yet. This dated empty snapshot is not a claim about lesson quality.
            learning.Publish(new(Guid.NewGuid(), 1, now, calendar.GetSessionBounds(settings.InitialSession).CloseUtc,
                ImmutableArray<AiEvaluatedLesson>.Empty), now);
        }
        using (var file = new FileStream(settingsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, settings, new JsonSerializerOptions { WriteIndented = true });
            file.Flush(flushToDisk: true);
        }
        marker.Dispose();
        File.Delete(settingsPath + ".preparing");
    }
}
