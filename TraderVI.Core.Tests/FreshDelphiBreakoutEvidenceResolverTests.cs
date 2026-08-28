#nullable enable

using Core.Trader;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class FreshDelphiBreakoutEvidenceResolverTests
{
    private static readonly DateTime EntryUtc =
        new(2026, 8, 27, 13, 40, 0, DateTimeKind.Utc);
    private static readonly DateTime DecisionUtc =
        new(2026, 8, 27, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewestAvailableRunWinsEvenWhenItDidNotPublishTheSymbol()
    {
        FreshDelphiBreakoutEvidenceSnapshot olderPublished = Snapshot(
            1,
            startedUtc: EntryUtc.AddMinutes(10),
            availableUtc: EntryUtc.AddMinutes(20),
            published: true);
        FreshDelphiBreakoutEvidenceSnapshot latestUnpublished = Snapshot(
            2,
            startedUtc: EntryUtc.AddMinutes(30),
            availableUtc: EntryUtc.AddMinutes(40),
            published: false);

        DelayedIntradayBreakoutEvidence? result =
            FreshDelphiBreakoutEvidenceResolver.Resolve(
                [olderPublished, latestUnpublished],
                EntryUtc,
                DecisionUtc);

        result.ShouldNotBeNull();
        result.RunId.ShouldBe(latestUnpublished.RunId);
        result.IsBreakoutPublished.ShouldBeFalse();
    }

    [Fact]
    public void RunCreatedAfterDecisionBarIsNotYetAvailable()
    {
        FreshDelphiBreakoutEvidenceSnapshot available = Snapshot(
            1,
            startedUtc: EntryUtc.AddMinutes(10),
            availableUtc: DecisionUtc.AddMinutes(-5),
            published: true);
        FreshDelphiBreakoutEvidenceSnapshot future = Snapshot(
            2,
            startedUtc: DecisionUtc.AddMinutes(-2),
            availableUtc: DecisionUtc.AddSeconds(1),
            published: true);

        DelayedIntradayBreakoutEvidence? result =
            FreshDelphiBreakoutEvidenceResolver.Resolve(
                [available, future],
                EntryUtc,
                DecisionUtc);

        result.ShouldNotBeNull();
        result.RunId.ShouldBe(available.RunId);
        result.AvailableUtc.ShouldBe(available.AvailableUtc);
    }

    [Fact]
    public void OriginalOrPreEntryRunCannotBecomeFreshEvidence()
    {
        FreshDelphiBreakoutEvidenceSnapshot original = Snapshot(
            1,
            startedUtc: EntryUtc,
            availableUtc: EntryUtc.AddMinutes(1),
            published: true);

        FreshDelphiBreakoutEvidenceResolver.Resolve(
                [original],
                EntryUtc,
                DecisionUtc)
            .ShouldBeNull();
    }

    [Fact]
    public void InvalidRunCannotSupersedeLatestValidEvidence()
    {
        FreshDelphiBreakoutEvidenceSnapshot valid = Snapshot(
            1,
            startedUtc: EntryUtc.AddMinutes(10),
            availableUtc: EntryUtc.AddMinutes(20),
            published: true);
        FreshDelphiBreakoutEvidenceSnapshot invalid = Snapshot(
            2,
            startedUtc: EntryUtc.AddMinutes(30),
            availableUtc: EntryUtc.AddMinutes(40),
            published: true) with
        {
            IsValid = false
        };

        DelayedIntradayBreakoutEvidence? result =
            FreshDelphiBreakoutEvidenceResolver.Resolve(
                [valid, invalid],
                EntryUtc,
                DecisionUtc);

        result.ShouldNotBeNull();
        result.RunId.ShouldBe(valid.RunId);
    }

    private static FreshDelphiBreakoutEvidenceSnapshot Snapshot(
        int id,
        DateTime startedUtc,
        DateTime availableUtc,
        bool published) =>
        new(
            new Guid(id, 0, 0, new byte[8]),
            startedUtc,
            availableUtc,
            IsValid: true,
            IsBreakoutPublished: published,
            BreakoutProbability: published ? 0.60 : null,
            DirectionEdge: published ? 0.10 : null,
            DownProbability: published ? 0.349 : null);
}
