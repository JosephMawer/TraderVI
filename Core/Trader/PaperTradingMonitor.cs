#nullable enable

using Core.Calibration;
using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

public sealed record PaperPositionMonitorResult(
    Guid PositionId,
    string Symbol,
    decimal EntryPrice,
    decimal? ObservedPrice,
    double? UnrealizedPnLPct,
    DateTime? LastPolicyBarEndUtc,
    decimal? TrailingStopPrice,
    IntradaySwingDirective? Directive,
    IntradaySwingReason? Reason,
    TimeSpan? DataAge,
    IntradayPollAuditState? FifteenMinuteAuditState,
    IntradayPollAuditState? FiveMinuteAuditState,
    bool ExitExecuted,
    decimal? ExitPrice,
    string? ErrorCode,
    DelayedIntradayBreakoutEvidence? FreshBreakoutEvidence,
    string? WarningCode);

public sealed record PaperMonitorCycleResult(
    Guid PollCycleId,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    bool AutomaticGhostExitsEnabled,
    IReadOnlyList<PaperPositionMonitorResult> Positions,
    string? BenchmarkWarningCode);

/// <summary>
/// Shared ADR-0030/0031 paper monitor used by both the console and WPF hosts.
/// It persists each source receipt before exposing a decision and can execute
/// ghost-only exits at a separately observed post-detection price.
/// </summary>
public sealed class PaperTradingMonitor
{
    public const int SourceIntervalMinutes = 5;
    public const int PolicyIntervalMinutes = 15;

    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    public async Task<PaperMonitorCycleResult> PollOnceAsync(
        bool executeGhostExits,
        CancellationToken cancellationToken = default)
    {
        await _pollGate.WaitAsync(cancellationToken);
        try
        {
            DateTime startedUtc = DateTime.UtcNow;
            Guid pollCycleId = Guid.NewGuid();
            var evidenceRepository = new IntradayEvidenceRepository();
            if (!await evidenceRepository.HasSchemaAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The ADR-0030 intraday evidence schema is not installed.");
            }

            List<ActivePositionInfo> positions =
                (await new ActivePositionRepository().GetActivePositions())
                .Where(position => position.OriginalPickId.HasValue)
                .OrderBy(position => position.Symbol)
                .ToList();

            CodeProvenance code = CalibrationProvenance.ResolveCode();
            var context = new IntradayPollContext(
                pollCycleId,
                IntradayPollPurpose.PaperMonitor,
                IntradayEvidenceVersions.Collector,
                IntradayEvidenceVersions.Policy,
                code);
            var results = new List<PaperPositionMonitorResult>();
            using var tmx = new TmxClient();

            string? benchmarkWarningCode = positions.Any(position =>
                    string.Equals(position.Symbol, "XIU", StringComparison.OrdinalIgnoreCase))
                ? null
                : await CollectBenchmarkAsync(
                    context,
                    tmx,
                    evidenceRepository,
                    cancellationToken);

            foreach (ActivePositionInfo position in positions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await PollPositionAsync(
                    position,
                    context,
                    tmx,
                    evidenceRepository,
                    executeGhostExits,
                    cancellationToken));
            }

            return new PaperMonitorCycleResult(
                pollCycleId,
                startedUtc,
                DateTime.UtcNow,
                executeGhostExits,
                results.AsReadOnly(),
                benchmarkWarningCode);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private static async Task<string?> CollectBenchmarkAsync(
        IntradayPollContext context,
        TmxClient tmx,
        IntradayEvidenceRepository evidenceRepository,
        CancellationToken cancellationToken)
    {
        const string symbol = "XIU";
        DateTime requestStartUtc = ToUtc(
            ToToronto(DateTime.UtcNow).Date.AddHours(9).AddMinutes(30));

        foreach (int intervalMinutes in new[] { PolicyIntervalMinutes, SourceIntervalMinutes })
        {
            DateTime requestEndUtc = DateTime.UtcNow;
            try
            {
                TmxIntradayBatch batch = await GetBatchAsync(
                    tmx,
                    symbol,
                    intervalMinutes,
                    requestStartUtc,
                    requestEndUtc,
                    cancellationToken);
                IntradayEvidenceAppendResult append =
                    await evidenceRepository.AppendCompletedBatchAsync(
                        context,
                        batch,
                        cancellationToken);
                if (append.AuditState == IntradayPollAuditState.Invalid)
                    return append.AuditCode ?? $"InvalidXiu{intervalMinutes}Evidence";
            }
            catch
            {
                await TryAppendFailureAsync(
                    evidenceRepository,
                    context,
                    symbol,
                    intervalMinutes,
                    requestStartUtc,
                    requestEndUtc,
                    $"TmxXiu{intervalMinutes}FetchOrPersistFailed",
                    cancellationToken);
                return $"TmxXiu{intervalMinutes}FetchOrPersistFailed";
            }
        }

        return null;
    }

    private static async Task<PaperPositionMonitorResult> PollPositionAsync(
        ActivePositionInfo position,
        IntradayPollContext context,
        TmxClient tmx,
        IntradayEvidenceRepository evidenceRepository,
        bool executeGhostExits,
        CancellationToken cancellationToken)
    {
        TradeLogInfo entryTrade = await GetEntryTradeAsync(position.Symbol);
        DateTime entryUtc = DateTime.SpecifyKind(entryTrade.CreatedUtc, DateTimeKind.Utc);
        DateTime requestStartUtc = ToUtc(
            ToToronto(entryUtc).Date.AddHours(9).AddMinutes(30));
        DateTime requestEndUtc = DateTime.UtcNow;
        TmxIntradayBatch policyBatch;

        try
        {
            policyBatch = await GetBatchAsync(
                tmx,
                position.Symbol,
                PolicyIntervalMinutes,
                requestStartUtc,
                requestEndUtc,
                cancellationToken);
        }
        catch
        {
            await TryAppendFailureAsync(
                evidenceRepository,
                context,
                position.Symbol,
                PolicyIntervalMinutes,
                requestStartUtc,
                requestEndUtc,
                "Tmx15FetchFailed",
                cancellationToken);
            return Error(position, "Tmx15FetchFailed");
        }

        IntradayEvidenceAppendResult policyAppend;
        try
        {
            policyAppend = await evidenceRepository.AppendCompletedBatchAsync(
                context,
                policyBatch,
                cancellationToken);
        }
        catch
        {
            return Error(position, "Persist15Failed");
        }
        if (policyAppend.AuditState == IntradayPollAuditState.Invalid)
            return Error(position, policyAppend.AuditCode ?? "Invalid15Evidence");

        IReadOnlyList<OhlcvBar> completedPolicyEvidence = policyBatch.Bars
            .Where(bar =>
                bar.TimestampUtc.AddMinutes(PolicyIntervalMinutes) <= policyBatch.ReceivedUtc)
            .ToList()
            .AsReadOnly();
        IReadOnlyList<DelayedIntradayBar> policyBars =
            CompletedIntradayBarAggregator.BuildPolicyBars(
                completedPolicyEvidence,
                policyBatch.ReceivedUtc,
                entryUtc);
        IReadOnlyList<FreshDelphiBreakoutEvidenceSnapshot> breakoutTimeline =
            Array.Empty<FreshDelphiBreakoutEvidenceSnapshot>();
        string? evidenceWarningCode = null;
        if (policyBars.Count > 0)
        {
            try
            {
                breakoutTimeline = await new CalibrationEvidenceRepository()
                    .GetValidOfficialBreakoutTimelineAsync(
                        position.Symbol,
                        entryUtc,
                        policyBars[^1].StartUtc,
                        cancellationToken);
            }
            catch
            {
                // Missing evidence can never grant the exception. Continue with
                // the normal loss limits and surface the bounded diagnostic.
                evidenceWarningCode = "FreshDelphiEvidenceReadFailed";
            }
        }

        IntradaySwingPositionState state =
            IntradaySwingPositionState.Open(position.EntryPrice, entryUtc);
        IntradaySwingDecision? decision = null;
        DelayedIntradayBreakoutEvidence? decisionEvidence = null;
        foreach (DelayedIntradayBar bar in policyBars)
        {
            decisionEvidence = FreshDelphiBreakoutEvidenceResolver.Resolve(
                breakoutTimeline,
                entryUtc,
                bar.StartUtc);
            decision = DelayedIntradaySwingExitPolicy.Evaluate(
                state,
                bar,
                decisionEvidence);
            state = decision.State;
            if (decision.Directive == IntradaySwingDirective.ExitAlert)
                break;
        }

        // This request is intentionally after policy evaluation. If an alert was
        // detected, its newest returned price is a post-detection ghost fill.
        TmxIntradayBatch fiveMinuteBatch;
        try
        {
            fiveMinuteBatch = await GetBatchAsync(
                tmx,
                position.Symbol,
                SourceIntervalMinutes,
                requestStartUtc,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch
        {
            await TryAppendFailureAsync(
                evidenceRepository,
                context,
                position.Symbol,
                SourceIntervalMinutes,
                requestStartUtc,
                DateTime.UtcNow,
                "Tmx5FetchFailed",
                cancellationToken);
            return Result(
                position,
                null,
                state,
                decision,
                policyAppend.AuditState,
                null,
                false,
                null,
                "Tmx5FetchFailed",
                decisionEvidence,
                evidenceWarningCode);
        }

        IntradayEvidenceAppendResult fiveAppend;
        try
        {
            fiveAppend = await evidenceRepository.AppendCompletedBatchAsync(
                context,
                fiveMinuteBatch,
                cancellationToken);
        }
        catch
        {
            return Result(
                position,
                null,
                state,
                decision,
                policyAppend.AuditState,
                null,
                false,
                null,
                "Persist5Failed",
                decisionEvidence,
                evidenceWarningCode);
        }
        if (fiveAppend.AuditState == IntradayPollAuditState.Invalid)
        {
            return Result(
                position,
                null,
                state,
                decision,
                policyAppend.AuditState,
                fiveAppend.AuditState,
                false,
                null,
                fiveAppend.AuditCode ?? "Invalid5Evidence",
                decisionEvidence,
                evidenceWarningCode);
        }

        OhlcvBar? observed = fiveMinuteBatch.Bars.LastOrDefault();
        if (observed is null)
        {
            return Result(
                position,
                null,
                state,
                decision,
                policyAppend.AuditState,
                fiveAppend.AuditState,
                false,
                null,
                "NoObservedPrice",
                decisionEvidence,
                evidenceWarningCode);
        }

        await UpdatePositionSnapshotAsync(position, observed.Close, state);

        bool exited = false;
        decimal? exitPrice = null;
        if (ShouldExecuteAutomaticExit(
                position.ExecutionMode,
                executeGhostExits,
                decision?.Directive))
        {
            IntradaySwingDecision exitDecision = decision!;
            bool wasForming =
                observed.TimestampUtc.AddMinutes(SourceIntervalMinutes) > fiveMinuteBatch.ReceivedUtc;
            string notes =
                $"ADR-0031 auto ghost exit; trigger={exitDecision.Reason}; " +
                $"TMX5 eventUtc={observed.TimestampUtc:O}; " +
                $"receivedUtc={fiveMinuteBatch.ReceivedUtc:O}; " +
                $"sourceState={(wasForming ? "forming" : "complete")}; " +
                $"freshDelphi={FormatEvidence(decisionEvidence)}; " +
                "delayed observed price, not guaranteed fill";
            exited = await new TradeManager(ghost: true).Sell(
                position.Symbol,
                observed.Close,
                notes,
                $"Policy {exitDecision.Reason}");
            if (exited)
                exitPrice = observed.Close;
        }

        return Result(
            position,
            observed.Close,
            state,
            decision,
            policyAppend.AuditState,
            fiveAppend.AuditState,
            exited,
            exitPrice,
            null,
            decisionEvidence,
            evidenceWarningCode);
    }

    public static bool ShouldExecuteAutomaticExit(
        TrackedExecutionMode executionMode,
        bool automaticGhostExitsEnabled,
        IntradaySwingDirective? directive) =>
        automaticGhostExitsEnabled &&
        executionMode.AllowsAutomaticExit() &&
        directive == IntradaySwingDirective.ExitAlert;

    private static async Task<TmxIntradayBatch> GetBatchAsync(
        TmxClient tmx,
        string symbol,
        int intervalMinutes,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken) =>
        endUtc - startUtc > TimeSpan.FromDays(5)
            ? await tmx.GetIntradayTimeSeriesChunkedAsync(
                symbol,
                intervalMinutes,
                startUtc,
                endUtc,
                cancellationToken)
            : await tmx.GetIntradayTimeSeriesBatchAsync(
                symbol,
                intervalMinutes,
                startUtc,
                endUtc,
                cancellationToken);

    private static async Task UpdatePositionSnapshotAsync(
        ActivePositionInfo position,
        decimal observedPrice,
        IntradaySwingPositionState state)
    {
        decimal currentValue = decimal.Round(observedPrice * position.Shares, 2);
        decimal unrealized = decimal.Round(currentValue - position.CostBasis, 2);
        double unrealizedPercent = position.CostBasis == 0m
            ? 0d
            : (double)(unrealized / position.CostBasis);
        decimal highWater = state.HighestCompletedClose;
        double drawdown = highWater == 0m
            ? 0d
            : (double)(observedPrice / highWater - 1m);
        await new ActivePositionRepository().UpdatePositionPrices(
            position.PositionId,
            observedPrice,
            currentValue,
            unrealized,
            unrealizedPercent,
            highWater,
            drawdown,
            state.LastTradingSessionOrdinal);
    }

    private static async Task<TradeLogInfo> GetEntryTradeAsync(string symbol)
    {
        List<TradeLogInfo> trades = await new TradeLogRepository()
            .GetTradesBySymbol(symbol);
        return trades
            .Where(trade =>
                string.Equals(trade.TradeType, "BUY", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.CreatedUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No BUY trade exists for active position {symbol}.");
    }

    private static async Task TryAppendFailureAsync(
        IntradayEvidenceRepository repository,
        IntradayPollContext context,
        string symbol,
        int intervalMinutes,
        DateTime requestStartUtc,
        DateTime requestEndUtc,
        string auditCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AppendFailedObservationAsync(
                context,
                symbol,
                intervalMinutes,
                requestStartUtc,
                requestEndUtc,
                DateTime.UtcNow,
                1,
                1,
                auditCode,
                cancellationToken);
        }
        catch
        {
            // The original source failure remains the bounded result. A duplicate
            // monitor or database failure can prevent its secondary audit insert.
        }
    }

    private static PaperPositionMonitorResult Error(
        ActivePositionInfo position,
        string errorCode) =>
        new(
            position.PositionId,
            position.Symbol,
            position.EntryPrice,
            position.CurrentPrice,
            position.UnrealizedPnLPct,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            errorCode,
            null,
            null);

    private static PaperPositionMonitorResult Result(
        ActivePositionInfo position,
        decimal? observedPrice,
        IntradaySwingPositionState state,
        IntradaySwingDecision? decision,
        IntradayPollAuditState? fifteenAudit,
        IntradayPollAuditState? fiveAudit,
        bool exitExecuted,
        decimal? exitPrice,
        string? errorCode,
        DelayedIntradayBreakoutEvidence? freshBreakoutEvidence = null,
        string? warningCode = null)
    {
        double? unrealizedPercent = observedPrice.HasValue && position.CostBasis != 0m
            ? (double)((decimal.Round(observedPrice.Value * position.Shares, 2) -
                        position.CostBasis) / position.CostBasis)
            : position.UnrealizedPnLPct;
        return new PaperPositionMonitorResult(
            position.PositionId,
            position.Symbol,
            position.EntryPrice,
            observedPrice,
            unrealizedPercent,
            state.LastProcessedBarEndUtc,
            state.TrailingStopPrice,
            decision?.Directive,
            decision?.Reason,
            decision?.DataAge,
            fifteenAudit,
            fiveAudit,
            exitExecuted,
            exitPrice,
            errorCode,
            freshBreakoutEvidence,
            warningCode);
    }

    private static string FormatEvidence(DelayedIntradayBreakoutEvidence? evidence) =>
        evidence is null
            ? "none"
            : $"run={evidence.RunId}; availableUtc={evidence.AvailableUtc:O}; " +
              $"published={evidence.IsBreakoutPublished}; " +
              $"breakout={evidence.BreakoutProbability?.ToString("0.000") ?? "null"}; " +
              $"edge={evidence.DirectionEdge?.ToString("0.000") ?? "null"}; " +
              $"down={evidence.DownProbability?.ToString("0.000") ?? "null"}";

    public static DateTime NextScheduledPollLocal(DateTime localNow)
    {
        DateTime hour = new(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            localNow.Hour,
            0,
            0,
            DateTimeKind.Unspecified);
        DateTime candidate = hour.AddMinutes(2);
        while (candidate <= localNow)
            candidate = candidate.AddMinutes(PolicyIntervalMinutes);

        DateTime firstRegularPoll = localNow.Date.AddHours(9).AddMinutes(47);
        return candidate < firstRegularPoll ? firstRegularPoll : candidate;
    }

    public static bool IsAutomaticPollTime(DateTime localNow) =>
        localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
        localNow.TimeOfDay >= new TimeSpan(9, 47, 0) &&
        localNow.TimeOfDay <= new TimeSpan(16, 2, 59);

    public static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TorontoTimeZone);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            TorontoTimeZone);
}
