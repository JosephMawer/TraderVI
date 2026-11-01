using System;

namespace Core.Calibration;

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

public sealed record OfficialEvidenceIdentity(
    Guid StrategyVersionId,
    string StrategyVersionName,
    string InitialCodeCommit,
    string DecisionRef,
    int IncludedOfficialRuns,
    int ExcludedOfficialRuns);
