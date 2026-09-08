#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Runtime;

public static class TradingSystems
{
    public const string Daily = "Daily";
    public static readonly string[] All = [Daily, EngineStrategySettings.Live, EngineStrategySettings.Shadow, EngineStrategySettings.Tracked];
    public static string Name(string key) => key switch
    {
        Daily => "Daily Delphi and dependent entries",
        EngineStrategySettings.Live => "Delphi Live",
        EngineStrategySettings.Shadow => "System Shadow",
        EngineStrategySettings.Tracked => "Trading monitor / tracked entries",
        _ => throw new ArgumentException("Unknown trading system.")
    };
    public static void Validate(string key) { _ = Name(key); }
}

public sealed record TradingSystemState(string SystemKey, bool Paused, DateTime ChangedUtc, string Reason);
public sealed record TradingSettingsAccess(bool Installed, bool Paused, int Holdings, int PendingOrders)
{
    public bool CanEdit => Installed && Paused && Holdings == 0 && PendingOrders == 0;
    public string Description => !Installed ? "Trading controls require migration 028. Settings are read-only." :
        !Paused ? "Read-only: pause new buys before changing trading rules." :
        Holdings > 0 || PendingOrders > 0 ? $"Read-only: {Holdings} holding(s) and {PendingOrders} pending order(s) remain. Close them on the trading pages; exits continue while paused." :
        "Paused and empty. Edit and save a version, assign it, then resume when ready.";
}

public sealed record OperatorExitRequest(Guid RequestId, string Family, Guid TargetId, Guid PositionId,
    string Symbol, DateTime RequestedUtc, string RequestedBy, string Reason, string PositionJson);
