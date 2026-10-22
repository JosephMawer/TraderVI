#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Nodes;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveObservedPosition(Guid PositionId, string Symbol, DateTime? ClosedUtc);
public sealed record DelphiLiveObservedExit(Guid ActionId, string Symbol);

/// <summary>Collection membership only. Contains no cash, decision authority or executable order.</summary>
public sealed record DelphiLiveObservationPortfolio(Guid PortfolioId, Guid PolicyVersionId, string Role,
    long Revision, DateOnly AsOfDate, ImmutableArray<DelphiLiveObservedPosition> Positions,
    ImmutableArray<DelphiLiveObservedExit> PendingSells)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DelphiLiveObservedPosition> OpenPositions => Positions.Where(p => p.ClosedUtc is null);
    public static DelphiLiveObservationPortfolio From(DelphiLivePortfolioSnapshot state, bool bounded = true) =>
        new(state.PortfolioId, state.PolicyVersionId, state.Role, state.Revision, LocalDate(state.UpdatedUtc),
            state.Positions.Where(p => !bounded || Relevant(p.ClosedUtc, LocalDate(state.UpdatedUtc)))
                .Select(p => new DelphiLiveObservedPosition(p.PositionId, p.Symbol, p.ClosedUtc)).ToImmutableArray(),
            state.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Sell)
                .Select(a => new DelphiLiveObservedExit(a.Intent.ActionId, a.Intent.Symbol)).ToImmutableArray());

    // Packing already enumerates the changed snapshot. Reuse its small item records without expanding dossiers.
    public static DelphiLiveObservationPortfolio FromPackage(DelphiLiveSnapshotPackage package)
    {
        var root = JsonNode.Parse(package.SnapshotJson)!;
        var date = LocalDate(root["updatedUtc"]!.GetValue<DateTime>());
        var positions = ImmutableArray.CreateBuilder<DelphiLiveObservedPosition>();
        var exits = ImmutableArray.CreateBuilder<DelphiLiveObservedExit>();
        foreach (var item in package.Items.Where(i => i.Collection is "positions" or "actions"))
        {
            var value = JsonNode.Parse(package.Contents[item.ContentHash])!;
            if (item.Collection == "positions")
            {
                DateTime? closed = value["closedUtc"]?.GetValue<DateTime>();
                if (Relevant(closed, date)) positions.Add(new(value["positionId"]!.GetValue<Guid>(), value["symbol"]!.GetValue<string>(), closed));
            }
            else if (value["status"]!.GetValue<string>() is "Pending" or "ExitPendingOvernight" &&
                value["intent"]!["side"]!.GetValue<int>() == (int)DelphiLiveActionSide.Sell)
                exits.Add(new(value["intent"]!["actionId"]!.GetValue<Guid>(), value["intent"]!["symbol"]!.GetValue<string>()));
        }
        return new(root["portfolioId"]!.GetValue<Guid>(), root["policyVersionId"]!.GetValue<Guid>(),
            root["role"]!.GetValue<string>(), root["revision"]!.GetValue<long>(), date, positions.ToImmutable(), exits.ToImmutable());
    }
    private static bool Relevant(DateTime? closed, DateOnly date) => closed is null || LocalDate(closed.Value) >= date;
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, ReviewedTsxSessionCalendar.Toronto));
}
