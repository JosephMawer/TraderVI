using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveObservationPortfolioTests
{
    [Fact]
    public void LongClosedHistoryDoesNotGrowCurrentMembership()
    {
        var open = new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000, "CAD", new(2026, 10, 21), open, open.AddDays(-1), "test", "fixture"));
        DelphiLiveLedgerPosition Position(DateTime? closed)
        {
            var id = Guid.NewGuid();
            return new(id, "ABC", 1, 100, open.AddDays(-30), Guid.NewGuid(), "{}", DelphiLiveProfitProtectionState.Open(id, 100), closed);
        }
        state = state with { UpdatedUtc = open, Positions = Enumerable.Range(0, 10000).Select(_ => Position(open.AddDays(-1)))
            .Append(Position(null)).Append(Position(open)).ToImmutableArray() };
        var pendingId = Guid.NewGuid();
        var intent = new DelphiLiveActionIntent(pendingId, Guid.NewGuid(), Guid.NewGuid(), "ABC", DelphiLiveActionSide.Sell,
            open.AddDays(-1), open.AddDays(-1), null, 1, null);
        state = state with { Actions = [new(intent, state.Positions.Last().PositionId, null, new(2026, 10, 20),
            Guid.NewGuid(), "Test", "{}", "ExitPendingOvernight", 1, null, null, null, [])] };
        var current = DelphiLiveObservationPortfolio.From(state);
        Assert.Equal(2, current.Positions.Length); Assert.Single(current.OpenPositions);
        Assert.Equal(pendingId, Assert.Single(current.PendingSells).ActionId);
        Assert.True(DelphiLiveLedgerJson.Serialize(current).Length < 2000);
        var packed = DelphiLiveObservationPortfolio.FromPackage(DelphiLiveSnapshotCodec.Pack(state));
        Assert.Equal(DelphiLiveLedgerJson.Serialize(current), DelphiLiveLedgerJson.Serialize(packed));
    }
}
