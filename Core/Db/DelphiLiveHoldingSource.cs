#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Runtime;
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
    private readonly HoldingMarketSymbolResolver symbols;
    public DelphiLiveHoldingSource(IDelphiLiveLedgerStore ledger, EnginePaperObservationStore? observations = null,
        EngineRealObservationStore? realObservations = null)
        : this(ledger, observations, realObservations, new HoldingMarketSymbolResolver()) { }
    internal DelphiLiveHoldingSource(IDelphiLiveLedgerStore ledger, EnginePaperObservationStore? observations,
        EngineRealObservationStore? realObservations, HoldingMarketSymbolResolver symbols)
    { this.ledger = ledger; this.observations = observations; this.realObservations = realObservations; this.symbols = symbols; }
    internal Task<IReadOnlyList<DelphiLiveObservedHolding>> ResolveMarketSymbolsAsync(
        IReadOnlyList<DelphiLiveObservedHolding> holdings, IEnumerable<string> canonical,
        IReadOnlyList<HoldingSymbolAlias> aliases, IReadOnlyList<EngineRealObservation>? real = null,
        CancellationToken ct = default) => symbols.ResolveAsync(holdings, canonical, aliases, real, ct);
    public async Task<IReadOnlyList<DelphiLiveObservedHolding>> GetObservedHoldingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DelphiLiveObservedHolding>();
        IReadOnlyList<EngineRealObservation> real = [];
        if (realObservations is not null)
        {
            real = await realObservations.ReadAsync(cancellationToken);
            result.AddRange(real.SelectMany(r => r.Holdings));
        }
        bool paperAvailable = realObservations is null ||
            (await new EngineIntegrityRepository().ReadAccessAsync(Core.Runtime.EngineIntegrityScope.Paper, null, cancellationToken)).ProtectionAllowed;
        if (paperAvailable)
        {
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
        }
        await using var referencesConnection = new SqlConnection(ConnectionString);
        if (await referencesConnection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.SymbolAliases',N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken: cancellationToken)) == 0)
            throw new InvalidOperationException("Live monitoring requires reviewed migration 20260928_041_AddSymbolAliases.sql. Account history is unchanged.");
        using var references = await referencesConnection.QueryMultipleAsync(new CommandDefinition("""
            SELECT Symbol FROM dbo.Symbols;
            SELECT Source,Alias,Symbol FROM dbo.SymbolAliases;
            """, cancellationToken: cancellationToken));
        var symbols = await references.ReadAsync<string>();
        var aliases = (await references.ReadAsync<HoldingSymbolAlias>()).AsList();
        return await ResolveMarketSymbolsAsync(result, symbols, aliases, realObservations is null ? null : real, cancellationToken);
    }

    internal static async Task<IReadOnlyList<DelphiLiveObservedHolding>> ResolveSymbolsAsync(
        IReadOnlyList<DelphiLiveObservedHolding> holdings, IEnumerable<string> symbols,
        IReadOnlyList<HoldingSymbolAlias> aliases, IReadOnlyList<EngineRealObservation> real,
        Func<EngineRealObservation, Task<IReadOnlyList<TrackedHoldingMarketSymbol>>> readManaged)
        => await new HoldingMarketSymbolResolver(_ => Task.FromResult(real), (o, _) => readManaged(o))
            .ResolveAsync(holdings, symbols, aliases);
}
