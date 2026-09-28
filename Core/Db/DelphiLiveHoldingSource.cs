#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class DelphiLiveHoldingSource : SQLBase, IDelphiLiveHoldingSource
{
    private readonly IDelphiLiveLedgerStore ledger;
    public DelphiLiveHoldingSource(IDelphiLiveLedgerStore ledger) => this.ledger = ledger;
    public async Task<IReadOnlyList<DelphiLiveObservedHolding>> GetObservedHoldingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DelphiLiveObservedHolding>();
        foreach (var position in await new ActivePositionRepository().GetActivePositions())
            if (TrackedPositionScope.Includes(position))
                result.Add(new(position.Symbol, position.ExecutionMode.ToString(), position.PositionId, false));
        await using var connection = new SqlConnection(ConnectionString);
        var shadow = await connection.QueryAsync(new CommandDefinition(
            "SELECT PositionId,Symbol FROM dbo.ShadowPosition WHERE Status=N'Open';", cancellationToken: cancellationToken));
        foreach (var row in shadow) result.Add(new((string)row.Symbol, "SystemShadow", (Guid)row.PositionId, false));
        DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ReviewedTsxSessionCalendar.Toronto));
        foreach (var portfolio in await ledger.GetPortfoliosForSessionAsync(date, cancellationToken))
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
