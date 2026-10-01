#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;

namespace Core.Oracle;

/// <summary>Private operator settings. No credentials and no implicit initialization or source-age defaults.</summary>
public sealed record AiPaperReviewSettings(bool Enabled, Guid PortfolioId, string? ActiveChoiceId,
    string JournalRoot, string CalendarPath, DateOnly InitialSession, TimeSpan RiskMaximumAge,
    TimeSpan CatalogMaximumAge, TimeSpan ContextMaximumAge, int HistoryDays,
    TimeSpan LearningRefreshInterval, ImmutableArray<string> IndependentBlocks)
{
    public static AiReviewTiming AgreedTiming => new(TimeSpan.FromMinutes(5), TimeSpan.FromHours(3), TimeSpan.FromMinutes(15));
    // Approved September 30 for the first review; includes all of October 31 in Toronto.
    public static AiSpendPolicy AgreedSpend => new(1.60m, 1.20m,
        new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
    public static string LocalSettingsPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "TraderVI", "AiMomentum", "review-settings.json");

    public static AiPaperReviewSettings? Load(string path)
    {
        if (File.Exists(path + ".preparing")) throw new InvalidDataException("AI setup was interrupted; preserve history for reviewed recovery.");
        if (!File.Exists(path)) return null; // Absence is disabled; it is never a request to create history.
        if (new FileInfo(path).Length > 65_536) throw new InvalidDataException("AI settings are oversized.");
        return JsonSerializer.Deserialize<AiPaperReviewSettings>(File.ReadAllText(path), new JsonSerializerOptions
        {
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("AI settings are missing.");
    }

    public void Validate(DateTimeOffset now)
    {
        if (!Enabled || PortfolioId == Guid.Empty || string.IsNullOrWhiteSpace(JournalRoot) ||
            !Path.IsPathFullyQualified(JournalRoot) || string.IsNullOrWhiteSpace(CalendarPath) ||
            !Path.IsPathFullyQualified(CalendarPath) || InitialSession == default ||
            RiskMaximumAge <= TimeSpan.Zero || CatalogMaximumAge <= TimeSpan.Zero ||
            ContextMaximumAge <= TimeSpan.Zero || LearningRefreshInterval <= TimeSpan.Zero ||
            HistoryDays is < 1 or > 252 || IndependentBlocks.IsDefault ||
            (ActiveChoiceId is not null && string.IsNullOrWhiteSpace(ActiveChoiceId)))
            throw new InvalidDataException("Explicit enabled paper-review scope and operating settings are required.");
        AgreedSpend.Validate(now);
    }
}

/// <summary>
/// Owns reopened journals and the review-only coordinator. No journal creation, account assignments,
/// price collection or execution. Dispose only after the run has stopped.
/// </summary>
public sealed class AiPaperReviewRuntime : IDisposable
{
    private readonly List<IDisposable> owned = [];
    private readonly ReviewedTsxSessionCalendar calendar;
    private readonly AiReviewScheduleJournal schedule;
    private readonly AiPortfolioReviewCoordinator coordinator;
    private readonly TimeProvider clock;

    public AiPaperReviewRuntime(AiPaperReviewSettings settings, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        settings.Validate(this.clock.GetUtcNow());
        calendar = ReviewedTsxSessionCalendar.Load(settings.CalendarPath);
        // Validate timing/session before opening mutable recovery journals.
        _ = new AiReviewScheduler(calendar, settings.InitialSession, AiPaperReviewSettings.AgreedTiming);
        string shared = Path.Combine(settings.JournalRoot, "shared");
        string account = Path.Combine(settings.JournalRoot, "paper", settings.PortfolioId.ToString("D"));
        foreach (var (directory, name) in new[] { (shared, "ai-calls"), (shared, "ai-learning"),
            (account, "ai-risk"), (account, "ai-review-schedule") })
            if (!File.Exists(Path.Combine(directory, name + ".json")) ||
                !File.Exists(Path.Combine(directory, name + ".lock")))
                throw new InvalidDataException("Prepare complete private AI history explicitly before startup; no files were created.");
        try
        {
            var configuration = LlmClientFactory.LoadOracleConfiguration();
            if (configuration is IDisposable disposable) owned.Add(disposable);
            _ = LlmClientFactory.GetOpenAiKey(configuration);
            var calls = Own(new AiCallJournal(shared));
            var learning = Own(new AiLearningJournal(shared));
            var policy = new AiPortfolioRiskPolicy(calendar);
            var risk = Own(new AiPortfolioRiskJournal(account,
                new(settings.PortfolioId, settings.PortfolioId, AiRiskAccountMode.Paper), policy));
            if (risk.ReadCurrent().State is null) throw new InvalidDataException("Initialize qualified risk history explicitly before starting reviews.");
            schedule = Own(new AiReviewScheduleJournal(account, new(settings.PortfolioId, settings.PortfolioId, "paper"),
                calendar, settings.InitialSession, AiPaperReviewSettings.AgreedTiming, this.clock.GetUtcNow()));
            var client = Own(LlmClientFactory.CreateAiMomentum(configuration, calls, AiPaperReviewSettings.AgreedSpend, enabled: true));
            var reader = new PaperAiPortfolioRiskReader(new SystemShadowRepository(),
                new SystemShadowAiPortfolioRiskMonitor(risk, policy, settings.RiskMaximumAge), this.clock);
            coordinator = AiPortfolioReviewCoordinator.FromSavedPaperInputs(schedule, reader, risk,
                () => new(settings.ActiveChoiceId, settings.IndependentBlocks), calendar,
                settings.CatalogMaximumAge, settings.ContextMaximumAge, settings.HistoryDays,
                client, learning, settings.LearningRefreshInterval, this.clock);
        }
        catch { Dispose(); throw; }
    }

    private T Own<T>(T resource) where T : IDisposable { owned.Add(resource); return resource; }

    private int runStarted;
    private void BeginRun()
    {
        if (Interlocked.Exchange(ref runStarted, 1) != 0) throw new InvalidOperationException("This runtime already started a review run.");
    }

    public Task RunAsync(Action<AiReviewHostNotice> report, CancellationToken ct)
    {
        BeginRun();
        return loop.RunAsync(TickAsync, report, ct);
    }

    /// <summary>One scheduler admission for an explicitly requested connected check; starts no loop.</summary>
    public Task<AiScheduledReviewResult> ReviewOnceAsync(CancellationToken ct = default)
    {
        BeginRun();
        return TickAsync(ct);
    }
    private readonly AiReviewHostLoop loop = new();

    private Task<AiScheduledReviewResult> TickAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
        if (calendar.IsRegularSession(today))
        {
            var bounds = calendar.GetSessionBounds(today);
            if ((DateTimeOffset)bounds.CloseUtc > schedule.SessionCloseUtc && now < bounds.CloseUtc)
                schedule.AdvanceSession(today, now);
        }
        return coordinator.TickAsync(ct);
    }

    public void Dispose()
    {
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        owned.Clear();
    }
}

public sealed record AiReviewHostNotice(string State, DateTimeOffset? NextReviewUtc, Guid? LastAttemptId);

/// <summary>A background tick only checks the existing scheduler; it is not a paid-call interval.</summary>
public sealed class AiReviewHostLoop
{
    private int started;
    public async Task RunAsync(Func<CancellationToken, Task<AiScheduledReviewResult>> tick,
        Action<AiReviewHostNotice> report, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("A review host can run only once.");
        AiReviewHostNotice? previous = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var result = await tick(ct).ConfigureAwait(false);
                var notice = new AiReviewHostNotice(result.Schedule.ReviewBlock ?? "Reviewed",
                    result.Schedule.NextReviewUtc, result.Schedule.LastAttempt?.Request.Id);
                if (notice != previous) { report(notice); previous = notice; }
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        // Unexpected storage/configuration/transport failure stops this owner. Do not leak exception bodies or auto-retry.
        catch { report(new("NeedsAttention", null, null)); }
    }
}
