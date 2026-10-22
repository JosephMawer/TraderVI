#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineLiveCommandTests
{
    [Fact]
    public async Task DashboardCommandsAreSentToTheOwnerAndDashboardCloseDoesNotStopIt()
    {
        var rpc = new FakeRpc(); var service = new DelphiLiveDesktopService(rpc);
        Assert.True(service.IsEngineClient);
        var id = Guid.NewGuid(); var date = new DateOnly(2026, 10, 22);
        await service.ActivateAsync(1000, "CAD", "operator reason");
        await service.ScheduleDiscoveryAsync(DelphiLiveHypothesisFamily.RawMoveThreshold, [0.01m], 1000, "CAD", "reviewed");
        await service.ScheduleUntouchedAsync(id, "reviewed");
        await service.ApprovePromotionAsync("reviewed");
        await service.RecordMeasurementDefectAsync("reviewed");
        await service.ResumeCapitalReviewAsync(id, "reviewed");
        await service.RecordCorporateActionAsync("TEST", date, date, "reviewed");
        await service.StopAsync();
        Assert.Equal(7, rpc.Commands.Count);
        Assert.Equal(Enum.GetValues<EngineLiveCommandKind>(), rpc.Commands.ConvertAll(c => c.Kind));
        Assert.Equal(id, rpc.Commands[2].Id); Assert.Equal(id, rpc.Commands[5].Id);
        Assert.Equal(date, rpc.Commands[6].From); Assert.Equal("TEST", rpc.Commands[6].Symbol);
        Assert.Equal(1000, rpc.Commands[0].Capital); Assert.Equal("operator reason", rpc.Commands[0].Reason);
    }

    [Fact]
    public async Task UncertainRemoteCommandIsNotReplayedOrExecutedLocally()
    {
        var rpc = new FakeRpc { Fail = true }; var service = new DelphiLiveDesktopService(rpc);
        await Assert.ThrowsAsync<System.IO.IOException>(() => service.ActivateAsync(1000, "CAD", "reviewed"));
        Assert.Single(rpc.Commands);
    }

    private sealed class FakeRpc : IEngineRpc
    {
        internal bool Fail;
        internal List<EngineLiveCommand> Commands = [];
        public Task<T> CallAsync<T>(EngineOperation operation, object? payload = null, CancellationToken ct = default)
        {
            Assert.Equal(EngineOperation.LiveCommand, operation);
            Commands.Add(JsonSerializer.Deserialize<EngineLiveCommand>(JsonSerializer.Serialize(payload))!);
            if (Fail) throw new System.IO.IOException("Synthetic lost reply.");
            return Task.FromResult((T)(object)true);
        }
    }
}
