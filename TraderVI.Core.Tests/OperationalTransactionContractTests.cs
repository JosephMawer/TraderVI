#nullable enable

using Core.Db;
using Core.Trader;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class OperationalTransactionContractTests
{
    [Fact]
    public void TrackedPositionOpening_RejectsInvalidRequestsBeforeDatabaseAccess()
    {
        var request = Request() with { Shares = 0 };

        Should.Throw<ArgumentOutOfRangeException>(() =>
            TrackedPositionOpeningRepository.Validate(request));
    }

    [Fact]
    public void DelphiPublication_AllowsSuccessfulZeroResultReplacement()
    {
        Should.NotThrow(() =>
            DelphiOperationalPublicationRepository.Validate(
                new DateTime(2026, 9, 3),
                Array.Empty<DelphiOperationalPick>()));
    }

    [Fact]
    public void DelphiPublication_RejectsDuplicateRankWithinLens()
    {
        DateTime date = new(2026, 9, 3);
        DelphiOperationalPick first = Pick(date, "CS", rank: 1);
        DelphiOperationalPick second = Pick(date, "GGD", rank: 1);

        Should.Throw<ArgumentException>(() =>
            DelphiOperationalPublicationRepository.Validate(date, [first, second]));
    }

    [Fact]
    public void DelphiPublication_AllowsSameRankAcrossDifferentLenses()
    {
        DateTime date = new(2026, 9, 3);
        DelphiOperationalPick continuation = Pick(date, "CS", rank: 1);
        DelphiOperationalPick breakout = Pick(date, "GGD", rank: 1) with { Lens = "Breakout" };

        Should.NotThrow(() =>
            DelphiOperationalPublicationRepository.Validate(date, [continuation, breakout]));
    }

    private static TrackedPositionOpenRequest Request() => new(
        "CS",
        new DateTime(2026, 9, 3, 10, 0, 0),
        35,
        15.73m,
        "Operator real fill",
        "test",
        OriginalPickId: null,
        EntryComposite: null,
        StopLossPrice: 14.16m,
        WarningPrice: 14.47m,
        TrackedExecutionMode.Real,
        "TFSA");

    private static DelphiOperationalPick Pick(DateTime date, string symbol, int rank) => new(
        Guid.NewGuid(),
        date,
        symbol,
        rank,
        "Buy",
        0.5,
        BreakoutProb: 0.6,
        DirectionProb: 0.7,
        VolExpansionProb: 0.5,
        RelStrengthProb: null,
        ExpectedReturn: 0.02,
        SuggestedSize: null,
        AllocationPercent: null,
        StrategyVersionId: null,
        Notes: null,
        Lens: "Continuation",
        Dossier: null);
}
