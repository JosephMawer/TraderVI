using Core.Trader;
using Core.Trader.Gates;
using System;
using System.Collections.Generic;

namespace Core.Calibration;

public static class CalibrationSchemaVersions
{
    public const int Feature = 1;
    public const int CandidateSnapshot = 1;
    public const int LensTrace = 1;
}

public enum CalibrationRunPurpose
{
    OfficialPaper,
    ExploratoryReplay,
    LegacyReconstruction
}

public enum CalibrationAuditState
{
    Valid,
    Degraded,
    Invalid
}

public enum CalibrationOutcomeMaturityState
{
    Pending,
    Matured,
    NoEntry
}

public sealed record CodeProvenance(
    string Commit,
    string Source,
    string WorkingTreeState);

public sealed record ModelArtifactProvenance(
    Guid ModelId,
    string TaskType,
    string ModelKind,
    string InputSchema,
    string? FeatureSet,
    DateTime? TrainedFromUtc,
    DateTime? TrainedToUtc,
    string ArtifactSha256);

public sealed record CalibrationRunEvidence(
    Guid RunId,
    CalibrationRunPurpose Purpose,
    DateTime RecommendationDate,
    DateTime MarketDataAsOf,
    DateTime StartedUtc,
    Guid? StrategyVersionId,
    string StrategyConfigJson,
    string ModelSnapshotJson,
    string RunContextJson,
    CodeProvenance Code,
    CalibrationAuditState AuditState,
    string? AuditMessage,
    int SymbolsDiscovered,
    int SymbolsModelEvaluated,
    int SkippedHistory,
    int SkippedStaleHistory,
    int SkippedUnaffordable,
    int SkippedLowPrice,
    int SkippedLowVolume,
    int SkippedLeveragedEtp);

public sealed record CalibrationCandidateEvidence(
    Guid CandidateId,
    Guid RunId,
    string Symbol,
    DateTime ObservationDate,
    float ObservationOpen,
    float ObservationHigh,
    float ObservationLow,
    float ObservationClose,
    long ObservationVolume,
    double? UpProbability,
    double? DownProbability,
    double? BreakoutProbability,
    double? VolExpansionProbability,
    double DirectionEdge,
    double CompositeScore,
    double? RsCompositeScore,
    double? RsCompositeScoreZ,
    string? ObvState,
    double ObvTilt,
    string SnapshotJson);

public sealed record CalibrationLensEvidence(
    Guid LensEvaluationId,
    Guid CandidateId,
    string Lens,
    string Direction,
    bool IsEligible,
    int Rank,
    double RankingKey,
    bool IsPublished,
    string? FirstFailedGate,
    string GateTraceJson);

public sealed record CalibrationEvidenceBatch(
    CalibrationRunEvidence Run,
    IReadOnlyList<CalibrationCandidateEvidence> Candidates,
    IReadOnlyList<CalibrationLensEvidence> LensEvaluations);

public sealed record CalibrationRunInfo(
    Guid RunId,
    CalibrationRunPurpose Purpose,
    DateTime RecommendationDate,
    DateTime MarketDataAsOf,
    DateTime StartedUtc,
    DateTime CreatedUtc,
    Guid? StrategyVersionId,
    string StrategyConfigJson,
    string ModelSnapshotJson,
    string RunContextJson,
    string CodeCommit,
    CalibrationAuditState AuditState,
    string? AuditMessage,
    int SymbolsDiscovered,
    int SymbolsModelEvaluated,
    int SkippedHistory,
    int SkippedStaleHistory,
    int SkippedUnaffordable,
    int SkippedLowPrice,
    int SkippedLowVolume,
    int SkippedLeveragedEtp);

public sealed record CalibrationCandidateRunInfo(
    string Symbol,
    double? UpProbability,
    double? DownProbability,
    double? BreakoutProbability,
    double? VolExpansionProbability,
    double DirectionEdge,
    double CompositeScore,
    string? ObvState,
    string SnapshotJson,
    string GateTraceJson);

public sealed record CalibrationObvStateCount(string State, int Count);

public sealed record CandidateSnapshotPayload(
    int SchemaVersion,
    IReadOnlyList<SignalResult> Signals,
    object? RelativeStrength,
    object? Obv,
    object? MarketContext);

public sealed record LensTracePayload(
    int SchemaVersion,
    IReadOnlyList<GateTraceEntry> Gates);
