#nullable enable

using Core.Db;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.DataQuality;

public interface IMarketDataAuditSnapshotSource
{
    Task<MarketDataAuditSnapshot> LoadAsync(
        CancellationToken cancellationToken = default);
}

public sealed record MarketDataAuditRunResult(
    DateTime StartedUtc,
    DateTime CompletedUtc,
    MarketDataAuditOptions Options,
    MarketDataAuditReport Report);

/// <summary>
/// Host-neutral, read-only Data Audit workflow shared by console and GUI hosts.
/// Presentation, exit codes, and user interaction remain host responsibilities.
/// </summary>
public sealed class MarketDataAuditWorkflow
{
    private readonly IMarketDataAuditSnapshotSource _snapshotSource;

    public MarketDataAuditWorkflow(string? connectionString = null)
        : this(new MarketDataAuditRepository(connectionString))
    {
    }

    public MarketDataAuditWorkflow(IMarketDataAuditSnapshotSource snapshotSource)
    {
        _snapshotSource = snapshotSource ??
            throw new ArgumentNullException(nameof(snapshotSource));
    }

    public async Task<MarketDataAuditRunResult> RunAsync(
        MarketDataAuditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MarketDataAuditOptions();
        options.Validate();

        DateTime startedUtc = DateTime.UtcNow;
        MarketDataAuditSnapshot snapshot =
            await _snapshotSource.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        MarketDataAuditReport report = MarketDataAuditor.Analyze(
            snapshot,
            options,
            DateTime.UtcNow);

        return new MarketDataAuditRunResult(
            startedUtc,
            DateTime.UtcNow,
            options,
            report);
    }
}

