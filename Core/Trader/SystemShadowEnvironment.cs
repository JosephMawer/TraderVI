#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Runtime;
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

public interface ISystemShadowEnvironment
{
    DateTime UtcNow { get; }
    Guid NewId();
    CodeProvenance Code { get; }
    ISystemShadowStore Store { get; }
    Task<IAsyncDisposable?> AcquireLeaseAsync(CancellationToken cancellationToken);
    Task<bool> EntriesPausedAsync();
    Task<IReadOnlyList<OperatorExitRequest>> ExitRequestsAsync();
    Task<SystemShadowRuntimePortfolio> ConfigureAsync(SystemShadowRuntimePortfolio portfolio);
    Task<bool> IsCurrentDailyRunAsync(Guid? runId);
    Task<SystemShadowDelphiRun?> GetDailyRunAsync(DateTime date, DateTime cutoffUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<SystemShadowDelphiCandidate>> GetCandidatesAsync(Guid runId, string lens, int maximumRank, CancellationToken cancellationToken);
    Task<TmxIntradayBatch?> CollectAsync(IntradayPollContext context, string symbol, int intervalMinutes,
        DateTime requestStartUtc, List<string> warnings, CancellationToken cancellationToken);
}

internal sealed class OperationalSystemShadowEnvironment : ISystemShadowEnvironment
{
    public DateTime UtcNow => DateTime.UtcNow;
    public Guid NewId() => Guid.NewGuid();
    public CodeProvenance Code => CalibrationProvenance.ResolveCode();
    public ISystemShadowStore Store { get; } = new SystemShadowRepository();
    private TmxClient? client;
    public async Task<IAsyncDisposable?> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        var lease = await EngineSettingsLease.AcquireAsync(cancellationToken);
        return new PollLease(this, lease);
    }
    private sealed class PollLease(OperationalSystemShadowEnvironment owner, IAsyncDisposable lease) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            owner.client?.Dispose();
            owner.client = null;
            await lease.DisposeAsync();
        }
    }
    public Task<bool> EntriesPausedAsync() => new TradingControlRepository().IsPausedAsync(EngineStrategySettings.Shadow);
    public Task<IReadOnlyList<OperatorExitRequest>> ExitRequestsAsync() => new TradingControlRepository().ExitRequestsAsync(EngineStrategySettings.Shadow);
    public Task<bool> IsCurrentDailyRunAsync(Guid? runId) => new TradingControlRepository().IsCurrentDailyRunAsync(runId);
    public async Task<SystemShadowRuntimePortfolio> ConfigureAsync(SystemShadowRuntimePortfolio portfolio)
    {
        var assignment = await new EngineSettingsRepository().ReadAsync(portfolio.PortfolioId,
            EngineStrategySettings.Shadow, SystemShadowPolicyConfig.Version1);
        return portfolio with { Settings = assignment.Settings, SettingsAssignedUtc = assignment.AssignedUtc, StrategyVersionId = assignment.VersionId };
    }
    public Task<SystemShadowDelphiRun?> GetDailyRunAsync(DateTime date, DateTime cutoffUtc, CancellationToken cancellationToken) =>
        new CalibrationEvidenceRepository().GetLatestValidOfficialRunAsync(date, cutoffUtc, cancellationToken);
    public Task<IReadOnlyList<SystemShadowDelphiCandidate>> GetCandidatesAsync(Guid runId, string lens, int maximumRank, CancellationToken cancellationToken) =>
        new CalibrationEvidenceRepository().GetPublishedCandidatesAsync(runId, lens, maximumRank, cancellationToken);
    public async Task<TmxIntradayBatch?> CollectAsync(
        IntradayPollContext context,
        string symbol,
        int intervalMinutes,
        DateTime requestStartUtc,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var tmx = client ??= new TmxClient();
        var repository = new IntradayEvidenceRepository();
        DateTime requestEndUtc = DateTime.UtcNow;
        try
        {
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                symbol,
                intervalMinutes,
                requestStartUtc,
                requestEndUtc,
                cancellationToken);
            IntradayEvidenceAppendResult append = await repository.AppendCompletedBatchAsync(
                context,
                batch,
                cancellationToken);
            if (append.AuditState == IntradayPollAuditState.Invalid)
            {
                warnings.Add($"{symbol} {intervalMinutes}m evidence invalid: {append.AuditCode}");
                return null;
            }
            return batch;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{symbol} {intervalMinutes}m unavailable: {ex.GetType().Name}");
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
                    $"Shadow{intervalMinutes}FetchOrPersistFailed",
                    cancellationToken);
            }
            catch
            {
                // The source error remains the bounded result when even its
                // secondary durable audit record cannot be written.
            }
            return null;
        }
    }

}
