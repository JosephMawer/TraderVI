#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record BrokerRiskBaselineState(bool Installed, BrokerImportState Import,
    BrokerBaselineRiskHistory RiskHistory);
public sealed record BrokerRiskBaselinePreview(bool Installed, BrokerRiskBaselinePreparation Preparation);
public sealed record BrokerRiskBaselineRequest(BrokerAccountRiskBaseline Reviewed, string ReviewedBy)
{
    // Preparation time changes at the transactional recheck; the source/evidence boundary does not.
    public string ReviewedFingerprint => ReviewFingerprint(Reviewed);
    public string RequestHash => BrokerAccountImport.Hash(new { Reviewed.BaselineId, ReviewedFingerprint, ReviewedBy });
    internal static string ReviewFingerprint(BrokerAccountRiskBaseline value) =>
        BrokerAccountImport.Hash(new
        {
            Baseline = value with { EstablishedAt = default,
                CarriedMemory = value.CarriedMemory with { Symbols = TradingRecommendationMemory.Empty.Symbols } },
            Symbols = value.CarriedMemory.Symbols.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()
        });
    public override string ToString() => "Reviewed initial risk baseline request (financial data omitted)";
}
public sealed record BrokerRiskBaselineReceipt(BrokerAccountRiskBaseline Baseline, string ReviewedBy, string RequestHash)
{
    public override string ToString() => "Saved initial risk baseline (financial data omitted)";
}

public interface IBrokerRiskBaselineStore
{
    Task<BrokerRiskBaselineState> ReadAsync(CancellationToken ct);
    Task<BrokerRiskBaselineReceipt?> FindAsync(Guid baselineId, CancellationToken ct);
    Task<BrokerRiskBaselineReceipt> SaveAsync(BrokerRiskBaselineRequest request, CancellationToken ct);
}

/// <summary>Explicit baseline setup only. No history import, broker client, engine or order dependency.</summary>
public sealed class BrokerAccountRiskBaselineWorkflow(IBrokerRiskBaselineStore store, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public async Task<BrokerRiskBaselinePreview> PreviewAsync(Guid baselineId, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        return new(state.Installed, BrokerAccountRiskBaselinePolicy.Prepare(baselineId, state.Import, state.RiskHistory, clock.GetUtcNow()));
    }

    public async Task<BrokerRiskBaselineReceipt> ApplyAsync(BrokerRiskBaselineRequest request, CancellationToken ct = default)
    {
        Validate(request);
        // Recover a committed request even when its original observation is now stale or a read fails.
        var saved = await store.FindAsync(request.Reviewed.BaselineId, ct);
        if (saved is not null)
        {
            RequireSameRequest(request, saved);
            return saved;
        }
        var state = await store.ReadAsync(ct);
        Recheck(request, state, clock.GetUtcNow());
        ct.ThrowIfCancellationRequested();
        return await store.SaveAsync(request, ct);
    }

    internal static BrokerAccountRiskBaseline Recheck(BrokerRiskBaselineRequest request, BrokerRiskBaselineState state, DateTimeOffset now)
    {
        Validate(request);
        if (!state.Installed)
            throw new InvalidOperationException("Install the separately reviewed initial risk-baseline migration before saving setup.");
        var prepared = BrokerAccountRiskBaselinePolicy.Prepare(request.Reviewed.BaselineId, state.Import, state.RiskHistory, now);
        if (!prepared.CanEstablish || BrokerRiskBaselineRequest.ReviewFingerprint(prepared.Baseline!) != request.ReviewedFingerprint)
            throw new InvalidOperationException("Broker observations, risk history or baseline presence changed. Review setup again.");
        return prepared.Baseline!;
    }

    internal static void Validate(BrokerRiskBaselineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Reviewed);
        if (request.Reviewed.BaselineId == Guid.Empty || string.IsNullOrWhiteSpace(request.ReviewedBy) || request.ReviewedBy.Length > 128)
            throw new ArgumentException("A baseline request identity and reviewer are required.");
    }

    internal static void RequireSameRequest(BrokerRiskBaselineRequest request, BrokerRiskBaselineReceipt saved)
    {
        if (saved.RequestHash != request.RequestHash || saved.Baseline.BaselineId != request.Reviewed.BaselineId)
            throw new InvalidOperationException("This baseline request already has different saved content or reviewer.");
    }
}
