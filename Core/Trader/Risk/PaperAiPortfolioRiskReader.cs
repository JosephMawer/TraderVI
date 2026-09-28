#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Core.Trader.DelphiLive;

namespace Core.Trader.Risk;

public interface ISystemShadowRiskReader
{
    Task<ShadowPaperRiskObservation?> ReadRiskObservationAsync(Guid portfolioId, DateOnly tradingDate,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists qualified paper risk checks. The explicit faster-source overload collects prices; no model or order calls.</summary>
public sealed class PaperAiPortfolioRiskReader
{
    private readonly Func<DateOnly, bool, bool, ImmutableArray<string>, CancellationToken, Task<AiRiskObservationResult>> read;

    public PaperAiPortfolioRiskReader(PaperAiFastValuationReader source, PaperAiFastRiskMonitor monitor, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(monitor);
        if (source.PortfolioId != monitor.PortfolioId) throw new ArgumentException("Reader and journal must own the same paper account.");
        clock ??= TimeProvider.System;
        read = async (date, initialize, reviewed, blocks, ct) =>
        {
            var result = await source.ReadAsync(date, ct);
            ct.ThrowIfCancellationRequested();
            var now = clock.GetUtcNow();
            return initialize ? monitor.Start(result.Valuation, now, reviewed, blocks) : monitor.Observe(result.Valuation, now, reviewed, blocks);
        };
    }

    public PaperAiPortfolioRiskReader(IDelphiLivePortfolioReader source, DelphiLiveAiPortfolioRiskMonitor monitor,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(monitor);
        clock ??= TimeProvider.System;
        read = async (date, initialize, reviewed, blocks, ct) =>
        {
            var snapshot = await source.LoadPortfolioAsync(monitor.PortfolioId, ct);
            ct.ThrowIfCancellationRequested();
            if (snapshot?.CurrentSession != date) snapshot = null;
            // Check age after I/O: a slow read must not return permission against the pre-read clock.
            var now = clock.GetUtcNow();
            return initialize ? monitor.Start(snapshot, now, reviewed, blocks) : monitor.Observe(snapshot, now, reviewed, blocks);
        };
    }

    public PaperAiPortfolioRiskReader(ISystemShadowRiskReader source, SystemShadowAiPortfolioRiskMonitor monitor,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(monitor);
        clock ??= TimeProvider.System;
        read = async (date, initialize, reviewed, blocks, ct) =>
        {
            var observation = await source.ReadRiskObservationAsync(monitor.PortfolioId, date, ct);
            ct.ThrowIfCancellationRequested();
            if (observation?.TradingDate != date) observation = null;
            var now = clock.GetUtcNow();
            return initialize ? monitor.Start(observation, now, reviewed, blocks) : monitor.Observe(observation, now, reviewed, blocks);
        };
    }

    // Read/storage failures propagate, requiring the host to block new risk. They never create an empty round.
    public Task<AiRiskObservationResult> StartAsync(DateOnly date, bool hasFreshAiReview,
        ImmutableArray<string> independentBlocks, CancellationToken ct = default) => read(date, true, hasFreshAiReview, independentBlocks, ct);
    public Task<AiRiskObservationResult> ObserveAsync(DateOnly date, bool hasFreshAiReview,
        ImmutableArray<string> independentBlocks, CancellationToken ct = default) => read(date, false, hasFreshAiReview, independentBlocks, ct);
}
