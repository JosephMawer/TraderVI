#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.WSTrade.Accounts;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class HoldingSectorPresentationTests
{
    [Fact]
    public void UsesVerifiedNamesAndRetainsClassificationDateWithoutGuessingSuffixes()
    {
        var date = new DateTime(2026, 11, 3, 11, 30, 0, DateTimeKind.Utc);
        var rows = HoldingSectorInfo.From([
            new("FIX", "FIX", "Basic Materials", "Mining", "^TTMT", date),
            new("RENAMED.TO", "FIX", "Basic Materials", "Mining", "^TTMT", date),
            new("OTHER", "OTHER", "Specialty", null, null, date)]);
        Assert.Equal("Materials", rows["fix"].Sector);
        Assert.Equal(rows["FIX"], rows["RENAMED.TO"]);
        Assert.Contains("Mining", rows["FIX"].Details);
        Assert.Contains("2026", rows["FIX"].Details);
        Assert.False(rows.ContainsKey("FIX.TO"));
        Assert.Equal("Specialty", rows["OTHER"].Sector);
    }

    [Fact]
    public void MissingOrConflictingClassificationsRemainUnknown()
    {
        var date = DateTime.UtcNow;
        var rows = HoldingSectorInfo.From([
            new("FIX", "FIX", "Energy", null, "^TTEN", date),
            new("FIX", "OTHER", "Energy", null, "^TTEN", date),
            new("EMPTY", "EMPTY", "Unknown", null, null, date)]);
        Assert.Equal("Unknown", rows["FIX"].Sector);
        Assert.Equal("Unknown", rows["EMPTY"].Sector);
        var holding = new WealthsimpleHoldingRow(new("UNMAPPED", "Fixture", "EQUITY", 1, null, null, null));
        Assert.Equal("Unknown", holding.Sector);
    }
}

public sealed partial class WealthsimpleAccountTests
{
    [Fact]
    public async Task SectorReadEnrichesHoldingsAndFailureDoesNotInvalidateAccountOrTradingSelection()
    {
        var source = new Source();
        source.Read = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success,
            Snapshot(a) with { Holdings = [new("FIX", "Fixture", "EQUITY", 1, new(10, "CAD"), new(9, "CAD"), null) { SecurityId = "fixture" }] }));
        bool fail = false;
        using var vm = new WealthsimpleAccountViewModel(source, readSectors: _ => fail
            ? Task.FromException<IReadOnlyDictionary<string, HoldingSectorInfo>>(new InvalidOperationException("fixture"))
            : Task.FromResult(HoldingSectorInfo.From([new("FIX", "FIX", "Energy", "Oil", "^TTEN", DateTime.UtcNow)])));
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Holdings);
        Assert.Equal("Energy", row.Sector);
        var exposure = Assert.Single(vm.SectorExposure);
        Assert.Equal("Energy", exposure.Sector);
        Assert.Equal(10m, exposure.Value);
        Assert.Equal(1m, exposure.Share);
        Assert.Contains("cash excluded", vm.ExposureContext);
        Assert.True(vm.IsCurrentSellSelection(row.SellSelection!));
        fail = true;
        await vm.RefreshAsync();
        row = Assert.Single(vm.Holdings);
        Assert.Equal("Energy", row.Sector);
        Assert.Contains("metadata read failed", row.SectorDetails);
        Assert.True(vm.IsCurrentSellSelection(row.SellSelection!));
        Assert.NotNull(vm.PendingOrders);
        Assert.DoesNotContain("refresh could not complete", vm.Status);
    }

    [Fact]
    public async Task LateMetadataCannotRestoreHoldingsAfterDisconnect()
    {
        var source = new Source();
        var completion = new TaskCompletionSource<IReadOnlyDictionary<string, HoldingSectorInfo>>();
        using var vm = new WealthsimpleAccountViewModel(source, readSectors: _ => completion.Task);
        var refresh = vm.RefreshAsync();
        Assert.True(vm.IsBusy);
        source.Disconnect();
        completion.SetResult(HoldingSectorInfo.From([]));
        await refresh;
        Assert.Null(vm.Snapshot);
        Assert.Empty(vm.Holdings);
        Assert.False(vm.IsBusy);
    }
}
