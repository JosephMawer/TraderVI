#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>
/// Explicitly opted-in SELECT-only check of installed paper records. No collectors, engines,
/// AI clients, journals or writes. Output contains counts/timings, never account values or signals.
/// Separately read catalogue/context snapshots do not establish concurrent runtime consistency.
/// </summary>
public sealed class AiDecisionReadersSqlTests(ITestOutputHelper output)
{
    [NativeAiReadersFact]
    [Trait("Category", "NativeSqlReadOnly")]
    public async Task InstalledCatalogueAndLatestSavedPaperContextsMapWithoutRefreshingEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var calendar = ReviewedTsxSessionCalendar.Load(
            Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("An explicit reviewed calendar path is required."));
        // This age is a diagnostic input, not an approved runtime freshness policy.
        var maximumAge = TimeSpan.FromMinutes(10);
        var repository = new AiPortfolioCatalogRepository(maximumAge);
        var timer = Stopwatch.StartNew();
        var catalogue = await repository.ReadAsync(ct);
        output.WriteLine($"Catalogue: {catalogue.Entries.Length} choices in {timer.ElapsedMilliseconds} ms.");
        Assert.NotEmpty(catalogue.Entries);
        Assert.Equal(catalogue.Entries.Length, catalogue.Entries.Select(e => e.PaperAccountId).Distinct().Count());
        await using var connection = new SqlConnection(repository.ConnectionString);
        await connection.OpenAsync(ct);
        var shadowSessions = await connection.QueryAsync(new CommandDefinition("""
SELECT s.Status,COUNT(*) AccountCount,MIN(s.TradingDate) FirstDate,MAX(s.TradingDate) LastDate,
 SUM(CASE WHEN COALESCE(r.CalibrationRunId,s.CalibrationRunId) IS NULL THEN 1 ELSE 0 END) MissingRunCount
FROM dbo.ShadowPortfolio p
CROSS APPLY(SELECT TOP(1) * FROM dbo.ShadowPortfolioSession WHERE PortfolioId=p.PortfolioId ORDER BY TradingDate DESC) s
OUTER APPLY(SELECT TOP(1) CalibrationRunId FROM dbo.ShadowDailySourceRevision WHERE SessionId=s.SessionId ORDER BY AdoptedUtc DESC,RevisionId) r
WHERE p.PortfolioId IN @Ids GROUP BY s.Status;
""", new { Ids = catalogue.Entries.Where(e => e.EngineFamily == "DailyShadow").Select(e => e.PaperAccountId).ToArray() }, cancellationToken: ct));
        foreach (var summary in shadowSessions)
            output.WriteLine($"Latest Shadow sessions: status={summary.Status}, count={summary.AccountCount}, missing publication={summary.MissingRunCount}, dates={summary.FirstDate:yyyy-MM-dd}..{summary.LastDate:yyyy-MM-dd}.");
        var failures = new List<string>();
        int shadowReads = 0, liveReads = 0;
        foreach (var group in catalogue.Entries.GroupBy(e => e.EngineFamily))
        {
            int loaded = 0, missing = 0, undefined = 0, blocked = 0, proposable = 0;
            timer.Restart();
            foreach (var choice in group)
            {
                try
                {
                    choice.Validate();
                    if (choice.Strategy is null) { undefined++; continue; }
                    bool shadow = choice.Strategy.Family == TradingStrategyFamily.DailyShadow;
                    string sql = shadow
                        ? "SELECT MAX(TradingDate) FROM dbo.ShadowPortfolioSession WHERE PortfolioId=@Id;"
                        : "SELECT TRY_CONVERT(date,JSON_VALUE(SnapshotJson,'$.currentSession')) FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Id;";
                    var storedDate = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(sql,
                        new { Id = choice.PaperAccountId }, cancellationToken: ct));
                    if (storedDate is null) { missing++; continue; }
                    var date = DateOnly.FromDateTime(storedDate.Value);
                    var close = new DateTimeOffset(calendar.GetSessionBounds(date).CloseUtc).AddTicks(1);
                    DateTimeOffset AfterClose(DateTimeOffset at) => close > at ? close : at;
                    AiStrategyDecisionContext context;
                    bool hasSaved;
                    if (shadow)
                    {
                        var saved = await new AiShadowDecisionRepository(30).ReadAsync(choice.PaperAccountId, date, ct);
                        var now = DateTimeOffset.UtcNow;
                        shadowReads++;
                        hasSaved = saved is not null;
                        context = AiShadowDecisionContext.Build(choice, saved, calendar, date, maximumAge, now);
                        Assert.False(AiShadowDecisionContext.Build(choice, saved, calendar, date, maximumAge, AfterClose(now)).CanPropose);
                    }
                    else
                    {
                        var saved = await new AiLiveDecisionRepository(30).ReadAsync(choice.PaperAccountId, date, ct);
                        var now = DateTimeOffset.UtcNow;
                        liveReads++;
                        hasSaved = saved is not null;
                        context = AiLiveDecisionContext.Build(choice, saved, calendar, date, maximumAge, now);
                        Assert.False(AiLiveDecisionContext.Build(choice, saved, calendar, date, maximumAge, AfterClose(now)).CanPropose);
                    }
                    if (hasSaved) loaded++; else missing++;
                    if (context.CanPropose) proposable++; else blocked++;
                    Assert.Equal(choice.Version, context.StrategyVersion);
                    // A saved previous session must never become current through rereading it.
                    if (DateTimeOffset.UtcNow >= calendar.GetSessionBounds(date).CloseUtc)
                        Assert.False(context.CanPropose);
                }
                catch (Exception ex)
                {
                    // Do not leak saved JSON, symbols, IDs, balances or connection details on failure.
                    string schema = ex is SqlException { Number: 207 or 208 } sqlError ? " " + sqlError.Message : "";
                    failures.Add($"{group.Key}: {ex.GetType().Name}{schema}\n{ex.StackTrace}");
                }
            }
            output.WriteLine($"{group.Key}: choices={group.Count()}, defined={group.Count() - undefined}, loaded={loaded}, missing={missing}, blocked={blocked}, proposable={proposable}, elapsed={timer.ElapsedMilliseconds} ms.");
            if (undefined != 0) failures.Add($"{group.Key}: {undefined} undefined installed choices.");
            if (loaded == 0) failures.Add($"{group.Key}: no full saved contexts mapped; empty reads cannot qualify this family.");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(shadowReads > 0 && liveReads > 0, "Both paper families must have saved sessions to qualify mapping.");
        output.WriteLine("PASS: installed mapping and rejection at a simulated after-close clock. Fresh feeds, cash events, concurrency and AI execution are not qualified.");
    }
}

public sealed class NativeAiReadersFactAttribute : FactAttribute
{
    public NativeAiReadersFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_RUN_AI_READ_ONLY_SQL") != "1")
            Skip = "Explicit opt-in required: SELECT-only reads of installed paper records on the configured database.";
    }
}
