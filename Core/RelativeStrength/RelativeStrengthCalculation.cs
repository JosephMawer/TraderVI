#nullable enable

using System;
using System.Collections.Generic;

namespace Core.RelativeStrength;

/// <summary>
/// One closing price identified by its completed market session.
/// Relative-strength inputs retain dates so callers cannot accidentally align series by list position.
/// </summary>
public readonly record struct RelativeStrengthPricePoint(DateOnly Date, double Close);

/// <summary>
/// Describes how stock and sector observations cover the canonical market sessions examined.
/// Leading missing sessions can mean insufficient history. A gap after the first observation or an
/// ambiguous duplicate identifies an alignment defect.
/// </summary>
public sealed record RelativeStrengthCoverage(
    DateOnly TargetDate,
    int RequiredCanonicalSessions,
    IReadOnlyList<DateOnly> CanonicalSessionsExamined,
    IReadOnlyList<DateOnly> MissingStockSessions,
    IReadOnlyList<DateOnly> MissingSectorSessions,
    IReadOnlyList<DateOnly> UnavailableMarketCloseSessions,
    IReadOnlyList<DateOnly> DuplicateStockSessions,
    IReadOnlyList<DateOnly> DuplicateSectorSessions,
    IReadOnlyList<DateOnly> DuplicateMarketSessions,
    bool HasTargetMarketSession,
    bool HasTargetMarketClose,
    bool HasStockGapAfterFirstObservation,
    bool HasSectorGapAfterFirstObservation)
{
    public bool HasAlignmentGap =>
        HasStockGapAfterFirstObservation ||
        HasSectorGapAfterFirstObservation ||
        DuplicateStockSessions.Count > 0 ||
        DuplicateSectorSessions.Count > 0 ||
        DuplicateMarketSessions.Count > 0 ||
        UnavailableMarketCloseSessions.Count > 0;

    public bool HasFullCoverage =>
        HasTargetMarketSession &&
        HasTargetMarketClose &&
        CanonicalSessionsExamined.Count >= RequiredCanonicalSessions &&
        MissingStockSessions.Count == 0 &&
        MissingSectorSessions.Count == 0 &&
        UnavailableMarketCloseSessions.Count == 0 &&
        DuplicateStockSessions.Count == 0 &&
        DuplicateSectorSessions.Count == 0 &&
        DuplicateMarketSessions.Count == 0;
}

/// <summary>
/// Relative-strength features plus the date-coverage facts used to compute them.
/// </summary>
public sealed record RelativeStrengthCalculationResult(
    RelativeStrengthRow Features,
    RelativeStrengthCoverage Coverage);
