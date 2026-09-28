#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>Opt-in SQL reads and in-memory risk calculations. No journal, SQL, order or account writes.</summary>
public sealed class AiRiskReadersSqlTests(ITestOutputHelper output)
{
    [NativeAiRiskReadersFact]
    [Trait("Category", "NativeSqlReadOnly")]
    public async Task SavedPaperBooksReconcileAndRiskRejectsStaleOrAlteredEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var calendar = ReviewedTsxSessionCalendar.Load(
            Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar is required."));
        var age = TimeSpan.FromMinutes(10); // Diagnostic only; not an approved production freshness setting.
        var policy = new AiPortfolioRiskPolicy(calendar);
        var repository = new AiPortfolioCatalogRepository(age);
        var catalogue = await repository.ReadAsync(ct);
        var failures = new List<string>();
        await using var connection = new SqlConnection(repository.ConnectionString);
        await connection.OpenAsync(ct);
        foreach (var family in catalogue.Entries.GroupBy(e => e.EngineFamily))
        {
            int mapped = 0, unavailable = 0, valued = 0, invested = 0, stale = 0, current = 0, heldMissing = 0, cashVerified = 0;
            var timer = Stopwatch.StartNew();
            foreach (var choice in family)
            {
                try
                {
                    bool shadow = choice.EngineFamily == "DailyShadow";
                    var stored = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(shadow
                        ? "SELECT MAX(TradingDate) FROM dbo.ShadowPortfolioSession WHERE PortfolioId=@Id;"
                        : "SELECT TRY_CONVERT(date,JSON_VALUE(SnapshotJson,'$.currentSession')) FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Id;",
                        new { Id = choice.PaperAccountId }, cancellationToken: ct));
                    if (stored is null) { unavailable++; continue; }
                    var date = DateOnly.FromDateTime(stored.Value);
                    PortfolioRiskValuation value;
                    if (shadow)
                    {
                        var observation = await new SystemShadowRepository().ReadRiskObservationAsync(choice.PaperAccountId, date, ct);
                        if (observation is null)
                        {
                            unavailable++;
                            int held = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                                "SELECT COUNT(*) FROM dbo.ShadowPosition WHERE PortfolioId=@Id AND Status=N'Open';",
                                new { Id = choice.PaperAccountId }, cancellationToken: ct));
                            if (held > 0) heldMissing++;
                            continue;
                        }
                        mapped++;
                        var now = DateTimeOffset.UtcNow;
                        value = SystemShadowAiPortfolioRiskMonitor.Qualify(choice.PaperAccountId, observation, null, age, now);
                        var open = observation.Positions.Where(p => p.Status == "Open").ToArray();
                        if (open.Length > 0) invested++;
                        if (observation.CashVerifiedUtc is { } verified)
                        {
                            Assert.True(open.Length == 0 && verified == observation.At && verified == observation.ObservedUtc,
                                "Cash verification must identify the current whole cash book.");
                            cashVerified++;
                        }
                        else Assert.True(open.Length > 0 || date != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)),
                            "Today's cash-only book is missing its transaction verification time.");
                        // Independently total the saved holding marks; the adapter uses actual matching source bars.
                        decimal expected = observation.Portfolio.CashBalance + open.Sum(p => p.Shares * p.LastPrice);
                        Assert.True(value.Value == expected, "Shadow risk value differs from its saved whole book.");
                        Assert.True(observation.Portfolio.StrategyVersionId == choice.Strategy!.PolicyVersionId,
                            "Shadow risk and decision readers use different policy identities.");
                        Assert.Throws<InvalidOperationException>(() => SystemShadowAiPortfolioRiskMonitor.Qualify(
                            Guid.NewGuid(), observation, null, age, now));
                        Assert.Throws<InvalidOperationException>(() => SystemShadowAiPortfolioRiskMonitor.Qualify(
                            choice.PaperAccountId, observation with { Portfolio = observation.Portfolio with
                                { CashBalance = observation.Portfolio.CashBalance + 0.01m } }, null, age, now));
                        if (open.Length > 0)
                            Assert.Throws<InvalidOperationException>(() => SystemShadowAiPortfolioRiskMonitor.Qualify(
                                choice.PaperAccountId, observation with { Prices = [] }, null, age, now));
                    }
                    else
                    {
                        var snapshot = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(choice.PaperAccountId, ct);
                        Assert.NotNull(snapshot);
                        mapped++;
                        var now = DateTimeOffset.UtcNow;
                        var mark = snapshot!.Marks.LastOrDefault();
                        if (mark is null || !mark.Complete || mark.Nav is null ||
                            snapshot.Fills.Any(f => f.FilledUtc > mark.BarEndUtc))
                        {
                            Assert.Throws<InvalidOperationException>(() => DelphiLiveAiPortfolioRiskMonitor.Qualify(
                                choice.PaperAccountId, snapshot, null, age, now));
                            unavailable++;
                            continue;
                        }
                        value = DelphiLiveAiPortfolioRiskMonitor.Qualify(choice.PaperAccountId, snapshot, null, age, now);
                        if (snapshot.OpenPositions.Any()) invested++;
                        decimal expected = snapshot.Cash + mark.Positions.Sum(p => p.Quantity * p.Price);
                        Assert.True(value.Value == expected && value.Value == mark.Nav, "Live risk value differs from its saved whole book.");
                        Assert.Throws<InvalidOperationException>(() => DelphiLiveAiPortfolioRiskMonitor.Qualify(
                            Guid.NewGuid(), snapshot, null, age, now));
                        Assert.Throws<InvalidOperationException>(() => DelphiLiveAiPortfolioRiskMonitor.Qualify(
                            choice.PaperAccountId, snapshot with { Cash = snapshot.Cash + 0.01m }, null, age, now));
                        Assert.Throws<InvalidOperationException>(() => DelphiLiveAiPortfolioRiskMonitor.Qualify(
                            choice.PaperAccountId, snapshot with { Marks = snapshot.Marks.SetItem(snapshot.Marks.Length - 1,
                                mark with { Complete = false }) }, null, age, now));
                    }
                    valued++;
                    Assert.True(value.PortfolioId == choice.PaperAccountId, "Valuation belongs to another account.");
                    Assert.NotNull(value.Source);
                    var bounds = calendar.GetSessionBounds(value.TradingDate);
                    var checkTime = DateTimeOffset.UtcNow;
                    bool usable = value.At <= checkTime && checkTime <= value.ValidUntil &&
                        value.At.UtcDateTime >= bounds.OpenUtc && value.At.UtcDateTime <= bounds.CloseUtc;
                    if (usable)
                    {
                        var state = policy.Start(value, checkTime); // In-memory only; never initializes a real journal.
                        Assert.True(state.RoundPeak == value.Value, "Risk starts from a different portfolio value.");
                        Assert.True(policy.Assess(state, value.ValidUntil.AddTicks(1), true, []).NewBuyAllowanceMultiplier == 0m,
                            "Expired evidence grants buying allowance.");
                        current++;
                    }
                    else
                    {
                        Assert.Throws<InvalidOperationException>(() => policy.Start(value, checkTime));
                        stale++;
                    }
                    Assert.Throws<InvalidOperationException>(() => policy.Start(value, value.ValidUntil.AddTicks(1)));
                }
                catch (Exception ex)
                {
                    // Values, IDs and serialized financial evidence never go to test output.
                    failures.Add($"{family.Key}: {ex.GetType().Name}\n{ex.StackTrace}");
                }
            }
            output.WriteLine($"{family.Key}: choices={family.Count()}, mapped={mapped}, reconciled={valued}, invested={invested}, verified current cash books={cashVerified}, unavailable={unavailable}, unavailable with holdings={heldMissing}, within diagnostic age/session={current}, expired/outside session={stale}, elapsed={timer.ElapsedMilliseconds} ms.");
            if (valued == 0) failures.Add($"{family.Key}: no saved values reconciled; empty reads cannot qualify the family.");
        }
        Assert.NotEmpty(catalogue.Entries);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        output.WriteLine("PASS: saved-book arithmetic and in-memory risk checks; unavailable books remain blocked. No production risk state was initialized. Feed quality, one-minute freshness, cash events and concurrent-load behavior are not qualified.");
    }
}

public sealed class NativeAiRiskReadersFactAttribute : FactAttribute
{
    public NativeAiRiskReadersFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_RUN_AI_RISK_READ_ONLY_SQL") != "1")
            Skip = "Explicit opt-in required: SELECT-only portfolio reads and in-memory risk calculations.";
    }
}
