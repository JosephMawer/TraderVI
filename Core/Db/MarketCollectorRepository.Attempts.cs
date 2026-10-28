#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class MarketCollectorRepository : IIntradayCollectorStore
{
    Task<MarketCollectorLease?> IIntradayCollectorStore.AcquireAsync(string source, Guid owner, CancellationToken ct) => TryAcquireAsync(source, owner, ct: ct);
    Task<bool> IIntradayCollectorStore.RenewAsync(MarketCollectorLease lease, CancellationToken ct) => RenewAsync(lease, ct: ct);
    async Task IIntradayCollectorStore.SaveAsync(MarketCollectorLease lease, DelphiLiveCollectorBatch batch, CancellationToken ct)
    {
        await AppendAsync(lease, batch, ct);
    }

    public async Task<CollectorAttempt?> ReserveAsync(MarketCollectorLease lease, CollectorSubscriptionSnapshot subscriptions,
        string symbol, DateTime endUtc, int requestBudget, CancellationToken ct)
    {
        Lease(lease);
        if (!subscriptions.Symbols().Contains(symbol) || requestBudget is < 1 or > 200000 ||
            endUtc.Kind != DateTimeKind.Utc || endUtc.Ticks % TimeSpan.TicksPerMinute != 0 ||
            endUtc <= subscriptions.SessionOpenUtc || endUtc > subscriptions.SessionCloseUtc)
            throw new ArgumentException("Invalid bounded collector request intent.");
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await FenceAsync(c, tx, lease, ct);
        var result = await c.QuerySingleOrDefaultAsync<CollectorAttempt>(new CommandDefinition("""
DECLARE @now datetime2=SYSUTCDATETIME();
IF @now<=@EndUtc OR @now>=@SessionCloseUtc OR @now>=DATEADD(MINUTE,1,@EndUtc)
 OR EXISTS(SELECT 1 FROM dbo.MarketCollectorAttempt WITH(UPDLOCK,HOLDLOCK)
  WHERE SourceContract=@SourceContract AND SessionOpenUtc=@SessionOpenUtc AND Symbol=@Symbol AND ExpectedEndUtc=@EndUtc)
 OR (SELECT COUNT_BIG(*) FROM dbo.MarketCollectorAttempt WITH(UPDLOCK,HOLDLOCK)
  WHERE SourceContract=@SourceContract AND SessionOpenUtc=@SessionOpenUtc)>=@Budget
BEGIN
 SELECT RequestId,SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc,ExpectedEndUtc,ReservedUtc FROM dbo.MarketCollectorAttempt WHERE 1=0;
END
ELSE
BEGIN
 INSERT dbo.MarketCollectorAttempt(RequestId,SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc,ExpectedEndUtc,ReservedUtc,OwnerId,FencingToken)
 OUTPUT inserted.RequestId,inserted.SourceContract,inserted.Symbol,inserted.SessionOpenUtc,inserted.SessionCloseUtc,inserted.ExpectedEndUtc,inserted.ReservedUtc
 VALUES(@Id,@SourceContract,@Symbol,@SessionOpenUtc,@SessionCloseUtc,@EndUtc,@now,@OwnerId,@FencingToken);
END
""", new { Id = Guid.NewGuid(), lease.SourceContract, lease.OwnerId, lease.FencingToken, Symbol = symbol,
            subscriptions.SessionOpenUtc, subscriptions.SessionCloseUtc, EndUtc = endUtc, Budget = requestBudget }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return result is null ? null : result with { SessionOpenUtc = Utc(result.SessionOpenUtc), SessionCloseUtc = Utc(result.SessionCloseUtc),
            ExpectedEndUtc = Utc(result.ExpectedEndUtc), ReservedUtc = Utc(result.ReservedUtc) };
    }
}
