#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Oracle.Llm;
using Core.Trader;
using Core.Trader.DelphiLive;

namespace Sandbox.Probes;

/// <summary>Explicit Preflight, Prepare or Review. Never starts the app or an automatic review loop.</summary>
public sealed class AiPaperFirstReviewProbe : IProbe
{
    public string Slug => "ai-paper-first-review";
    public string Description => "Selected Breakout 3 book: read-only preflight, explicit initial setup, or one paid review.";

    public async Task RunAsync()
    {
        string stage = "configuration";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var ct = timeout.Token;
            string mode = Environment.GetEnvironmentVariable("TRADERVI_AI_FIRST_REVIEW_MODE") ?? "Preflight";
            if (mode is not ("Preflight" or "Prepare" or "Review")) throw new InvalidOperationException();
            string settingsPath = AiPaperReviewSettings.LocalSettingsPath;
            if (mode == "Review")
            {
                stage = "one connected review";
                var saved = AiPaperReviewSettings.Load(settingsPath) ?? throw new InvalidOperationException();
                if (saved.Enabled) throw new InvalidOperationException("Stop automatic operation before a bounded check.");
                AiScheduledReviewResult result;
                using (var runtime = new AiPaperReviewRuntime(saved with { Enabled = true }))
                    result = await runtime.ReviewOnceAsync(ct);
                Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}; outcome: {result.Schedule.LastAttempt?.Outcome}; " +
                    $"block: {result.Schedule.ReviewBlock ?? "none"}; proposal returned: {result.Proposal is not null}; " +
                    $"action: {result.Proposal?.Action ?? "none"}; next interval minutes: {result.Schedule.Interval.Applied.TotalMinutes}.");
                using var calls = new AiCallJournal(Path.Combine(saved.JournalRoot, "shared"));
                var call = calls.ReadAll().SingleOrDefault(c => c.CallId == result.Schedule.LastAttempt?.Request.Id);
                Console.WriteLine($"Call recorded: {call is not null}; input tokens: {call?.InputTokens}; output tokens: {call?.OutputTokens}; " +
                    $"conservative CAD accounting: {call?.ChargedCad}; reserved CAD: {call?.ReservedCad}.");
                if (result.Proposal is null) throw new InvalidOperationException("A current proposal was not returned.");
                return;
            }

            stage = "selected portfolio read";
            var catalog = await new AiPortfolioCatalogRepository(TimeSpan.FromMinutes(10)).ReadAsync(ct);
            var choice = catalog.Entries.Single(c => c.Comparison is { StrategyCode: "BreakoutTop3", Profile: PaperGateProfile.PortfolioRulesOnlyV1 });
            string calendarPath = Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
                ?? throw new InvalidOperationException("An explicit reviewed calendar is required.");
            var now = DateTimeOffset.UtcNow;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
            var book = await new SystemShadowRepository().ReadRiskObservationAsync(choice.PaperAccountId, date, ct);
            Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}; catalogue choices: {catalog.Entries.Length}; " +
                $"selected book present: {book is not null}; open holdings: {book?.Positions.Count(p => p.Status == "Open")}; " +
                $"price marks: {book?.Prices.Length}; valuation UTC: {book?.At:O}; cash verification UTC: {book?.CashVerifiedUtc:O}.");
            if (mode == "Preflight") return;

            stage = "opening qualification and initial history";
            if (!int.TryParse(Environment.GetEnvironmentVariable("TRADERVI_AI_FIRST_REVIEW_AGE_MINUTES"), out int minutes) || minutes <= 0)
                throw new InvalidOperationException("An explicit approved first-review age is required.");
            var settings = new AiPaperReviewSettings(false, choice.PaperAccountId, choice.Id,
                Path.GetDirectoryName(settingsPath)!, Path.GetFullPath(calendarPath), date,
                TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes), 15,
                TimeSpan.FromMinutes(5), ImmutableArray<string>.Empty);
            AiPaperReviewSetup.InitializeNew(settings, settingsPath, book ?? throw new InvalidOperationException(), DateTimeOffset.UtcNow);
            Console.WriteLine("Initial risk, spending, schedule and empty learning history prepared. Automatic reviews remain disabled. No model call or SQL write.");
        }
        catch (Exception error)
        {
            // Exception bodies may include private SQL, account or provider data. Never print them here.
            Console.WriteLine($"First-review check stopped at {stage}; error category: {error.GetType().Name}. Existing history was preserved; no automatic retry.");
            throw new InvalidOperationException("First-review check did not complete; inspect the bounded stage summary.");
        }
    }
}
