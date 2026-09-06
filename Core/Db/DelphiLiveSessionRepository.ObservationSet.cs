#nullable enable

using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class DelphiLiveSessionRepository
{
    public async Task<DelphiLiveSessionContext> SynchronizeObservationSetAsync(
        Guid sessionId, DateTime nextBarEndUtc, DelphiLiveLease lease,
        IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(portfolios);
        if (sessionId == Guid.Empty || nextBarEndUtc.Kind != DateTimeKind.Utc ||
            nextBarEndUtc.Ticks % TimeSpan.FromMinutes(5).Ticks != 0)
            throw new ArgumentException("Observation synchronization requires an exact session checkpoint.");
        var observed = NormalizeObservedHoldings(await holdings.GetObservedHoldingsAsync(cancellationToken));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockSessionAsync(connection, transaction, cancellationToken);
        var session = await connection.QuerySingleAsync(new CommandDefinition(
            "SELECT TradingDate,SessionOpenUtc,SessionCloseUtc,ExpectedPriorCanonicalSessionDate FROM dbo.DelphiLiveSession WHERE SessionId=@Session;",
            new { Session = sessionId }, transaction, cancellationToken: cancellationToken));
        DateOnly tradingDate = DateOnly.FromDateTime((DateTime)session.TradingDate);
        DateTime openUtc = Utc((DateTime)session.SessionOpenUtc), closeUtc = Utc((DateTime)session.SessionCloseUtc);
        if (nextBarEndUtc <= openUtc || nextBarEndUtc > closeUtc)
            throw new ArgumentException("Observation checkpoint is outside the frozen regular session.");
        var sources = PlanObservationSources(observed, portfolios, openUtc, closeUtc);
        DateTime now = Utc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
            "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken)));
        var addedSymbols = (await connection.QueryAsync<string>(new CommandDefinition(SynchronizeSql, new
        {
            Session = sessionId, End = nextBarEndUtc, Close = closeUtc, Now = now,
            Lease = lease.LeaseId, Owner = lease.OwnerId, Fence = lease.FencingToken,
            Sources = JsonSerializer.Serialize(sources)
        }, transaction, cancellationToken: cancellationToken))).ToArray();
        await FreezeBaselinesAsync(connection, transaction, sessionId, tradingDate,
            DateOnly.FromDateTime((DateTime)session.ExpectedPriorCanonicalSessionDate), openUtc, now,
            addedSymbols, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadContextAsync(tradingDate, cancellationToken)
            ?? throw new InvalidOperationException("Frozen session disappeared after observation synchronization.");
    }

    internal static IReadOnlyList<DelphiLiveObservedHolding> NormalizeObservedHoldings(
        IReadOnlyList<DelphiLiveObservedHolding> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return observed.Select(h =>
        {
            if (h is null || string.IsNullOrWhiteSpace(h.Symbol) || h.Symbol.Trim().Length > 20 ||
                h.OwnerRecordId == Guid.Empty || string.IsNullOrWhiteSpace(h.Owner))
                throw new ArgumentException("Observed holdings require canonical symbol and owner identities.");
            return h with { Symbol = h.Symbol.Trim().ToUpperInvariant(),
                DelphiLiveMayAct = h.DelphiLiveMayAct && h.Owner == "DelphiLiveShadow" };
        }).ToArray();
    }

    internal static IReadOnlyList<DelphiLiveObservationSourcePlan> PlanObservationSources(
        IReadOnlyList<DelphiLiveObservedHolding> observed, IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios,
        DateTime sessionOpenUtc, DateTime sessionCloseUtc)
    {
        var normalized = NormalizeObservedHoldings(observed);
        var symbols = normalized.Select(h => h.Symbol).Concat(portfolios.SelectMany(p => p.Positions)
                .Where(p => p.ClosedUtc is null || (p.ClosedUtc >= sessionOpenUtc && p.ClosedUtc <= sessionCloseUtc))
                .Select(p => p.Symbol)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return symbols.Select(symbol =>
        {
            var holdingsForSymbol = normalized.Where(h => h.Symbol == symbol).ToArray();
            var positions = portfolios.SelectMany(p => p.Positions).Where(p => p.Symbol == symbol).ToArray();
            var pendingSells = portfolios.SelectMany(p => p.PendingActions)
                .Where(a => a.Intent.Symbol == symbol && a.Intent.Side == DelphiLiveActionSide.Sell).ToArray();
            bool live = positions.Any(p => p.ClosedUtc is null);
            bool carry = positions.Any(p => p.ClosedUtc >= sessionOpenUtc && p.ClosedUtc <= sessionCloseUtc);
            return new DelphiLiveObservationSourcePlan(symbol,
                holdingsForSymbol.Any(h => !h.DelphiLiveMayAct), live || holdingsForSymbol.Any(h => h.DelphiLiveMayAct),
                pendingSells.Length > 0, carry,
                JsonSerializer.Serialize(new
                {
                    observedOwners = holdingsForSymbol.Select(h => new { h.Owner, h.OwnerRecordId, h.DelphiLiveMayAct }),
                    positionIds = positions.Select(p => p.PositionId),
                    pendingExitIds = pendingSells.Select(a => a.Intent.ActionId),
                    soldThisSession = carry
                }));
        }).ToArray();
    }

    private static Task LockSessionAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
SET XACT_ABORT ON;
DECLARE @Result INT;
EXEC @Result=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',
    @LockOwner=N'Transaction',@LockTimeout=10000;
IF @Result<0 THROW 52230,'Delphi Live session store lock unavailable.',1;
""", transaction: transaction, cancellationToken: ct));

    internal const string SynchronizeSql = """
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@Lease AND OwnerId=@Owner AND FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 52231,'Observation synchronization lost the current host lease.',1;
IF @Now>=DATEADD(MINUTE,7,@End) THROW 52232,'Observation membership cannot be added after its collection deadline.',1;
IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle WHERE SessionId=@Session AND BarEndUtc=@End)
 THROW 52233,'Observation membership must be synchronized before the cycle expected set is frozen.',1;
DECLARE @ObservedSet TABLE(Symbol NVARCHAR(20) PRIMARY KEY,Tracked BIT,Live BIT,PendingSell BIT,Carry BIT,SourceJson NVARCHAR(MAX));
INSERT @ObservedSet SELECT Symbol,IsTrackedHolding,IsDelphiLiveHolding,HasPendingProtectiveSell,IsSessionCarryCandidate,SourceIdentityJson
FROM OPENJSON(@Sources) WITH(Symbol NVARCHAR(20),IsTrackedHolding BIT,IsDelphiLiveHolding BIT,
 HasPendingProtectiveSell BIT,IsSessionCarryCandidate BIT,SourceIdentityJson NVARCHAR(MAX));
DECLARE @Added TABLE(Symbol NVARCHAR(20));
INSERT dbo.DelphiLiveSessionSymbol
 (SessionSymbolId,SessionId,Symbol,IsFrozenDailyCandidate,IsXiuBenchmark,IsTrackedHolding,IsDelphiLiveHolding,
  HasPendingProtectiveSell,IsSessionCarryCandidate,FrozenSourceLensCount,BestFrozenSourceLensRank,
  RequiredFromBarEndUtc,RequiredThroughBarEndUtc,SourceIdentityJson,AddedUtc)
OUTPUT inserted.Symbol INTO @Added
SELECT NEWID(),@Session,s.Symbol,0,CASE WHEN s.Symbol=N'XIU' THEN 1 ELSE 0 END,s.Tracked,s.Live,s.PendingSell,
 CASE WHEN s.Symbol=N'XIU' THEN 0 ELSE s.Carry END,0,NULL,@End,@Close,s.SourceJson,
 CASE WHEN @Now>@Close THEN @Close ELSE @Now END
FROM @ObservedSet s WHERE NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveSessionSymbol t WHERE t.SessionId=@Session AND t.Symbol=s.Symbol);
-- Current ownership/protection flags may change, but prior required ranges,
-- frozen selection/ranks, source identity, and baseline evidence are retained.
UPDATE t SET IsTrackedHolding=COALESCE(s.Tracked,0),IsDelphiLiveHolding=COALESCE(s.Live,0),
 HasPendingProtectiveSell=COALESCE(s.PendingSell,0),
 IsSessionCarryCandidate=CASE WHEN t.IsXiuBenchmark=1 OR t.IsFrozenDailyCandidate=1 THEN 0
   WHEN s.Carry=1 OR t.IsSessionCarryCandidate=1 THEN 1 ELSE 0 END
FROM dbo.DelphiLiveSessionSymbol t LEFT JOIN @ObservedSet s ON s.Symbol=t.Symbol
WHERE t.SessionId=@Session
  AND (t.IsFrozenDailyCandidate=1 OR t.IsXiuBenchmark=1 OR t.IsSessionCarryCandidate=1 OR s.Symbol IS NOT NULL);
SELECT Symbol FROM @Added ORDER BY Symbol;
""";
}

internal sealed record DelphiLiveObservationSourcePlan(
    string Symbol, bool IsTrackedHolding, bool IsDelphiLiveHolding, bool HasPendingProtectiveSell,
    bool IsSessionCarryCandidate, string SourceIdentityJson);
