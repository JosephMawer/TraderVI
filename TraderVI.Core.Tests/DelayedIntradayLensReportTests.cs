#nullable enable

using Core.Calibration;
using Core.TMX.Models.Domain;
using Core.Trader;
using Shouldly;
using System;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelayedIntradayLensReportTests
{
    [Fact]
    public void ReportUsesEqualCohortWeightAndKeepsSensitivitySeparate()
    {
        var rows = new[]
        {
            Row(new DateTime(2026, 8, 27), "Continuation", 0.10, 0.09),
            Row(new DateTime(2026, 8, 27), "Continuation", 0.20, 0.19),
            Row(new DateTime(2026, 8, 28), "Continuation", -0.10, -0.11)
        };

        DelayedIntradayLensReport report = DelayedIntradayLensReportCalculator.Build(rows)[0];

        report.MetricsAvailable.ShouldBeTrue();
        report.MeanGrossReturn!.Value.ShouldBe(0.025, 0.0000001);
        report.MeanConservativeNetReturn!.Value.ShouldBe(0.015, 0.0000001);
        report.ContributingCohorts.ShouldBe(2);
    }

    [Fact]
    public void ReportBlocksMetricsBelowCoverageFloor()
    {
        DelayedIntradayLensEvidenceRow matured = Row(
            new DateTime(2026, 8, 28), "Breakout", 0.10, 0.09);
        var pending = matured with
        {
            CandidateId = Guid.NewGuid(),
            MaturityState = null,
            AuditState = null,
            OutcomeJson = null
        };

        DelayedIntradayLensReport report = DelayedIntradayLensReportCalculator.Build([matured, pending])[1];

        report.MetricsAvailable.ShouldBeFalse();
        report.UsableCoverage.ShouldBe(0.5);
        report.MeanGrossReturn.ShouldBeNull();
    }

    [Fact]
    public void MalformedMaturedOutcomeIsInvalidRatherThanPending()
    {
        DelayedIntradayLensEvidenceRow row = Row(
            new DateTime(2026, 8, 28), "Continuation", 0.10, 0.09) with
        {
            OutcomeJson = "{not-json}"
        };

        DelayedIntradayLensReport report = DelayedIntradayLensReportCalculator.Build([row])[0];

        report.InvalidRecommendations.ShouldBe(1);
        report.PendingRecommendations.ShouldBe(0);
        report.MetricsAvailable.ShouldBeFalse();
    }

    private static DelayedIntradayLensEvidenceRow Row(
        DateTime cohort,
        string lens,
        double gross,
        double conservative)
    {
        var outcome = new DelayedIntradayOutcomeV1(
            1,
            IntradayEvidenceVersions.Policy,
            Utc(13, 30),
            10m,
            10.025m,
            20m,
            IntradaySwingReason.TrailingProfit,
            10.5m,
            Utc(14, 0),
            Utc(14, 15),
            Utc(14, 31),
            16,
            false,
            Utc(14, 35),
            4,
            11m,
            10.9725m,
            20.2m,
            gross,
            conservative,
            0.01,
            gross - 0.01,
            conservative - 0.01,
            0.0025m,
            DelayedIntradayOutcomeCalculator.FillConvention,
            null);
        return new DelayedIntradayLensEvidenceRow(
            Guid.NewGuid(),
            cohort,
            lens,
            Guid.NewGuid(),
            nameof(CalibrationOutcomeMaturityState.Matured),
            nameof(CalibrationAuditState.Valid),
            JsonSerializer.Serialize(outcome));
    }

    private static DateTime Utc(int hour, int minute) =>
        new(2026, 8, 28, hour, minute, 0, DateTimeKind.Utc);
}
