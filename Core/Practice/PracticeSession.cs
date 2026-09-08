#nullable enable
using Core.Calibration;
using Core.ML.Engine.Profit;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Practice;

public sealed record PracticeAccountFrame(string Code, Guid PortfolioId, decimal Cash, decimal? NetAssetValue,
    decimal? Return, string MarkStatus, string RiskStatus, IReadOnlyList<SystemShadowPositionInfo> Positions,
    IReadOnlyList<SystemShadowPendingOrder> PendingOrders);
public sealed record PracticeCandidateFrame(string Account, string Symbol, int Rank, string State, string? ReasonCode, DateTime? LastEvaluatedUtc);
public sealed record PracticeFrame(string Label, string DatasetIdentity, DateTime SimulatedUtc, DateOnly Session,
    string DailyStatus, SystemShadowPollResult? Poll, IReadOnlyList<PracticeAccountFrame> Accounts)
{
    public IReadOnlyList<DelphiEvaluatedPick> DailyPicks { get; init; } = [];
    public IReadOnlyList<PracticeCandidateFrame> Candidates { get; init; } = [];
    public string ModelTimingNote { get; init; } = "Recorded model dates unavailable — exploratory only.";
}
public sealed record PracticeDailyResult(DateOnly Session, bool Published, string Reason,
    DateTime? MarketDataAsOf, IReadOnlyList<DelphiEvaluatedPick> Picks, DelphiPresentationSnapshot? Presentation);

/// <summary>One sequential, host-neutral playback. WPF can await StepAsync for pause/step/speed control.
/// A new instance is a fresh run, never an implicit restart with reset balances.</summary>
public sealed class PracticeSession : ISystemShadowEnvironment
{
    private readonly PracticeDataset dataset;
    private readonly SystemShadowController shadow;
    private readonly Func<DateOnly, CancellationToken, Task<DelphiWorkflowRunResult>> evaluateDaily;
    private readonly Dictionary<(string Symbol, int Interval), OhlcvBar[]> bars;
    private readonly Dictionary<Guid, IReadOnlyList<DelphiEvaluatedPick>> picks = [];
    private readonly string modelTiming;
    private SystemShadowDelphiRun? dailyRun;
    private int sessionIndex;
    private long sequence;
    private bool dailyEvaluated;
    private readonly SemaphoreSlim stepGate = new(1, 1);
    public DateTime UtcNow { get; private set; }
    public bool Completed { get; private set; }
    public bool Faulted { get; private set; }
    public PracticeShadowStore Ledger { get; }
    public List<PracticeDailyResult> DailyResults { get; } = [];
    public ISystemShadowStore Store => Ledger;
    public CodeProvenance Code => new(dataset.Document.CoreAssemblySha256, "Practice pinned Core assembly", "Exploratory");

    public PracticeSession(PracticeDataset dataset) : this(dataset, null) { }
    internal PracticeSession(PracticeDataset dataset, Func<DateOnly, CancellationToken, Task<DelphiWorkflowRunResult>>? dailyEvaluator)
    {
        this.dataset = dataset;
        UtcNow = ReviewedTsxSessionCalendar.At(dataset.Sessions[0], new(9, 0));
        Ledger = new(() => UtcNow, NewId, NewId(), dataset.Document.ShadowPolicy, dataset.Document.ShadowPolicyId);
        foreach (var policy in dataset.Document.PortfolioPolicies)
        {
            int i = Ledger.Portfolios.FindIndex(p => p.PortfolioCode == policy.PortfolioCode);
            Ledger.Portfolios[i] = Ledger.Portfolios[i] with { Settings = policy.Settings, StrategyVersionId = policy.VersionId };
        }
        shadow = new(this);
        evaluateDaily = dailyEvaluator ?? EvaluateDailyAsync;
        var models = dataset.Document.Daily.Models.Models;
        var created = models.Select(x => x.Registry.CreatedUtc).Where(d => d != default).ToArray();
        var trained = models.Select(x => x.Registry.TrainedToUtc).OfType<DateTime>().ToArray();
        modelTiming = $"Latest model creation (UTC): {(created.Length == 0 ? "unknown" : created.Max().ToString("yyyy-MM-dd"))}; " +
            $"latest recorded training end: {(trained.Length == 0 ? "unknown" : trained.Max().ToString("yyyy-MM-dd"))}. " +
            "Training-label timing remains unverified.";
        bars = dataset.Document.Intraday.GroupBy(x => (x.Symbol.ToUpperInvariant(), x.IntervalMinutes))
            .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Bars).OrderBy(x => x.TimestampUtc).ToArray());
    }

    public Guid NewId() => new(SHA256.HashData(Encoding.UTF8.GetBytes(dataset.Identity + ":" + (++sequence).ToString(System.Globalization.CultureInfo.InvariantCulture))).AsSpan(0, 16));
    public Task<IAsyncDisposable?> AcquireLeaseAsync(CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(null);
    public Task<bool> EntriesPausedAsync() => Task.FromResult(false);
    public Task<IReadOnlyList<OperatorExitRequest>> ExitRequestsAsync() => Task.FromResult<IReadOnlyList<OperatorExitRequest>>([]);
    public Task<SystemShadowRuntimePortfolio> ConfigureAsync(SystemShadowRuntimePortfolio portfolio) => Task.FromResult(portfolio);
    public Task<bool> IsCurrentDailyRunAsync(Guid? runId) => Task.FromResult(runId.HasValue && dailyRun?.RunId == runId);
    public Task<SystemShadowDelphiRun?> GetDailyRunAsync(DateTime date, DateTime cutoffUtc, CancellationToken cancellationToken) =>
        Task.FromResult(dailyRun?.RecommendationDate == date.Date && dailyRun.CreatedUtc <= cutoffUtc ? dailyRun : null);
    public Task<IReadOnlyList<SystemShadowDelphiCandidate>> GetCandidatesAsync(Guid runId, string lens, int maximumRank, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SystemShadowDelphiCandidate>>(picks.GetValueOrDefault(runId, []).Where(x => x.Lens == lens && x.IsEligible && x.Rank <= maximumRank)
            .OrderBy(x => x.Rank).Select(x => new SystemShadowDelphiCandidate(
                new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(runId.ToString("N") + ":" + x.Symbol)).AsSpan(0, 16)),
                x.Symbol, x.Rank, x.PreviousClose)).ToArray());

    public Task<TmxIntradayBatch?> CollectAsync(IntradayPollContext context, string symbol, int intervalMinutes,
        DateTime requestStartUtc, List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = Observable(symbol, intervalMinutes, requestStartUtc);
        if (available.Length == 0) warnings.Add($"{symbol} {intervalMinutes}m: no saved observation available at practice time.");
        // These times explicitly describe the assumed replay receipt, never the historical acquisition.
        return Task.FromResult<TmxIntradayBatch?>(new(symbol, intervalMinutes, requestStartUtc, UtcNow, UtcNow, UtcNow, 0, 0, available));
    }
    private OhlcvBar[] Observable(string symbol, int interval, DateTime start)
    {
        var bounds = dataset.Calendar.GetSessionBounds(dataset.Sessions[sessionIndex]);
        return bars.GetValueOrDefault((symbol.ToUpperInvariant(), interval), []).Where(x => x.TimestampUtc >= start &&
            x.TimestampUtc.AddMinutes(interval + dataset.Document.AssumedBarDelayMinutes) <= UtcNow &&
            x.TimestampUtc.AddMinutes(interval) <= bounds.CloseUtc &&
            (x.TimestampUtc - bounds.OpenUtc).Ticks % TimeSpan.FromMinutes(interval).Ticks == 0 && PracticeCoverage.IsValid(x)).ToArray();
    }

    private async Task<DelphiWorkflowRunResult> EvaluateDailyAsync(DateOnly day, CancellationToken ct)
    {
        var inputs = dataset.Document.Daily;
        var options = new DelphiWorkflowOptions(CalibrationRunPurpose.ExploratoryReplay, day.ToDateTime(TimeOnly.MinValue),
            AvailableCapital: 1000m, SaveToDatabase: false);
        // Only known market-input failures become a no-publication day. Model, code and storage failures escape.
        if (DailyBenchmarkPolicy.IsRequired(inputs.Strategy.DecisionRef))
        {
            try { DailyBenchmarkPolicy.Prepare(options.RecommendationDate!.Value, dataset.Calendar,
                inputs.Bars("XIU", options.RecommendationDate.Value), inputs.Bars("SPY", options.RecommendationDate.Value)); }
            catch (InvalidDataException ex) { return Missing(ex.Message); }
        }
        try
        {
            var result = await new DelphiWorkflow(inputs, dataset.Calendar, () => UtcNow).RunAsync(options, TextWriter.Null, ct);
            return result with { CompletedUtc = UtcNow };
        }
        catch (ProfitFeatureInputException ex) when (ex.SharedBenchmark) { return Missing(ex.Message); }
        DelphiWorkflowRunResult Missing(string reason) => DelphiWorkflowRunResult.Failed(options, UtcNow,
            options.RecommendationDate!.Value, reason) with { CompletedUtc = UtcNow };
    }

    public async Task<PracticeFrame> StepAsync(CancellationToken cancellationToken = default)
    {
        await stepGate.WaitAsync(cancellationToken);
        try { return await StepCoreAsync(cancellationToken); }
        finally { stepGate.Release(); }
    }
    private async Task<PracticeFrame> StepCoreAsync(CancellationToken cancellationToken)
    {
        if (Completed || Faulted) throw new InvalidOperationException("This playback is completed or faulted; create a new run to replay.");
        cancellationToken.ThrowIfCancellationRequested();
        var day = dataset.Sessions[sessionIndex];
        try
        {
            if (!dailyEvaluated)
            {
                var daily = await evaluateDaily(day, cancellationToken);
                if (daily.Purpose != CalibrationRunPurpose.ExploratoryReplay || daily.RecommendationDate.Date != day.ToDateTime(TimeOnly.MinValue) ||
                    daily.MarketDataAsOf.HasValue && daily.MarketDataAsOf.Value.Date >= daily.RecommendationDate.Date)
                    throw new InvalidOperationException("Daily result violates the practice source cutoff.");
                dailyEvaluated = true;
                dailyRun = daily.Succeeded && daily.MarketDataAsOf.HasValue
                    ? new(NewId(), daily.RecommendationDate, daily.MarketDataAsOf.Value, UtcNow) : null;
                if (dailyRun is not null) picks.Add(dailyRun.RunId, daily.EvaluatedPicks);
                DailyResults.Add(new(day, dailyRun is not null, daily.Status, daily.MarketDataAsOf, daily.EvaluatedPicks, daily.Presentation));
                var frame = Frame(day, null);
                UtcNow = dataset.Calendar.GetSessionBounds(day).OpenUtc;
                return frame;
            }
            var poll = await shadow.PollOnceAsync(cancellationToken);
            var current = Frame(day, poll);
            if (UtcNow >= dataset.Calendar.GetSessionBounds(day).CloseUtc.AddMinutes(5))
            {
                if (++sessionIndex == dataset.Sessions.Count) Completed = true;
                else
                {
                    dailyEvaluated = false;
                    dailyRun = null;
                    UtcNow = ReviewedTsxSessionCalendar.At(dataset.Sessions[sessionIndex], new(9, 0));
                }
            }
            else UtcNow = UtcNow.AddMinutes(1);
            return current;
        }
        catch { Faulted = true; throw; }
    }

    private PracticeFrame Frame(DateOnly day, SystemShadowPollResult? poll)
    {
        var bounds = dataset.Calendar.GetSessionBounds(day);
        var accounts = Ledger.Portfolios.Select(p =>
        {
            var open = Ledger.Positions.Where(x => x.PortfolioId == p.PortfolioId && x.Status == "Open").ToArray();
            decimal? value = p.CashBalance;
            DateTime lastAvailableEnd = UtcNow.AddMinutes(-dataset.Document.AssumedBarDelayMinutes);
            if (lastAvailableEnd > bounds.CloseUtc) lastAvailableEnd = bounds.CloseUtc;
            DateTime expected = bounds.OpenUtc.AddMinutes(System.Math.Floor((lastAvailableEnd - bounds.OpenUtc).TotalMinutes / 5) * 5 - 5);
            foreach (var position in open)
            {
                var mark = Observable(position.Symbol, 5, bounds.OpenUtc).LastOrDefault();
                if (mark is null || mark.TimestampUtc != expected) { value = null; break; }
                value += position.Shares * mark.Close;
            }
            return new PracticeAccountFrame(p.PortfolioCode, p.PortfolioId, p.CashBalance, value, value / 1000m - 1m,
                value.HasValue ? "Observed completed bars" : "Unavailable — missing or stale mark", p.Status, open,
                Ledger.PendingOrders.Where(x => x.PortfolioId == p.PortfolioId).ToArray());
        }).ToArray();
        var sessionIds = Ledger.Sessions.Where(x => x.TradingDate == day.ToDateTime(TimeOnly.MinValue)).Select(x => x.SessionId).ToHashSet();
        return new("Practice / Exploratory", dataset.Identity, UtcNow, day, DailyResults.Last().Reason, poll, accounts)
        { ModelTimingNote = modelTiming, DailyPicks = DailyResults.Last().Picks, Candidates = Ledger.Candidates.Where(x => sessionIds.Contains(x.SessionId)).Select(x =>
            new PracticeCandidateFrame(Ledger.Portfolios.Single(p => p.PortfolioId == Ledger.Sessions.Single(s => s.SessionId == x.SessionId).PortfolioId).PortfolioCode,
                x.Symbol, x.Rank, x.State, x.ReasonCode, x.LastEvaluatedUtc)).ToArray() };
    }

    public async Task RunToDirectoryAsync(string destination, CancellationToken cancellationToken = default)
    {
        if (DailyResults.Count != 0 || Completed || Faulted) throw new InvalidOperationException("Directory playback must start with a fresh session.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Use a new directory for each practice run.");
        Directory.CreateDirectory(destination);
        var compact = new JsonSerializerOptions(PracticeDataset.Json) { WriteIndented = false };
        await File.WriteAllTextAsync(Path.Combine(destination, "coverage.json"), JsonSerializer.Serialize(PracticeCoverage.Inspect(dataset), PracticeDataset.Json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(destination, "run.json"), JsonSerializer.Serialize(new
        {
            Label = "Practice / Exploratory", DatasetIdentity = dataset.Identity, dataset.Document.FirstSession,
            dataset.Document.LastSession, dataset.Document.Provenance, dataset.Document.CoreAssemblySha256,
            dataset.Document.ShadowPolicyId, dataset.Document.ShadowPolicy, dataset.Document.PortfolioPolicies, StartingCashPerAccount = 1000m, Currency = "CAD",
            Execution = "SharedShadowNextCompletedFiveMinuteOpenWithFrictionV1", dataset.Document.AssumedBarDelayMinutes,
            ModelEvidence = "Today's preserved models; training may postdate replay or be unverified; not forward/promotion evidence.",
            Models = dataset.Document.Daily.Models.Models.Select(x => new { x.Registry.ModelId, x.ArtifactSha256,
                x.Registry.CreatedUtc, x.Registry.TrainedFromUtc, x.Registry.TrainedToUtc }),
            Recovery = "A failed run retains its last session-boundary checkpoint, completed frames and partial failure trace. Restart explicitly as a fresh run."
        }, PracticeDataset.Json), cancellationToken);
        await using var frames = new StreamWriter(Path.Combine(destination, "frames.ndjson"), false, Encoding.UTF8);
        var performance = new PracticePerformance();
        try
        {
            while (!Completed)
            {
                var frame = await StepAsync(cancellationToken);
                performance.Observe(frame);
                await frames.WriteLineAsync(JsonSerializer.Serialize(frame, compact));
                await frames.FlushAsync(cancellationToken);
                // Frames are flushed each tick; full inspection checkpoints are bounded to session boundaries.
                if (frame.Poll is null || frame.SimulatedUtc >= dataset.Calendar.GetSessionBounds(frame.Session).CloseUtc.AddMinutes(5))
                {
                    string checkpoint = Path.Combine(destination, "checkpoint.json");
                    await File.WriteAllTextAsync(checkpoint + ".tmp", JsonSerializer.Serialize(new
                    { Frame = frame, Completed, Sequence = sequence, Ledger.Portfolios, Ledger.Sessions, Ledger.Candidates,
                        Ledger.Positions, Ledger.PendingOrders, Ledger.Fills, Ledger.Events, DailyResults }, compact), cancellationToken);
                    File.Move(checkpoint + ".tmp", checkpoint, true);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(destination, "summary.json"), JsonSerializer.Serialize(new
            { Label = "Practice / Exploratory", DatasetIdentity = dataset.Identity, Sessions = dataset.Sessions.Count,
                DailyPublications = DailyResults.Count(x => x.Published), Accounts = performance.Summarize(Ledger),
                Notes = "No forced liquidation. Exposure averages observed intraday frames; missing frames are counted. Drawdown is unavailable if any held valuation is missing. No operational reliability or forward-performance claim." }, PracticeDataset.Json), cancellationToken);
        }
        catch (Exception ex)
        {
            Faulted = true;
            Completed = false;
            await File.WriteAllTextAsync(Path.Combine(destination, "failure.json"), JsonSerializer.Serialize(new
            { Label = "Practice / Exploratory", UtcNow, Error = ex.GetType().FullName, ex.Message,
                Ledger.Portfolios, Ledger.Positions, Ledger.PendingOrders, Ledger.Events }, PracticeDataset.Json), CancellationToken.None);
            throw;
        }
    }
}
