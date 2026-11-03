using System;
using System.IO;
using System.Text.Json;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class DashboardCollectorOverviewTests
{
    [Theory]
    [InlineData("OutsideSession", 0, "Outside session")]
    [InlineData("Collected", 0, "Collected")]
    [InlineData("SourceAttention", 0, "Needs attention")]
    [InlineData("Stopped", 0, "Stopped")]
    [InlineData("Collected", -121, "Stale")]
    [InlineData("Collected", 60, "Unverified")]
    [InlineData("UnknownState", 0, "Unverified")]
    public void UsesOnlyFreshRecognizedCollectorReceipts(string state, int offset, string expected)
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var pointer = Path.Combine(dir.FullName, "path.txt");
            var status = Path.Combine(dir.FullName, "status.json");
            File.WriteAllText(pointer, status);
            Assert.Equal("Unavailable", DashboardCollectorOverview.Read(pointer, TimeProvider.System).State);
            File.WriteAllText(status, "{");
            Assert.Equal("Unavailable", DashboardCollectorOverview.Read(pointer, TimeProvider.System).State);
            File.WriteAllText(status, JsonSerializer.Serialize(new
            {
                ObservedUtc = DateTimeOffset.UtcNow.AddSeconds(offset), Result = new { State = state }
            }));
            Assert.Equal(expected, DashboardCollectorOverview.Read(pointer, TimeProvider.System).State);
        }
        finally { dir.Delete(true); }
    }
}
