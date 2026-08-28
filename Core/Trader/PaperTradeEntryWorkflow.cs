#nullable enable

using Core.Db;
using System;
using System.Threading.Tasks;

namespace Core.Trader;

public sealed record PaperTradeEntryResult(
    bool Succeeded,
    Guid PickId,
    string Symbol,
    string Lens,
    int Shares,
    decimal FillPrice,
    decimal BookCost,
    TrackedExecutionMode ExecutionMode,
    string? AccountLabel,
    string Message);

/// <summary>
/// Host-neutral bridge from a persisted Delphi recommendation to a monitored
/// tracked Ghost or Real position. The operator supplies the observed fill;
/// Real means a manually reported broker fill. This workflow never calls a
/// broker or invents an execution price.
/// </summary>
public sealed class PaperTradeEntryWorkflow
{
    public async Task<PaperTradeEntryResult> OpenAsync(
        Guid pickId,
        int shares,
        decimal fillPrice,
        TrackedExecutionMode executionMode = TrackedExecutionMode.Ghost,
        string? accountLabel = null)
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
        if (!string.Equals(pick.Direction, "Buy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The selected {pick.Lens} pick for {pick.Symbol} is {pick.Direction}, not Buy.");

        ActivePositionInfo? existing = await new ActivePositionRepository()
            .GetPositionBySymbol(pick.Symbol);
        if (existing is not null)
            throw new InvalidOperationException(
                $"{pick.Symbol} is already an active {existing.ExecutionMode.ToStorageValue()} position with {existing.Shares} share(s). Close or reconcile it before opening another.");

        decimal bookCost = decimal.Round(fillPrice * shares, 2);
        string lensNote = string.Equals(pick.Lens, "Breakout", StringComparison.OrdinalIgnoreCase)
            ? "; exploratory Breakout selection"
            : "; production Continuation selection";
        string provenance = executionMode == TrackedExecutionMode.Ghost
            ? "OfficialPaper operator entry"
            : "OperatorReal manually reported broker fill";
        string notes =
            $"{provenance}; {pick.Lens} rank={pick.Rank}; " +
            $"pickDate={pick.PickDate:yyyy-MM-dd}; operator-supplied fill{lensNote}; " +
            (executionMode == TrackedExecutionMode.Ghost
                ? "ghost database record only; no broker order"
                : $"account={normalizedAccount}; broker fill reported by operator; TraderVI sent no order");

        bool inserted = await new TradeManager(ghost: true).Buy(
            pick.Symbol,
            shares,
            fillPrice,
            notes,
            pick.PickId,
            pick.CompositeScore,
            executionMode == TrackedExecutionMode.Ghost
                ? $"Operator ghost entry · {pick.Lens}"
                : $"Operator real fill · {pick.Lens}",
            executionMode,
            normalizedAccount);
        if (!inserted)
            throw new InvalidOperationException($"The {pick.Symbol} tracked position was not opened.");

        return new PaperTradeEntryResult(
            true,
            pick.PickId,
            pick.Symbol,
            pick.Lens,
            shares,
            fillPrice,
            bookCost,
            executionMode,
            normalizedAccount,
            $"Tracking {shares} {pick.Symbol} share(s) at {fillPrice:C2} as {executionMode.ToStorageValue()} from the saved {pick.Lens} pick.");
    }
}
