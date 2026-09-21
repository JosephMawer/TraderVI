#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.WSTrade.Accounts;

/// <summary>CAD account-value evidence at the broker's timestamp; net deposits are cumulative.</summary>
public sealed record WealthsimpleAccountValuePoint(DateTimeOffset At, decimal Value, decimal? NetDeposits)
{
    public override string ToString() => "Broker account-value point (financial data omitted)";
}

/// <summary>
/// Read-only ONE_DAY/REGULAR broker graph. Availability of a timed point alone does not establish
/// strategy-compatible valuation, cash-flow completeness, a saved risk baseline or trading authority.
/// </summary>
public sealed class WealthsimpleAccountDayValues
{
    public WealthsimpleTfsaAccount Account { get; }
    public DateTimeOffset RetrievedAt { get; }
    public WealthsimpleAccountValuePoint? PreviousClose { get; }
    public ImmutableArray<WealthsimpleAccountValuePoint> Points { get; }

    internal WealthsimpleAccountDayValues(WealthsimpleTfsaAccount account, DateTimeOffset retrievedAt,
        WealthsimpleAccountValuePoint? previousClose, ImmutableArray<WealthsimpleAccountValuePoint> points)
    { Account = account; RetrievedAt = retrievedAt; PreviousClose = previousClose; Points = points; }

    // The caller supplies the full dated instant. Never interpolate, choose the nearest point,
    // fall back to previous close or silently accept a different day after a late startup.
    public WealthsimpleAccountValuePoint? FindExact(DateTimeOffset at) => Points.FirstOrDefault(p => p.At == at);

    public override string ToString() => "Broker account day values (financial data omitted)";
}
