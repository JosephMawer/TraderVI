#nullable enable

using Core.Db;

namespace Core.Trader;

/// <summary>
/// Defines which durable positions belong in the operational dashboard and
/// monitor without weakening Delphi provenance for simulated positions.
/// </summary>
public static class TrackedPositionScope
{
    public static bool Includes(ActivePositionInfo position) =>
        position.OriginalPickId.HasValue ||
        position.ExecutionMode == TrackedExecutionMode.Real;

    public static bool AllowsFreshDelphiLossException(ActivePositionInfo position) =>
        position.OriginalPickId.HasValue;
}
