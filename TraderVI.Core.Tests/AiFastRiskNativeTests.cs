#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.TMX;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>
/// Explicit native diagnostic: SELECT-only accounts, at most 48 one-minute TMX requests (144 transport
/// attempts), and isolated local diagnostic journals. Two passes one minute apart during a regular
/// session; one rejection-only pass after hours. No production journal, account, order or app changes.
/// Ages below are diagnostic bounds, not an operational freshness policy. No financial values/IDs in output.
/// </summary>
public sealed class AiFastRiskNativeTests(ITestOutputHelper output)
{
    [NativeAiFastRiskFact]
    [Trait("Category", "NativeSqlAndTmxReadOnly")]
    public async Task ConnectedPaperReadsPersistOrExplainRejectionAndRecoverInIsolatedJournals()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;
        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar is required."));
        string root = Environment.GetEnvironmentVariable("TRADERVI_AI_RISK_DIAGNOSTIC_DIRECTORY")
            ?? throw new InvalidOperationException("An isolated diagnostic journal directory is required.");
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException("Diagnostic directory must be absolute.");
        string run = Path.Combine(root, Guid.NewGuid().ToString("N")); // Always new; never opens or overwrites production state.
        var now = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(date);
        bool duringSession = now >= bounds.OpenUtc.AddMinutes(1) && now < bounds.CloseUtc.AddMinutes(-3);
        bool outsideSession = now < bounds.OpenUtc || now >= bounds.CloseUtc;
        if (!duringSession && !outsideSession) throw new InvalidOperationException("Not run near session open/close; require three minutes before close.");
        var accountAge = TimeSpan.FromSeconds(45);
        var priceAge = TimeSpan.FromMinutes(2);
        var receiptAge = TimeSpan.FromSeconds(30);
        var policy = new AiPortfolioRiskPolicy(calendar);
        var catalogue = await new AiPortfolioCatalogRepository(TimeSpan.FromMinutes(10)).ReadAsync(ct);
        Assert.InRange(catalogue.Entries.Length, 1, 20);
        var shadow = new SystemShadowRepository();
        var live = new DelphiLiveLedgerRepository();
        int holdingsTotal = 0;
        var baseline = new Dictionary<string, (int Books, int Invested)>();
        // Validate actual cash/full history even after hours; these checks do not claim current market prices.
        foreach (var choice in catalogue.Entries)
        {
            int holdings;
            if (choice.EngineFamily == "DailyShadow")
            {
                var read = await shadow.ReadAccountAsync(choice.PaperAccountId, date, ct);
                Assert.True(read is not null, "A current Shadow account read is unavailable.");
                SystemShadowAiPortfolioRiskMonitor.ValidateAccount(choice.PaperAccountId, read!.Book, DateTimeOffset.UtcNow);
                holdings = read.Book.Positions.Count(h => h.Status == "Open");
            }
            else
            {
                Assert.True(choice.EngineFamily == "DelphiLive", "An unsupported family needs a reviewed reader.");
                var read = await live.LoadPortfolioAsync(choice.PaperAccountId, ct);
                DelphiLiveAiPortfolioRiskMonitor.ValidateAccount(choice.PaperAccountId, read, DateTimeOffset.UtcNow);
                Assert.True(read!.CurrentSession == date, "The account has no current session.");
                DelphiLiveAiPortfolioRiskMonitor.ValidateLedger(read, DateTime.UtcNow);
                holdings = read.OpenPositions.Count();
            }
            Assert.InRange(holdings, 0, 8);
            holdingsTotal += holdings;
            var counts = baseline.GetValueOrDefault(choice.EngineFamily);
            baseline[choice.EngineFamily] = (counts.Books + 1, counts.Invested + (holdings > 0 ? 1 : 0));
        }
        Assert.True(holdingsTotal <= 24, "Held-symbol workload exceeds the bounded diagnostic request allowance.");
        foreach (var (family, counts) in baseline)
            output.WriteLine($"Baseline {family}: reconciled={counts.Books}, invested={counts.Invested}, cash-only={counts.Books - counts.Invested}.");
        output.WriteLine($"StartedUtc={now:O}; mode={(duringSession ? "two regular-session passes" : "after-hours rejection only")}; account age=45s, price age=120s, receipt age=30s (diagnostic only).");
        using var client = new TmxClient();
        int requests = 0, attempts = 0;
        var prices = new TmxAiRiskPriceSource(async (symbol, start, end, token) =>
        {
            if (requests >= 48 || !duringSession) throw new InvalidOperationException("Diagnostic request bound reached.");
            requests++;
            var batch = await client.GetIntradayTimeSeriesBatchAsync(symbol, 1, start, end, token);
            attempts += batch.AttemptCount;
            return batch;
        }, calendar, priceAge, receiptAge, TimeSpan.FromSeconds(20));
        var paths = new Dictionary<Guid, string>();
        var summaries = new Dictionary<string, int>();
        var elapsed = new List<long>();
        int accepted = 0, recovered = 0, transitions = 0;
        for (int pass = 0; pass < (duringSession ? 2 : 1); pass++)
        {
            if (pass == 1) await Task.Delay(TimeSpan.FromSeconds(60), ct);
            int slot = 0;
            foreach (var choice in catalogue.Entries)
            {
                slot++;
                var timer = Stopwatch.StartNew();
                var scope = new AiRiskJournalScope(choice.PaperAccountId, choice.PaperAccountId, AiRiskAccountMode.Paper);
                string path = paths.GetValueOrDefault(choice.PaperAccountId) ?? Path.Combine(run, $"account-{slot:D2}");
                bool initializeFile = !paths.ContainsKey(choice.PaperAccountId);
                paths[choice.PaperAccountId] = path;
                AiRiskJournalSnapshot saved;
                using (var journal = new AiPortfolioRiskJournal(path, scope, policy, initializeFile))
                {
                    var source = choice.EngineFamily == "DailyShadow"
                        ? new PaperAiFastValuationReader(choice.PaperAccountId, shadow, prices, calendar, accountAge)
                        : new PaperAiFastValuationReader(choice.PaperAccountId, live, prices, calendar, accountAge);
                    var candidate = await source.ReadAsync(date, ct);
                    var monitor = new PaperAiFastRiskMonitor(journal, policy, calendar, accountAge, priceAge, receiptAge);
                    bool start = journal.ReadCurrent().State is null;
                    var result = start ? monitor.Start(candidate.Valuation, DateTimeOffset.UtcNow, false, ["NativeDiagnosticOnly"]) :
                        monitor.Observe(candidate.Valuation, DateTimeOffset.UtcNow, false, ["NativeDiagnosticOnly"]);
                    string reason = candidate.BlockReason ?? (result.Accepted ? "Accepted" : "RiskRejected");
                    string key = $"pass={pass + 1} {choice.EngineFamily} {reason}";
                    summaries[key] = summaries.GetValueOrDefault(key) + 1;
                    Assert.True(result.Assessment.NewBuyAllowanceMultiplier == 0m, "Diagnostic observations must not grant new buying.");
                    if (outsideSession)
                        Assert.True(candidate.BlockReason == "OutsideSession" && !result.Accepted && journal.ReadHistory().IsEmpty,
                            "After-hours reads must not create a valuation or risk transition.");
                    if (result.Accepted)
                    {
                        accepted++;
                        Assert.True(candidate.Valuation is not null && result.Snapshot.State!.LastValuation.Value == candidate.Valuation.Value,
                            "Journal value differs from its verified account preparation.");
                        var expired = monitor.Observe(candidate.Valuation, candidate.Valuation!.ValidUntilUtc.AddTicks(1), false, ["NativeDiagnosticOnly"]);
                        Assert.True(!expired.Accepted && expired.Assessment.NewBuyAllowanceMultiplier == 0m,
                            "An expired prepared value must be rejected without renewing evidence.");
                    }
                    saved = journal.ReadCurrent();
                    if (pass == (duringSession ? 1 : 0)) transitions += journal.ReadHistory().Length;
                }
                using (var reopened = new AiPortfolioRiskJournal(path, scope, policy))
                    Assert.True(reopened.ReadCurrent() == saved, "Diagnostic journal changed on reopen.");
                recovered++;
                elapsed.Add(timer.ElapsedMilliseconds);
            }
        }
        foreach (var (reason, count) in summaries.OrderBy(p => p.Key)) output.WriteLine($"{reason}: {count}.");
        output.WriteLine($"Requests={requests}; transport attempts in returned batches={attempts} (failed-request attempts unavailable); accepted observations={accepted}; saved transitions={transitions}; journal reopens verified={recovered}; account-check elapsed min={elapsed.Min()}ms max={elapsed.Max()}ms.");
        if (outsideSession) Assert.Equal(0, requests);
        output.WriteLine(duringSession
            ? "Bounded native diagnostic complete; rejections remain blocks. This does not qualify continuous coverage, collection concurrency, operating age limits or storage retention."
            : "After-hours rejection and empty-journal recovery verified. Fresh combined values and their native persistence remain unverified until a regular-session run.");
    }
}

public sealed class NativeAiFastRiskFactAttribute : FactAttribute
{
    public NativeAiFastRiskFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_RUN_AI_FAST_RISK_NATIVE") != "1")
            Skip = "Explicit opt-in required: SQL reads, bounded TMX requests during market hours, isolated diagnostic journal writes.";
    }
}
