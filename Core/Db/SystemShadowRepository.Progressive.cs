#nullable enable
using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class SystemShadowRepository
{
    private bool? progressiveSchemaInstalled;
    public async Task<bool> HasProgressiveProtectionSchemaAsync(CancellationToken ct = default)
    {
        if (progressiveSchemaInstalled.HasValue) return progressiveSchemaInstalled.Value;
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.ShadowProgressiveProtection',N'U') IS NULL THEN 0 ELSE 1 END", c);
        progressiveSchemaInstalled = Convert.ToInt32(await q.ExecuteScalarAsync(ct)) == 1;
        return progressiveSchemaInstalled.Value;
    }

    public async Task<ProgressiveProfitProtectionState?> ReadProgressiveProtectionAsync(Guid positionId, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var q = new SqlCommand("SELECT StateJson FROM dbo.ShadowProgressiveProtection WHERE PositionId=@PositionId", c);
        q.Parameters.Add(P("@PositionId", SqlDbType.UniqueIdentifier, positionId));
        var json = await q.ExecuteScalarAsync(ct) as string;
        if (json is null) return null;
        var state = JsonSerializer.Deserialize<ProgressiveProfitProtectionState>(json) ?? throw new InvalidOperationException("Protection state is empty.");
        ProgressiveProfitProtection.Validate(state);
        return state;
    }

    public async Task SaveProgressiveProtectionAsync(Guid positionId, ProgressiveProfitProtectionState state, CancellationToken ct = default)
    {
        ProgressiveProfitProtection.Validate(state);
        const string sql = """
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.ShadowPosition WITH(UPDLOCK,HOLDLOCK) WHERE PositionId=@PositionId AND Status=N'Open')
    THROW 51000, 'Progressive protection needs an open position.', 1;
IF EXISTS(SELECT 1 FROM dbo.ShadowProgressiveProtection WITH(UPDLOCK,HOLDLOCK) WHERE PositionId=@PositionId AND
    ((JSON_VALUE(StateJson,'$.StopPrice') IS NOT NULL AND (@StopPrice IS NULL OR TRY_CONVERT(decimal(19,6),JSON_VALUE(StateJson,'$.StopPrice')) > @StopPrice)) OR
     (JSON_VALUE(StateJson,'$.HighestClose') IS NOT NULL AND (@High IS NULL OR TRY_CONVERT(decimal(19,6),JSON_VALUE(StateJson,'$.HighestClose')) > @High)) OR
     (JSON_VALUE(StateJson,'$.TightestGap') IS NOT NULL AND (@Gap IS NULL OR TRY_CONVERT(decimal(19,6),JSON_VALUE(StateJson,'$.TightestGap')) < @Gap)) OR
     (JSON_VALUE(StateJson,'$.ExitTriggered')='true' AND @ExitTriggered=0)))
    THROW 51000, 'Progressive protection cannot be loosened or an exit cleared.', 1;
UPDATE dbo.ShadowProgressiveProtection SET StateJson=@Json,UpdatedUtc=SYSUTCDATETIME() WHERE PositionId=@PositionId;
IF @@ROWCOUNT=0 INSERT dbo.ShadowProgressiveProtection(PositionId,StateJson) VALUES(@PositionId,@Json);
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,PositionId,OccurredUtc,EventType,ReasonCode,DetailsJson)
SELECT NEWID(),PortfolioId,PositionId,SYSUTCDATETIME(),N'Risk',N'ProgressiveProfitProtectionV1',@Json
FROM dbo.ShadowPosition WHERE PositionId=@PositionId;
COMMIT;
""";
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var q = new SqlCommand(sql, c);
        q.Parameters.AddRange([P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
            P("@Json", SqlDbType.NVarChar, JsonSerializer.Serialize(state), -1),
            P("@StopPrice", SqlDbType.Decimal, state.StopPrice, precision: 19, scale: 6),
            P("@High", SqlDbType.Decimal, state.HighestClose, precision: 19, scale: 6),
            P("@Gap", SqlDbType.Decimal, state.TightestGap, precision: 19, scale: 6),
            P("@ExitTriggered", SqlDbType.Bit, state.ExitTriggered)]);
        await q.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ProgressiveHistoricalClose>> ReadProgressiveClosesAsync(SystemShadowPositionInfo position,
        DateTime? afterEndUtc, DateTime throughUtc, CancellationToken ct = default)
    {
        const string sql = """
SELECT DATEADD(minute,5,b.EventUtc),b.[Close],o.ReceivedUtc,CAST(buy.AverageCost AS decimal(19,6))
FROM dbo.IntradayEvidenceBar b JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
CROSS APPLY(SELECT SUM(CONVERT(decimal(28,6),a.Shares)*a.AdjustedFillPrice)/SUM(a.Shares) AverageCost
    FROM dbo.ShadowOrder a WHERE a.PositionId=@PositionId AND a.Side=N'Buy' AND a.Status=N'Filled'
    AND a.FillUtc<=DATEADD(minute,5,b.EventUtc) AND a.UpdatedUtc<=o.ReceivedUtc) buy
WHERE b.Symbol=@Symbol AND b.IntervalMinutes=5 AND DATEADD(minute,5,b.EventUtc)>@After
    AND b.CreatedUtc<=@Through AND o.ReceivedUtc>=DATEADD(minute,5,b.EventUtc) AND o.ReceivedUtc<=@Through
    AND o.EvidenceSchemaVersion=1 AND o.Provider=N'TMX'
    AND o.AuditState IN(N'Valid',N'Degraded') AND buy.AverageCost>0
    AND NOT EXISTS(SELECT 1 FROM dbo.IntradayPollObservation bad WHERE bad.Symbol=b.Symbol AND bad.IntervalMinutes=5
        AND bad.AuditState=N'Invalid' AND bad.ReceivedUtc<=@Through AND bad.AuditCode=N'CompletedBarConflict'
        AND b.EventUtc>=bad.RequestedStartUtc AND b.EventUtc<bad.RequestedEndUtc)
ORDER BY b.EventUtc;
""";
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var q = new SqlCommand(sql, c);
        q.Parameters.AddRange([P("@PositionId", SqlDbType.UniqueIdentifier, position.PositionId),
            P("@Symbol", SqlDbType.NVarChar, position.Symbol, 20),
            P("@After", SqlDbType.DateTime2, afterEndUtc ?? position.EntryUtc), P("@Through", SqlDbType.DateTime2, throughUtc)]);
        var rows = new List<ProgressiveHistoricalClose>();
        await using var reader = await q.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(Utc(reader.GetDateTime(0)), reader.GetDecimal(1), Utc(reader.GetDateTime(2)), reader.GetDecimal(3)));
        return rows;
    }
}
