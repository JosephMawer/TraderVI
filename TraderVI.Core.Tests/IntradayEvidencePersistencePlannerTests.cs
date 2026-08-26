using Core.Calibration;
using Core.TMX.Models.Domain;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class IntradayEvidencePersistencePlannerTests
{
    [Fact]
    public void Create_StoresOnlyCompletedBarsAndKeepsExactRepeatsIdempotent()
    {
        DateTime startUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        var completed = Bar(startUtc, 10m);
        var forming = Bar(startUtc.AddMinutes(5), 11m);
        TmxIntradayBatch batch = Batch(startUtc, startUtc.AddMinutes(8), completed, forming);
        StoredIntradayEvidenceBar existing = StoredIntradayEvidenceBar.From(completed);

        IntradayEvidenceWritePlan plan = IntradayEvidencePersistencePlanner.Create(
            Context(),
            batch,
            new[] { existing });

        plan.CompletedBars.ShouldBe(new[] { completed });
        plan.NewBars.ShouldBeEmpty();
        plan.ConflictingBars.ShouldBeEmpty();
        plan.AuditState.ShouldBe(IntradayPollAuditState.Valid);
        plan.AuditCode.ShouldBeNull();
    }

    [Fact]
    public void Create_MarksDifferentCompletedNaturalKeyInvalidWithoutReplacingIt()
    {
        DateTime startUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        var received = Bar(startUtc, 10m);
        var stored = new StoredIntradayEvidenceBar(
            startUtc,
            received.Open,
            received.High,
            received.Low,
            99m,
            received.Volume);

        IntradayEvidenceWritePlan plan = IntradayEvidencePersistencePlanner.Create(
            Context(),
            Batch(startUtc, startUtc.AddMinutes(6), received),
            new[] { stored });

        plan.AuditState.ShouldBe(IntradayPollAuditState.Invalid);
        plan.AuditCode.ShouldBe("CompletedBarConflict");
        plan.NewBars.ShouldBeEmpty();
        plan.ConflictingBars.ShouldBe(new[] { received });
    }

    [Fact]
    public void Create_MarksAnOldNewestCompletedBarDegraded()
    {
        DateTime startUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        OhlcvBar bar = Bar(startUtc, 10m);

        IntradayEvidenceWritePlan plan = IntradayEvidencePersistencePlanner.Create(
            Context(),
            Batch(startUtc, startUtc.AddMinutes(51), bar),
            Array.Empty<StoredIntradayEvidenceBar>());

        plan.AuditState.ShouldBe(IntradayPollAuditState.Degraded);
        plan.AuditCode.ShouldBe("LateCompletedEvidence");
        plan.NewBars.ShouldBe(new[] { bar });
    }

    [Fact]
    public void Create_ComparesAtThePersistedSixDecimalPrecision()
    {
        DateTime startUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        OhlcvBar received = new(
            startUtc,
            10.1234567m,
            11.1234567m,
            9.1234567m,
            10.1234567m,
            100);
        StoredIntradayEvidenceBar stored = StoredIntradayEvidenceBar.From(received);

        IntradayEvidenceWritePlan plan = IntradayEvidencePersistencePlanner.Create(
            Context(),
            Batch(startUtc, startUtc.AddMinutes(6), received),
            new[] { stored });

        plan.AuditState.ShouldBe(IntradayPollAuditState.Valid);
        plan.ConflictingBars.ShouldBeEmpty();
        plan.NewBars.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_RejectsAnUnsupportedInterval()
    {
        DateTime startUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        var batch = new TmxIntradayBatch(
            "XIU",
            1,
            startUtc,
            startUtc.AddMinutes(1),
            startUtc,
            startUtc.AddMinutes(1),
            1,
            1,
            new[] { Bar(startUtc, 10m) });

        Should.Throw<ArgumentOutOfRangeException>(() =>
            IntradayEvidencePersistencePlanner.Validate(
                Context(),
                batch,
                Array.Empty<StoredIntradayEvidenceBar>()));
    }

    private static IntradayPollContext Context() =>
        new(
            Guid.NewGuid(),
            IntradayPollPurpose.PaperMonitor,
            IntradayEvidenceVersions.Collector,
            IntradayEvidenceVersions.Policy,
            new CodeProvenance("abc123", "Git", "Clean"));

    private static TmxIntradayBatch Batch(
        DateTime startUtc,
        DateTime receivedUtc,
        params OhlcvBar[] bars) =>
        new(
            "XIU",
            5,
            startUtc,
            receivedUtc,
            startUtc,
            receivedUtc,
            1,
            1,
            bars);

    private static OhlcvBar Bar(DateTime eventUtc, decimal close) =>
        new(eventUtc, close, close + 1m, close - 1m, close, 100);
}
