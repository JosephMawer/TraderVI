#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class DelphiLiveExperimentRepository : IDelphiLiveDiagnosticSource
{
    public async Task<IReadOnlyList<DelphiLiveDiagnosticEvaluation>> ReadChampionEvaluationsAsync(
        DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        ValidateDiagnosticRange(from, through);
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, null, ChampionDiagnosticSql, P("@From", from), P("@Through", through));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        var result = new List<DelphiLiveDiagnosticEvaluation>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDiagnosticProjection(reader));
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<DelphiLivePortfolioHistoryItem>> ReadPortfolioHistoryAsync(
        DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        ValidateDiagnosticRange(from, through);
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, null, PortfolioHistorySql, P("@From", from), P("@Through", through));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        var result = new List<DelphiLivePortfolioHistoryItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            // The snapshot already respects the historical report cutoff. The
            // generation boundary also clips the calculator's expected sessions.
            var portfolio = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(reader.GetString(0));
            DateOnly? ended = reader.IsDBNull(1) ? null : DateOnly.FromDateTime(reader.GetDateTime(1));
            result.Add(new(portfolio, ended));
        }
        return result.AsReadOnly();
    }

    internal static void ValidateDiagnosticRange(DateOnly from, DateOnly through)
    {
        if (through < from || through.DayNumber - from.DayNumber > 365)
            throw new ArgumentOutOfRangeException(nameof(through), "Diagnostic reads require an ordered range of at most 366 calendar dates.");
    }

    internal static DelphiLiveDiagnosticEvaluation ReadDiagnosticProjection(IDataRecord row)
    {
        // Read in ordinal order so the production reader can stream SQL MAX
        // fields without buffering the original input or its expanding bar history.
        Guid evaluation = row.GetGuid(0), session = row.GetGuid(1);
        string symbol = row.GetString(2);
        DateOnly date = DateOnly.FromDateTime(row.GetDateTime(3));
        DateTime end = DateTime.SpecifyKind(row.GetDateTime(4), DateTimeKind.Utc);
        bool valid = row.GetBoolean(5), mature = row.GetBoolean(6), confirmed = row.GetBoolean(7);
        return new(evaluation, session, symbol, date, end,
            ReadDiagnosticJson<DelphiLiveTrueRangeRulerMeasurement>(row, 8), valid, mature, confirmed,
            ReadDiagnosticJson<DelphiLivePersistenceJudgment>(row, 9),
            ReadDiagnosticJson<DelphiLivePriceMovementJudgment>(row, 10),
            ReadDiagnosticJson<DelphiLiveVolumeSupportJudgment>(row, 11),
            ReadDiagnosticJson<DelphiLivePriceStructureJudgment>(row, 12),
            ReadDiagnosticJson<DelphiLivePriceMovementMeasurements>(row, 13),
            ReadDiagnosticJson<DelphiLiveDataConfidence>(row, 14),
            ReadDiagnosticJson<DelphiLiveMomentumJudgment>(row, 15),
            ReadDiagnosticJson<DelphiLiveMomentumJudgment>(row, 16),
            ReadDiagnosticJson<DelphiLiveSafetyEvaluation>(row, 17),
            ReadDiagnosticJson<DelphiLiveSafetyInput>(row, 18));
    }

    private static T ReadDiagnosticJson<T>(IDataRecord row, int ordinal) =>
        DelphiLiveLedgerJson.Deserialize<T>(row.GetString(ordinal));

    internal const string ChampionDiagnosticSql = """
SELECT e.EvaluationId,e.SessionId,e.Symbol,s.TradingDate,e.BarEndUtc,e.ObservedOnTime,
 CAST(CASE JSON_VALUE(e.ResultJson,'$.familiesMature') WHEN N'true' THEN 1 WHEN N'false' THEN 0 ELSE NULL END AS BIT) AS FamiliesMature,
 e.ConfirmedLiveEligible,
 JSON_QUERY(e.InputJson,'$.volatilityRulers.tenSession') AS TenSessionRulerJson,
 JSON_QUERY(e.ResultJson,'$.persistence') AS PersistenceJson,
 JSON_QUERY(e.ResultJson,'$.priceMovement') AS PriceMovementJson,
 JSON_QUERY(e.ResultJson,'$.volumeSupport') AS VolumeSupportJson,
 JSON_QUERY(e.ResultJson,'$.priceStructure') AS PriceStructureJson,
 JSON_QUERY(e.ResultJson,'$.priceMovementMeasurements') AS PriceMovementMeasurementsJson,
 JSON_QUERY(e.ResultJson,'$.nextState.confidence') AS ConfidenceJson,
 JSON_QUERY(e.InputJson,'$.previousState.momentum') AS PreviousMomentumJson,
 JSON_QUERY(e.ResultJson,'$.nextState.momentum') AS CurrentMomentumJson,
 JSON_QUERY(e.ResultJson,'$.safety') AS SafetyJson,
 JSON_QUERY(e.ResultJson,'$.safetyInput') AS SafetyInputJson
FROM dbo.DelphiLiveEvaluation e
JOIN dbo.DelphiLiveSession s ON s.SessionId=e.SessionId
JOIN dbo.DelphiLiveSessionPolicy p ON p.SessionId=e.SessionId AND p.DelphiLivePolicyVersionId=e.PolicyVersionId
 AND p.RoleSlot=0 AND p.PolicyRole=N'OperationalChampion'
WHERE s.TradingDate BETWEEN @From AND @Through
ORDER BY s.TradingDate,e.BarEndUtc,e.Symbol;
""";

    internal const string PortfolioHistorySql = """
SELECT revision.SnapshotJson,g.EndExclusiveTradingDate
FROM dbo.DelphiLivePortfolioLedger l
JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
CROSS APPLY
(
 SELECT TOP(1) r.SnapshotJson FROM dbo.DelphiLivePortfolioRevision r
 WHERE r.PortfolioId=l.PortfolioId
  AND r.PersistedUtc<DATEADD(DAY,1,CAST(@Through AS DATETIME2))
  AND (g.EndExclusiveTradingDate IS NULL OR r.PersistedUtc<CAST(g.EndExclusiveTradingDate AS DATETIME2))
 ORDER BY r.Revision DESC
) revision
WHERE g.EffectiveTradingDate<=@Through
 AND (g.EndExclusiveTradingDate IS NULL OR g.EndExclusiveTradingDate>@From)
ORDER BY g.EffectiveTradingDate,g.PortfolioRole,l.PortfolioId;
""";
}
