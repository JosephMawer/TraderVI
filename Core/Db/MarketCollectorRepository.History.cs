#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class MarketCollectorRepository
{
    // A need is short-lived. A successfully reserved minute can never trigger another history
    // request in this session, including after a failed response, collector crash or lease takeover.
    public async Task<bool> RequestHistoryAsync(CollectorRuleHistory demand, DateTime evaluatedUtc, CancellationToken ct)
    {
        if (demand.Minutes.Count == 0) return true;
        var expires = evaluatedUtc.AddMinutes(2);
        if (expires > demand.SessionCloseUtc) expires = demand.SessionCloseUtc;
        var json = JsonSerializer.Serialize(demand.Minutes);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await using var c = new SqlConnection(ConnectionString);
            await c.OpenAsync(timeout.Token);
            await c.ExecuteAsync(new CommandDefinition("""
IF @expires<=SYSUTCDATETIME() RETURN;
MERGE dbo.MarketCollectorHistoryNeed WITH(HOLDLOCK) AS n
USING (SELECT Symbol,StartUtc,[Rule] FROM OPENJSON(@json) WITH
    (Symbol nvarchar(20), StartUtc datetime2, [Rule] varchar(24))) AS d
ON n.SourceContract=@SourceContract AND n.SessionOpenUtc=@SessionOpenUtc AND n.Symbol=d.Symbol AND n.BarStartUtc=d.StartUtc
WHEN MATCHED AND n.RequestId IS NULL AND n.ExpiresUtc<@expires THEN UPDATE SET ExpiresUtc=@expires
WHEN NOT MATCHED THEN INSERT(SourceContract,SessionOpenUtc,Symbol,BarStartUtc,[Rule],ExpiresUtc)
    VALUES(@SourceContract,@SessionOpenUtc,d.Symbol,d.StartUtc,d.[Rule],@expires);
""", new { demand.SourceContract, demand.SessionOpenUtc, json, expires }, commandTimeout: 2, cancellationToken: timeout.Token));
            return true;
        }
        catch (SqlException) when (!ct.IsCancellationRequested) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    private static async Task<CollectorHistoryRange?> ClaimHistoryAsync(SqlConnection c, SqlTransaction tx,
        Core.Runtime.CollectorAttempt attempt, CancellationToken ct)
    {
        var starts = (await c.QueryAsync<DateTime>(new CommandDefinition("""
;WITH missing AS (
    SELECT TOP (@limit) n.BarStartUtc FROM dbo.MarketCollectorHistoryNeed n WITH(UPDLOCK,HOLDLOCK)
    WHERE n.SourceContract=@SourceContract AND n.SessionOpenUtc=@SessionOpenUtc AND n.Symbol=@Symbol
      AND n.RequestId IS NULL AND n.ExpiresUtc>SYSUTCDATETIME() AND n.BarStartUtc<DATEADD(MINUTE,-1,@ExpectedEndUtc)
      AND NOT EXISTS (SELECT 1 FROM dbo.MarketCollectorObservation o WHERE o.SourceContract=n.SourceContract
          AND o.SessionOpenUtc=n.SessionOpenUtc AND o.Symbol=n.Symbol AND o.BarStartUtc=n.BarStartUtc)
      AND NOT EXISTS (SELECT 1 FROM dbo.MarketCollectorConflict f WHERE f.SourceContract=n.SourceContract
          AND f.SessionOpenUtc=n.SessionOpenUtc AND f.Symbol=n.Symbol)
    ORDER BY n.BarStartUtc DESC
), numbered AS (
    SELECT BarStartUtc,ROW_NUMBER() OVER(ORDER BY BarStartUtc DESC) AS rn,MAX(BarStartUtc) OVER() AS newest FROM missing
)
UPDATE n SET RequestId=@RequestId OUTPUT inserted.BarStartUtc
FROM dbo.MarketCollectorHistoryNeed n JOIN numbered b ON b.BarStartUtc=n.BarStartUtc
WHERE n.SourceContract=@SourceContract AND n.SessionOpenUtc=@SessionOpenUtc AND n.Symbol=@Symbol
  AND b.BarStartUtc=DATEADD(MINUTE,CONVERT(int,1-b.rn),b.newest);
""", new { attempt.RequestId, attempt.SourceContract, attempt.Symbol, attempt.SessionOpenUtc, attempt.ExpectedEndUtc,
            limit = CollectorHistoryRange.MaximumMinutes }, tx, cancellationToken: ct))).AsList();
        return starts.Count == 0 ? null : new(Utc(starts.Min()), Utc(starts.Max()).AddMinutes(1));
    }
}
