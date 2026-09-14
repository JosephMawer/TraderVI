#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Core.WSTrade.Accounts;

public enum WealthsimpleReadStatus { Success, NotAuthenticated, Forbidden, RateLimited, Unavailable, UnsupportedResponse, AccountUnavailable }

// Results contain presentation data, never request/response bodies, tokens or raw server errors.
public sealed record WealthsimpleReadResult<T>(WealthsimpleReadStatus Status, T? Data = default, string? Stage = null)
{
    public override string ToString() => $"Wealthsimple read: {Status} ({Stage})";
}

public sealed class WealthsimpleTfsaAccount
{
    internal string Id { get; }
    public string DisplayName { get; }
    internal WealthsimpleTfsaAccount(string id, string name) { Id = id; DisplayName = name; }
    public override string ToString() => DisplayName;
}

public sealed record WealthsimpleMoney(decimal Amount, string Currency)
{
    public override string ToString() => $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {Currency}";
}

public sealed record WealthsimpleHolding(string Symbol, string Name, string SecurityType, decimal Quantity,
    WealthsimpleMoney? Value, WealthsimpleMoney? BookValue, WealthsimpleMoney? UnrealizedGain)
{
    // Preserve broker instrument identity; symbols alone are insufficient for order routing.
    internal string? SecurityId { get; init; }
    public override string ToString() => "Wealthsimple holding (financial data omitted)";
}

public sealed record WealthsimplePendingOrder(string Symbol, string Side, string Status,
    string ExecutionType, decimal Quantity, WealthsimpleMoney? LimitPrice, DateTimeOffset CreatedAt)
{
    internal string OrderId { get; init; } = "";
    internal string SecurityId { get; init; } = "";
    public override string ToString() => "Wealthsimple pending order (financial data omitted)";
}

public sealed record WealthsimplePendingOrders(WealthsimpleTfsaAccount Account, DateTimeOffset RetrievedAt,
    IReadOnlyList<WealthsimplePendingOrder> Orders)
{
    public override string ToString() => "Wealthsimple pending orders (financial data omitted)";
}

public sealed class WealthsimpleCanadianSecurity
{
    internal object? SourceClient { get; init; }
    internal string? SourceIdentity { get; init; }
    internal string Id { get; }
    public string Symbol { get; }
    public string Exchange { get; }
    internal WealthsimpleCanadianSecurity(string id, string symbol, string exchange)
    { Id = id; Symbol = symbol; Exchange = exchange; }
    public override string ToString() => $"{Symbol} · {Exchange} · CAD";
}

public sealed record WealthsimpleTfsaSnapshot(WealthsimpleTfsaAccount Account, DateTimeOffset RetrievedAt,
    WealthsimpleMoney? Value, WealthsimpleMoney? Cash, WealthsimpleMoney? BuyingPower,
    WealthsimpleMoney? NetDeposits, WealthsimpleMoney? Return, decimal? ReturnRate,
    string? ReturnReferenceDate, DateTimeOffset? ReturnAsOf, IReadOnlyList<WealthsimpleHolding> Holdings)
{
    public override string ToString() => "Wealthsimple TFSA snapshot (financial data omitted)";
}
