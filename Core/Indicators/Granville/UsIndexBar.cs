using System;

namespace Core.Indicators.Granville;

/// <summary>
/// Daily OHLC bar for a US-listed index (e.g., S&amp;P 500, NYSE Composite).
/// Used by <see cref="GenuityIndicators"/> (Granville #17–#20) to verify whether
/// XIU's daily move is "genuine" — i.e., commensurately mirrored by a broader US index.
/// </summary>
/// <param name="Symbol">Canonical index symbol (e.g., "^GSPC", "^NYA"). Stored as-is in [dbo].[UsIndexBars].</param>
/// <param name="Date">Trading-day date (UTC date component).</param>
/// <param name="Open">Session open.</param>
/// <param name="High">Session high.</param>
/// <param name="Low">Session low.</param>
/// <param name="Close">Session close — the only field Genuity actually consumes.</param>
/// <param name="Volume">Reported volume; indices often publish 0. Not used by Genuity.</param>
public sealed record UsIndexBar(
    string Symbol,
    DateTime Date,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume);
