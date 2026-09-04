#nullable enable

using Core.Db;
using System;
using System.Threading.Tasks;

namespace Core.Trader;

public sealed record PaperTradeEntryResult(
    bool Succeeded,
    Guid PickId,
    Guid? OriginalPickId,
    string Symbol,
    string Lens,
    int Shares,
    decimal FillPrice,
    decimal BookCost,
    TrackedExecutionMode ExecutionMode,
    string? AccountLabel,
    bool IsDiscretionaryOverride,
    string Message);

public sealed record PaperTradeEntryAttribution(
    Guid? OriginalPickId,
    double? EntryComposite,
    bool IsDiscretionaryOverride);

/// <summary>
/// Host-neutral bridge from a persisted Delphi row to a monitored
/// tracked Ghost or Real position. The operator supplies the observed fill;
/// Real means a manually reported broker fill. This workflow never calls a
/// broker or invents an execution price. Non-Buy rows may supply context only
/// for an explicitly confirmed, unlinked discretionary Real holding.
/// </summary>
public sealed class PaperTradeEntryWorkflow
{
    public async Task<PaperTradeEntryResult> OpenAsync(
        Guid pickId,
        int shares,
        decimal fillPrice,
        TrackedExecutionMode executionMode = TrackedExecutionMode.Ghost,
        string? accountLabel = null,
        bool confirmNonBuyRealOverride = false)
    {
        if (pickId == Guid.Empty)
            throw new ArgumentException("A saved Delphi pick is required.", nameof(pickId));
        if (shares <= 0)
            throw new ArgumentOutOfRangeException(nameof(shares), "Shares must be positive.");
        if (fillPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(fillPrice), "Fill price must be positive.");

        string? normalizedAccount =
            TrackedExecutionModeContract.NormalizeAccountLabel(executionMode, accountLabel);

        DailyPickInfo pick = await new DailyPickRepository().GetPickById(pickId)
            ?? throw new InvalidOperationException("The selected Delphi pick no longer exists.");
        PaperTradeEntryAttribution attribution = ResolveAttribution(
            pick,
            executionMode,
            confirmNonBuyRealOverride);

        ActivePositionInfo? existing = await new ActivePositionRepository()
            .GetPositionBySymbol(pick.Symbol);
        if (existing is not null)
            throw new InvalidOperationException(
                $"{pick.Symbol} is already an active {existing.ExecutionMode.ToStorageValue()} position with {existing.Shares} share(s). Close or reconcile it before opening another.");

        decimal bookCost = decimal.Round(fillPrice * shares, 2);
        string lensNote = attribution.IsDiscretionaryOverride
            ? $"; saved {pick.Lens} row retained as context only"
            : string.Equals(pick.Lens, "Breakout", StringComparison.OrdinalIgnoreCase)
                ? "; exploratory Breakout selection"
                : "; production Continuation selection";
        string provenance = executionMode == TrackedExecutionMode.Ghost
            ? "OfficialPaper operator entry"
            : attribution.IsDiscretionaryOverride
                ? "OperatorReal discretionary override; manually reported broker fill"
                : "OperatorReal manually reported broker fill";
        string attributionNote = attribution.IsDiscretionaryOverride
            ? $"selectedPickId={pick.PickId}; savedDirection={pick.Direction}; no original-pick provenance; no fresh-Delphi loss exception"
            : "linked to saved Buy pick";
        string notes =
            $"{provenance}; {pick.Lens} rank={pick.Rank}; " +
            $"pickDate={pick.PickDate:yyyy-MM-dd}; {attributionNote}; operator-supplied fill{lensNote}; " +
            (executionMode == TrackedExecutionMode.Ghost
                ? "ghost database record only; no broker order"
                : $"account={normalizedAccount}; broker fill reported by operator; TraderVI sent no order");

        bool inserted = await new TradeManager(ghost: true).Buy(
            pick.Symbol,
            shares,
            fillPrice,
            notes,
            attribution.OriginalPickId,
            attribution.EntryComposite,
            executionMode == TrackedExecutionMode.Ghost
                ? $"Operator ghost entry · {pick.Lens}"
                : attribution.IsDiscretionaryOverride
                    ? "Operator real fill · discretionary override"
                    : $"Operator real fill · {pick.Lens}",
            executionMode,
            normalizedAccount);
        if (!inserted)
            throw new InvalidOperationException($"The {pick.Symbol} tracked position was not opened.");

        return new PaperTradeEntryResult(
            true,
            pick.PickId,
            attribution.OriginalPickId,
            pick.Symbol,
            pick.Lens,
            shares,
            fillPrice,
            bookCost,
            executionMode,
            normalizedAccount,
            attribution.IsDiscretionaryOverride,
            attribution.IsDiscretionaryOverride
                ? $"Tracking {shares} {pick.Symbol} share(s) at {fillPrice:C2} as a discretionary Real holding; the saved {pick.Lens} {pick.Direction} row is retained as context, not recommendation provenance."
                : $"Tracking {shares} {pick.Symbol} share(s) at {fillPrice:C2} as {executionMode.ToStorageValue()} from the saved {pick.Lens} Buy pick.");
    }

    public static PaperTradeEntryAttribution ResolveAttribution(
        DailyPickInfo pick,
        TrackedExecutionMode executionMode,
        bool confirmNonBuyRealOverride)
    {
        ArgumentNullException.ThrowIfNull(pick);

        if (string.Equals(pick.Direction, "Buy", StringComparison.OrdinalIgnoreCase))
        {
            return new PaperTradeEntryAttribution(
                pick.PickId,
                pick.CompositeScore,
                IsDiscretionaryOverride: false);
        }

        if (executionMode != TrackedExecutionMode.Real)
        {
            throw new InvalidOperationException(
                $"The selected {pick.Lens} pick for {pick.Symbol} is {pick.Direction}, not Buy. Ghost entries require a saved Buy recommendation.");
        }

        if (!confirmNonBuyRealOverride)
        {
            throw new InvalidOperationException(
                $"The selected {pick.Lens} pick for {pick.Symbol} is {pick.Direction}, not Buy. Recording an actual Real fill requires explicit confirmation that this is a discretionary override.");
        }

        return new PaperTradeEntryAttribution(
            OriginalPickId: null,
            EntryComposite: null,
            IsDiscretionaryOverride: true);
    }
}
