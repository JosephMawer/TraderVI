#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveAssignedVariantTests
{
    [Theory]
    [InlineData(0.15, 0.008, DelphiLiveFamilyState.Supportive, DelphiLiveFamilyState.Neutral)]
    [InlineData(0.35, 0.012, DelphiLiveFamilyState.Neutral, DelphiLiveFamilyState.Supportive)]
    public void AssignedRawMoveVariantsChangeActualPolicyJudgment(
        double selected, double rawReturn, DelphiLiveFamilyState variantState, DelphiLiveFamilyState championState)
    {
        DelphiLivePolicyDefinition champion = DelphiLivePolicyDefinition.Version1;
        DelphiLivePolicyDefinition variant = champion with
        {
            PolicyVersionId = Guid.NewGuid(), SelectedRawMoveThreshold = (decimal)selected
        };
        DelphiLiveWindowReturnMeasurement window = Window((decimal)rawReturn, 0m);

        DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(window, Available(0.04m), variant)
            .State.ShouldBe(variantState);
        DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(window, Available(0.04m), champion)
            .State.ShouldBe(championState);
    }

    [Theory]
    [InlineData(0.025, 0.0088, DelphiLiveFamilyState.Supportive, DelphiLiveFamilyState.Neutral)]
    [InlineData(0.10, 0.007, DelphiLiveFamilyState.Neutral, DelphiLiveFamilyState.Supportive)]
    public void AssignedRelativeVariantsChangeActualPolicyJudgment(
        double selected, double xiuReturn, DelphiLiveFamilyState variantState, DelphiLiveFamilyState championState)
    {
        DelphiLivePolicyDefinition champion = DelphiLivePolicyDefinition.Version1;
        DelphiLivePolicyDefinition variant = champion with
        {
            PolicyVersionId = Guid.NewGuid(), SelectedExcessMoveThreshold = (decimal)selected
        };
        DelphiLiveWindowReturnMeasurement window = Window(0.01m, (decimal)xiuReturn);

        DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(window, Available(0.04m), variant)
            .State.ShouldBe(variantState);
        DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(window, Available(0.04m), champion)
            .State.ShouldBe(championState);
    }

    [Fact]
    public void AssignedFourteenSessionRulerChangesActualFamilyCalculationWithoutRenamingBaseline()
    {
        DelphiLivePolicyDefinition champion = DelphiLivePolicyDefinition.Version1;
        DelphiLivePolicyDefinition variant = champion with
        {
            PolicyVersionId = Guid.NewGuid(), SelectedRulerSessions = 14
        };
        DelphiLiveTrueRangeRulerMeasurement ten = new(10, new DateOnly(2026, 9, 4), Available(0.04m));
        DelphiLiveTrueRangeRulerMeasurement fourteen = new(14, new DateOnly(2026, 9, 4), Available(0.02m));
        var rulers = new DelphiLiveVolatilityRulerMeasurements(
            new(5, ten.SourceThroughSession, Available(0.05m)), ten, fourteen,
            new(20, ten.SourceThroughSession, Available(0.03m)));
        DelphiLiveScalarMeasurement immature = DelphiLiveScalarMeasurement.NotMature();
        var hour = new DelphiLiveWindowReturnMeasurement(TimeSpan.FromHours(1), immature, immature, immature);
        var measurements = new DelphiLivePriceMovementMeasurements(
            new DateTime(2026, 9, 8, 13, 50, 0, DateTimeKind.Utc),
            Window(0.006m, 0m), hour, hour, hour, Available(0.006m));

        rulers.Select(variant).ShouldBe(fourteen);
        DelphiLiveFamilyClassifiers.ClassifyPriceMovement(measurements, rulers.Select(variant), variant)
            .Family.State.ShouldBe(DelphiLiveFamilyState.Supportive);
        DelphiLiveFamilyClassifiers.ClassifyPriceMovement(measurements, rulers.Select(champion), champion)
            .Family.State.ShouldBe(DelphiLiveFamilyState.Neutral);
        DelphiLiveFamilyClassifiers.ClassifyPriceMovement(measurements, ten, variant)
            .Family.State.ShouldBe(DelphiLiveFamilyState.Unavailable);
    }

    [Fact]
    public void UndeclaredThresholdsCannotBeAssigned()
    {
        Should.Throw<DelphiLivePolicyValidationException>(() =>
            (DelphiLivePolicyDefinition.Version1 with { SelectedRawMoveThreshold = 0.20m }).Validate());
        Should.Throw<DelphiLivePolicyValidationException>(() =>
            (DelphiLivePolicyDefinition.Version1 with { SelectedExcessMoveThreshold = 0.075m }).Validate());
        Should.Throw<DelphiLivePolicyValidationException>(() =>
            (DelphiLivePolicyDefinition.Version1 with { SelectedRulerSessions = 5 }).Validate());
    }

    private static DelphiLiveWindowReturnMeasurement Window(decimal stock, decimal xiu) =>
        new(TimeSpan.FromMinutes(20), Available(stock), Available(xiu), Available(stock - xiu));

    private static DelphiLiveScalarMeasurement Available(decimal value) =>
        DelphiLiveScalarMeasurement.Available(value);
}
