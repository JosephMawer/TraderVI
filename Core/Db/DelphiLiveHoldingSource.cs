#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class DelphiLiveHoldingSource : SQLBase, IDelphiLiveHoldingSource
{
    private readonly IDelphiLiveLedgerStore ledger;
    private readonly EnginePaperObservationStore? observations;
    private readonly EngineRealObservationStore? realObservations;
    public DelphiLiveHoldingSource(IDelphiLiveLedgerStore ledger, EnginePaperObservationStore? observations = null,
        EngineRealObservationStore? realObservations = null)
    { this.ledger = ledger; this.observations = observations; this.realObservations = realObservations; }
    public async Task<IReadOnlyList<DelphiLiveObservedHolding>> GetObservedHoldingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DelphiLiveObservedHolding>();
        if (realObservations is not null)
            result.AddRange((await realObservations.ReadAsync(cancellationToken)).SelectMany(r => r.Holdings));
        foreach (var position in await new ActivePositionRepository().GetActivePositions())
            if (TrackedPositionScope.Includes(position))
                result.Add(new(position.Symbol, position.ExecutionMode.ToString(), position.PositionId, false));
        await using var connection = new SqlConnection(ConnectionString);
        var shadow = await connection.QueryAsync(new CommandDefinition(
            "SELECT PositionId,Symbol FROM dbo.ShadowPosition WHERE Status=N'Open';", cancellationToken: cancellationToken));
        foreach (var row in shadow) result.Add(new((string)row.Symbol, "SystemShadow", (Guid)row.PositionId, false));
        DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ReviewedTsxSessionCalendar.Toronto));
        var portfolios = observations is null
            ? (await ledger.GetPortfoliosForSessionAsync(date, cancellationToken)).Select(p => DelphiLiveObservationPortfolio.From(p)).ToArray()
            : realObservations is null ? await observations.ReadAsync(date, cancellationToken)
                : await observations.ReadForCollectionAsync(date, cancellationToken);
        foreach (var portfolio in portfolios)
            foreach (var position in portfolio.OpenPositions)
                result.Add(new(position.Symbol, "DelphiLiveShadow", position.PositionId, true));
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.SymbolAliases',N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: cancellationToken)) == 0)
            throw new InvalidOperationException("Live monitoring requires reviewed migration 20260928_041_AddSymbolAliases.sql. Account history is unchanged.");
        using var references = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT Symbol FROM dbo.Symbols;
            SELECT Source,Alias,Symbol FROM dbo.SymbolAliases;
            """, cancellationToken: cancellationToken));
        var symbols = await references.ReadAsync<string>();
        var aliases = (await references.ReadAsync<HoldingSymbolAlias>()).AsList();
        return DelphiLiveHoldingSymbols.Resolve(result, symbols, aliases);
    }
}
