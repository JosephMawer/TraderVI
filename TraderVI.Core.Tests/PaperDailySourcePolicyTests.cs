using Core.Trader;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperDailySourcePolicyTests
{
    private static readonly DateTime Open = new(2026, 9, 9, 13, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void LatePublicationAdoptsOnce_AndCannotMoveBackOrUseFutureEvidence()
    {
        Guid first = Guid.NewGuid(), corrected = Guid.NewGuid();
        DateTime published = Open.AddMinutes(29), now = Open.AddMinutes(70);
        PaperDailySourcePolicy.ShouldAdopt(null, null, corrected, published, now).ShouldBeTrue();
        PaperDailySourcePolicy.ShouldAdopt(first, Open.AddMinutes(20), corrected, published, now).ShouldBeTrue();
        PaperDailySourcePolicy.ShouldAdopt(corrected, published, corrected, published, now.AddHours(1)).ShouldBeFalse();
        PaperDailySourcePolicy.ShouldAdopt(corrected, published, first, Open.AddMinutes(20), now).ShouldBeFalse();
        PaperDailySourcePolicy.ShouldAdopt(first, Open, corrected, now.AddSeconds(1), now).ShouldBeFalse();
        PaperDailySourcePolicy.ShouldAdopt(null, null, Guid.Empty, published, now).ShouldBeFalse();
    }

    [Fact]
    public void FreshBoundaryRoundsForward_AndRequiresAdjacentPostBoundaryEndpoints()
    {
        DateTime boundary = PaperDailySourcePolicy.EffectiveBoundary(Open.AddMinutes(29).AddSeconds(14));
        boundary.ShouldBe(Open.AddMinutes(30));
        PaperDailySourcePolicy.EffectiveBoundary(boundary).ShouldBe(boundary);
        PaperDailySourcePolicy.HasFreshPair(boundary.AddMinutes(-5), boundary, boundary).ShouldBeFalse();
        PaperDailySourcePolicy.HasFreshPair(boundary, boundary.AddMinutes(5), boundary).ShouldBeTrue();
        PaperDailySourcePolicy.HasFreshPair(boundary.AddMinutes(-5), boundary.AddMinutes(5), boundary).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => PaperDailySourcePolicy.EffectiveBoundary(DateTime.SpecifyKind(Open, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void SourceChangePermanentlyExcludesOtherwiseCleanOpeningResearch()
    {
        var cohort = new DelphiLiveCohortEvidence(new(2026, 9, 9), 1, "test", 78, 78, false, false, true,
            true, true, false, false, ImmutableDictionary<Guid, decimal?>.Empty, ImmutableDictionary<Guid, decimal?>.Empty);
        cohort.IsClean.ShouldBeTrue();
        (cohort with { DailySourceChanged = true }).IsClean.ShouldBeFalse();
    }
}
