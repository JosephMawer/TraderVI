#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPaperReviewSetupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tradervi-setup-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2026, 10, 1);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private readonly Guid portfolio = Guid.NewGuid(), generation = Guid.NewGuid();
    private string SettingsPath => Path.Combine(root, "private", "review-settings.json");

    private AiPaperReviewSettings Settings()
    {
        Directory.CreateDirectory(root);
        string calendar = Path.Combine(root, "calendar.json");
        File.WriteAllText(calendar, JsonSerializer.Serialize(new ReviewedTsxCalendarDocument("test", "synthetic", Date, Date, [Date])));
        return new(false, portfolio, portfolio.ToString("D"), Path.Combine(root, "private"), calendar, Date,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), 15, TimeSpan.FromMinutes(5), []);
    }

    private ShadowPaperRiskObservation Book() => new(Guid.NewGuid(), Date, Open.AddMinutes(10), Open.AddMinutes(12),
        new(generation, SystemShadowVersions.Policy, SystemShadowGenerationStatus.Active, 10000m, 10000m,
            Open.AddDays(-1), Open.AddDays(-1), Open.AddDays(-1)),
        new(portfolio, generation, "BreakoutTop3", "Breakout", 3, "Active", 5000m, 10000m, Open.AddDays(-1)),
        new(Guid.NewGuid(), portfolio, Date.ToDateTime(TimeOnly.MinValue), null, "Monitoring", null, 10000m, false),
        [new(Guid.NewGuid(), portfolio, "TEST", "Open", 50, 100m, 5000m, 5000m, Open.AddMinutes(1), Date.ToDateTime(TimeOnly.MinValue),
            0, 0, null, null, null, false, 100m, Open.AddMinutes(5), 0m, null, null)],
        [new("TEST", new(Guid.NewGuid(), Open.AddMinutes(5), Open.AddMinutes(12), 5, 100m, 100m, 100m, 100m))]);

    [Fact]
    public void PreparationReopensAllHistoryWithAutomaticReviewsOffAndNoCallsOrLessons()
    {
        var settings = Settings();
        var now = Open.AddMinutes(12);
        AiPaperReviewSetup.InitializeNew(settings, SettingsPath, Book(), now);
        Assert.False(AiPaperReviewSettings.Load(SettingsPath)!.Enabled);
        Assert.False(File.Exists(SettingsPath + ".preparing"));
        string shared = Path.Combine(settings.JournalRoot, "shared");
        string account = Path.Combine(settings.JournalRoot, "paper", portfolio.ToString("D"));
        var calendar = ReviewedTsxSessionCalendar.Load(settings.CalendarPath);
        using var calls = new AiCallJournal(shared);
        Assert.Empty(calls.ReadAll());
        using var learning = new AiLearningJournal(shared);
        Assert.Empty(learning.Consult("supervisor", "test", "paper", [new("test", "v1")], now, TimeSpan.FromMinutes(5))!.Lessons);
        using var risk = new AiPortfolioRiskJournal(account, new(portfolio, portfolio, AiRiskAccountMode.Paper), new(calendar));
        Assert.Equal(10000m, risk.ReadCurrent().State!.RoundPeak);
        using var schedule = new AiReviewScheduleJournal(account, new(portfolio, portfolio, "paper"), calendar,
            Date, AiPaperReviewSettings.AgreedTiming, now);
        Assert.Null(schedule.Read(now).LastAttempt);
    }

    [Fact]
    public void RepeatedPreparationPreservesExistingFiles()
    {
        var settings = Settings();
        AiPaperReviewSetup.InitializeNew(settings, SettingsPath, Book(), Open.AddMinutes(12));
        var before = Directory.GetFiles(settings.JournalRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => AiPaperReviewSetup.InitializeNew(settings, SettingsPath, Book(), Open.AddMinutes(12)));
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("stale")]
    [InlineData("scope")]
    [InlineData("missing")]
    public void InvalidOpeningCreatesNoPrivateHistory(string failure)
    {
        var settings = Settings();
        var book = Book();
        if (failure == "enabled") settings = settings with { Enabled = true };
        if (failure == "scope") settings = settings with { PortfolioId = Guid.NewGuid() };
        Assert.ThrowsAny<Exception>(() => AiPaperReviewSetup.InitializeNew(settings, SettingsPath,
            failure == "missing" ? null! : book, Open.AddMinutes(failure == "stale" ? 30 : 12)));
        Assert.False(Directory.Exists(settings.JournalRoot));
    }

    [Fact]
    public void InterruptedPreparationCannotLoadOrReplaceHistory()
    {
        var settings = Settings();
        Directory.CreateDirectory(settings.JournalRoot);
        File.WriteAllText(SettingsPath + ".preparing", "preserve");
        Assert.Throws<InvalidDataException>(() => AiPaperReviewSettings.Load(SettingsPath));
        Assert.Throws<IOException>(() => AiPaperReviewSetup.InitializeNew(settings, SettingsPath, Book(), Open.AddMinutes(12)));
        Assert.Equal("preserve", File.ReadAllText(SettingsPath + ".preparing"));
        Assert.False(Directory.Exists(Path.Combine(settings.JournalRoot, "shared")));
    }

    public void Dispose()
    {
        // The fixture owns this unique temporary directory only.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
