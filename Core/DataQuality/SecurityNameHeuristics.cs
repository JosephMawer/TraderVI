#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.DataQuality;

/// <summary>
/// Defense-in-depth name checks. These produce audit candidates; they are not
/// authoritative classifications and must never update the database directly.
/// </summary>
public static class SecurityNameHeuristics
{
    private static readonly string[] LeveragedOrInverseMarkers =
    [
        "2x", "3x", "-2x", "-3x", "(2X)", "(3X)",
        "BetaPro", "BtaPro", "MegaLong", "MegaShort",
        "SavvyLong", "SavvyShort", "SavvyLg", "SavvyLng", "SavvyShrt",
        "LFG Daily", "Inverse", "Invrs", "Leveraged",
        "DlyBl", "DlyBr", "DailyInvrs"
    ];

    private static readonly string[] FundMarkers =
    [
        "ETF", "Exchange-Traded", "Exchange Traded", "Covered Call",
        "High Interest Savings", "Income Shares", "UltraYield"
    ];

    public static bool LooksLeveragedOrInverse(string? name)
        => ContainsAny(name, LeveragedOrInverseMarkers);

    public static bool LooksLikeFund(string? name)
        => ContainsAny(name, FundMarkers);

    private static bool ContainsAny(string? value, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return markers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
