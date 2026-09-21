#nullable enable
using Core.Db;
using Core.WSTrade.Accounts;
using System;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class BrokerAccountImportTests
{
    [Theory]
    [InlineData("2026-09-16T12:00:00.0012345Z", "2026-09-16T12:00:00.0000000Z")]
    [InlineData("2026-09-16T12:00:00.0017000Z", "2026-09-16T12:00:00.0033333Z")]
    [InlineData("2026-09-16T12:00:00.0051000Z", "2026-09-16T12:00:00.0066667Z")]
    [InlineData("2026-09-16T23:59:59.9999000Z", "2026-09-17T00:00:00.0000000Z")]
    [InlineData("2026-09-16T08:00:00.8635789-04:00", "2026-09-16T12:00:00.8633333Z")]
    public void SavedImportRecoversExactLegacySqlRoundingWithoutChangingPlan(string source, string stored)
    {
        var at = DateTimeOffset.Parse(source, CultureInfo.InvariantCulture);
        var plan = Plan() with { Observation = Observation() with { SnapshotAt = at } };
        var row = StoredImport(plan) with { SnapshotUtc = DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture).UtcDateTime };
        var receipt = BrokerAccountImportRepository.Decode(row)!;
        Assert.Equal(at, receipt.Plan.Observation.SnapshotAt);
        Assert.Equal(plan.Fingerprint, receipt.Plan.Fingerprint);
        Assert.Equal(row.RequestId, receipt.RequestId);
        Assert.Equal(row.RequestHash, receipt.RequestHash);
    }

    [Fact]
    public void SavedImportKeepsFullPrecisionForNewAndUnspecifiedSqlTimestamps()
    {
        var at = Now.AddTicks(1234567);
        var plan = Plan() with { Observation = Observation() with { SnapshotAt = at } };
        var row = StoredImport(plan) with { SnapshotUtc = DateTime.SpecifyKind(at.UtcDateTime, DateTimeKind.Unspecified) };
        Assert.Equal(at, BrokerAccountImportRepository.Decode(row)!.Plan.Observation.SnapshotAt);
    }

    [Theory]
    [InlineData(1)] [InlineData(-1)] [InlineData(10000)] [InlineData(10000000)]
    public void SavedImportRejectsTimestampChangesEvenInsideTypicalRoundingTolerance(long deltaTicks)
    {
        var at = Now.AddTicks(12345);
        var plan = Plan() with { Observation = Observation() with { SnapshotAt = at } };
        Assert.Throws<InvalidOperationException>(() => BrokerAccountImportRepository.Decode(
            StoredImport(plan) with { SnapshotUtc = at.UtcDateTime.AddTicks(deltaTicks) }));
    }

    [Theory]
    [InlineData("fingerprint")] [InlineData("identity")] [InlineData("blocked")]
    [InlineData("account")] [InlineData("amount")]
    public void LegacyRoundedTimestampDoesNotBypassImportIntegrityChecks(string change)
    {
        var at = Now.AddTicks(12345);
        var original = Plan() with { Observation = Observation() with { SnapshotAt = at } };
        var altered = change switch
        {
            "identity" => original with { BrokerKey = new string('C', 64) },
            "blocked" => original with { Blocks = ["synthetic conflict"] },
            "account" => original with { AccountLabel = "RRSP" },
            "amount" => original with { Observation = original.Observation with { Cash = 1 } },
            _ => original
        };
        var row = StoredImport(altered) with { SnapshotUtc = Now.UtcDateTime };
        if (change == "fingerprint") row = row with { PlanFingerprint = new string('D', 64) };
        if (change == "amount") row = row with { PlanFingerprint = original.Fingerprint };
        Assert.Throws<InvalidOperationException>(() => BrokerAccountImportRepository.Decode(row));
    }

    [Fact]
    public void LegacyTimestampRecoveryRejectsDatesOutsideSqlDatetimeRange()
    {
        var at = new DateTimeOffset(1600, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(BrokerAccountImportRepository.SnapshotMatches(at, at.UtcDateTime));
        Assert.False(BrokerAccountImportRepository.SnapshotMatches(at, at.UtcDateTime.AddTicks(1)));
    }

    private static BrokerAccountImportRepository.ReceiptRow StoredImport(BrokerImportPlan plan) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new string('E', 64), plan.Fingerprint,
            plan.Observation.SnapshotAt.UtcDateTime, Now.AddDays(1).UtcDateTime, JsonSerializer.Serialize(plan));
}
