using System;

namespace Core.Trader.DelphiLive;

public interface ITsxSessionCalendar
{
    bool IsRegularSession(DateOnly tradingDate);
    DateOnly GetImmediatelyPrecedingSession(DateOnly tradingDate);
    DateOnly GetNextSession(DateOnly afterDate);
    int GetSessionOrdinal(DateOnly tradingDate);
    DelphiLiveSessionBounds GetSessionBounds(DateOnly tradingDate);
}

public sealed record DelphiLiveSessionBounds
{
    public DelphiLiveSessionBounds(
        DateOnly tradingDate,
        DateTime openUtc,
        DateTime closeUtc)
    {
        RequireUtc(openUtc, nameof(openUtc));
        RequireUtc(closeUtc, nameof(closeUtc));
        if (closeUtc <= openUtc)
            throw new ArgumentException("Session close must follow its open.");
        TradingDate = tradingDate;
        OpenUtc = openUtc;
        CloseUtc = closeUtc;
    }

    public DateOnly TradingDate { get; }
    public DateTime OpenUtc { get; }
    public DateTime CloseUtc { get; }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
