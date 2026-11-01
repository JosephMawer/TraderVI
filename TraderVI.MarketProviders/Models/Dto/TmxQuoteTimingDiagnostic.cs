#nullable enable
using System;
using System.Collections.Generic;

namespace Core.TMX.Models.Dto;

/// <summary>Unqualified page data. Last-trade time is not bid/ask time or a fresh account valuation.</summary>
public sealed class TmxDelayedQuoteTimingDto
{
    public decimal? bid { get; set; }
    public decimal? ask { get; set; }
    public string? lasttradedatetime { get; set; }
    public override string ToString() => "TMX delayed quote timing (values omitted)";
}

public sealed record TmxQuoteTimingDiagnostic(DateTime FetchStartedUtc, DateTime ReceivedUtc, int AttemptCount,
    IReadOnlyList<TmxQuoteDto> Quotes, IReadOnlyList<TmxDelayedQuoteTimingDto> DelayedQuotes,
    IReadOnlyList<TmxRecentTradeDto> RecentTrades)
{
    public override string ToString() => "TMX quote timing diagnostic (values omitted; not valuation authority)";
}
