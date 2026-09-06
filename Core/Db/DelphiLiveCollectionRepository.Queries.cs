#nullable enable

using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class DelphiLiveCollectionRepository
{
    /// <summary>
    /// Loads canonical facts through an exact endpoint with their original
    /// operational eligibility. A later research receipt never repairs a miss.
    /// Callers use the current continuity epoch separately for rolling families;
    /// the complete originally-on-time session path remains available for VWAP.
    /// </summary>
    public async Task<IReadOnlyList<DelphiLiveFiveMinuteBar>> GetSessionBarsAsync(
        Guid sessionId, DateTime throughBarEndUtc, CancellationToken cancellationToken = default)
    {
        RequireUtc(throughBarEndUtc, nameof(throughBarEndUtc));
        const string sql = """
SELECT b.EvidenceBarId,s.Symbol,d.TradingDate,s.ExpectedBarStartUtc,s.ExpectedBarEndUtc,
    b.[Open],b.High,b.Low,b.[Close],b.Volume,r.ReceivedUtc,c.Provider,c.SourceContractVersion,
    CAST(CASE WHEN s.OperationallyUsable=1 AND r.OperationallyUsable=1
        AND s.ReceivedUtc<s.DeadlineUtc AND s.SettledUtc<s.DeadlineUtc THEN 1 ELSE 0 END AS BIT)
FROM dbo.IntradayCollectionSlot s
JOIN dbo.IntradayCollectionCycle c ON c.CycleId=s.CycleId
JOIN dbo.DelphiLiveSession d ON d.SessionId=s.SessionId
CROSS APPLY
 (
    SELECT TOP (1) p.EvidenceBarId,p.ReceivedUtc,p.OperationallyUsable
    FROM dbo.IntradayCollectionReceipt p
    WHERE p.CollectionSlotId=s.CollectionSlotId AND p.EvidenceBarId IS NOT NULL
      AND p.Disposition IN (N'OperationalOnTime',N'IdenticalDuplicate',N'LateResearchOnly')
    ORDER BY p.OperationallyUsable DESC,p.ReceivedUtc,p.ReceiptId
 ) r
JOIN dbo.IntradayEvidenceBar b ON b.EvidenceBarId=r.EvidenceBarId AND b.Symbol=s.Symbol
    AND b.IntervalMinutes=s.IntervalMinutes AND b.EventUtc=s.ExpectedBarStartUtc
WHERE s.SessionId=@SessionId AND s.ExpectedBarEndUtc<=@Through
  AND c.CollectorVersion=N'IntradayEvidenceCollectorV3' AND c.SourceContractVersion=1
  AND NOT EXISTS (SELECT 1 FROM dbo.IntradayEvidenceConflict x WHERE x.CollectionSlotId=s.CollectionSlotId)
ORDER BY s.Symbol,s.ExpectedBarEndUtc;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@SessionId", SqlDbType.UniqueIdentifier, sessionId));
        command.Parameters.Add(P("@Through", SqlDbType.DateTime2, throughBarEndUtc));
        var result = new List<DelphiLiveFiveMinuteBar>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0),reader.GetString(1),DateOnly.FromDateTime(reader.GetDateTime(2)),
                Utc(reader.GetDateTime(3)),Utc(reader.GetDateTime(4)),reader.GetDecimal(5),reader.GetDecimal(6),
                reader.GetDecimal(7),reader.GetDecimal(8),reader.GetInt64(9),Utc(reader.GetDateTime(10)),
                reader.GetString(11),reader.GetInt32(12),reader.GetBoolean(13)
                    ? DelphiLiveEvidenceDisposition.OperationalOnTime : DelphiLiveEvidenceDisposition.LateResearchOnly));
        return result.AsReadOnly();
    }

    public async Task<DateTime?> GetLastOperationalCycleEndAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT MAX(BarEndUtc) FROM dbo.IntradayCollectionCycle
WHERE SessionId=@SessionId AND StartedUtc IS NOT NULL AND CycleStatus IN (N'Completed',N'DeadlineExceeded',N'Cancelled');
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@SessionId", SqlDbType.UniqueIdentifier, sessionId));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateTime end ? Utc(end) : null;
    }

    public async Task<IReadOnlyList<DelphiLiveStoredCollectionSlot>> GetCycleSlotsAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT s.CollectionSlotId,s.Symbol,s.ExpectedBarEndUtc,s.Disposition,s.DispositionCode,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.IntradayEvidenceConflict x WHERE x.CollectionSlotId=s.CollectionSlotId)
        THEN 0 ELSE s.OperationallyUsable END AS BIT),s.MissedOperationalDeadline,s.ReceivedUtc,s.SettledUtc,
    s.PollObservationId,s.EvidenceBarId,s.IsXiuBenchmark
FROM dbo.IntradayCollectionSlot s WHERE s.CycleId=@CycleId ORDER BY s.PriorityOrdinal,s.Symbol;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@CycleId", SqlDbType.UniqueIdentifier, cycleId));
        var result = new List<DelphiLiveStoredCollectionSlot>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0),reader.GetString(1),Utc(reader.GetDateTime(2)),reader.GetString(3),
                reader.IsDBNull(4)?null:reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),
                reader.IsDBNull(7)?null:Utc(reader.GetDateTime(7)),reader.IsDBNull(8)?null:Utc(reader.GetDateTime(8)),
                reader.IsDBNull(9)?null:reader.GetGuid(9),reader.IsDBNull(10)?null:reader.GetGuid(10),reader.GetBoolean(11)));
        return result.AsReadOnly();
    }
}

public sealed record DelphiLiveStoredCollectionSlot(
    Guid CollectionSlotId, string Symbol, DateTime BarEndUtc, string Disposition, string? DispositionCode,
    bool OperationallyUsable, bool MissedOperationalDeadline, DateTime? ReceivedUtc, DateTime? SettledUtc,
    Guid? PollObservationId, Guid? EvidenceBarId, bool IsXiuBenchmark);
