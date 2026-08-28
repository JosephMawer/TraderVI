#nullable enable

using System;

namespace Core.Trader;

/// <summary>
/// Describes what a tracked position represents. Real means an operator-reported
/// broker fill; it never implies that TraderVI submitted or can submit an order.
/// </summary>
public enum TrackedExecutionMode
{
    Ghost,
    Real
}

public static class TrackedExecutionModeContract
{
    public const string GhostStorageValue = "Ghost";
    public const string RealStorageValue = "Real";

    public static string ToStorageValue(this TrackedExecutionMode mode) => mode switch
    {
        TrackedExecutionMode.Ghost => GhostStorageValue,
        TrackedExecutionMode.Real => RealStorageValue,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown execution mode.")
    };

    public static TrackedExecutionMode Parse(string? value) => value switch
    {
        GhostStorageValue => TrackedExecutionMode.Ghost,
        RealStorageValue => TrackedExecutionMode.Real,
        _ => throw new InvalidOperationException(
            $"Unsupported tracked execution mode '{value ?? "<null>"}'.")
    };

    public static string? NormalizeAccountLabel(
        TrackedExecutionMode mode,
        string? accountLabel)
    {
        if (mode == TrackedExecutionMode.Ghost)
        {
            if (!string.IsNullOrWhiteSpace(accountLabel))
                throw new ArgumentException(
                    "Ghost positions cannot carry a brokerage account label.",
                    nameof(accountLabel));
            return null;
        }

        string normalized = accountLabel?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 64)
            throw new ArgumentException(
                "Real positions require an account label of 1 to 64 characters.",
                nameof(accountLabel));
        return normalized;
    }

    public static bool AllowsAutomaticExit(this TrackedExecutionMode mode) =>
        mode == TrackedExecutionMode.Ghost;
}
