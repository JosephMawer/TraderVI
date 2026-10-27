#nullable enable
using Core.Trader.Promotion;
using Dapper;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Exact saved market evidence only. Never fetches, fills gaps, or changes financial records.</summary>
public sealed class BrokerOngoingBoundaryRepository
{
    public async Task<ImmutableArray<BrokerRiskHoldingBar>> ReadAsync(ImmutableArray<string> symbols,
        DateTime startUtc, CancellationToken ct)
    {
        if (symbols.IsEmpty) return [];
        await using var c = await TradingRecommendationStorage.Open(ct);
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
SELECT b.Symbol,b.EvidenceBarId,b.EventUtc,o.ReceivedUtc,b.[Open],b.High,b.Low,b.[Close]
FROM dbo.IntradayEvidenceBar b JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
WHERE b.Symbol IN @Symbols AND b.EventUtc=@Start AND b.IntervalMinutes=5
 AND o.AuditState IN(N'Valid',N'Degraded') AND o.EvidenceSchemaVersion=@Schema AND o.Provider=@Provider
 AND o.SourceContractVersion=@SourceContract
 AND o.ReceivedUtc<=@Now AND b.CreatedUtc<=@Now AND DATEADD(minute,5,b.EventUtc)<=o.ReceivedUtc
 AND NOT EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict f
 WHERE f.Symbol=b.Symbol AND f.IntervalMinutes=b.IntervalMinutes AND f.ExistingBarEventUtc=b.EventUtc)
""", new { Symbols = symbols.ToArray(), Start = startUtc, Now = DateTime.UtcNow,
            Schema = Core.TMX.Models.Domain.IntradayEvidenceVersions.Schema,
            Provider = Core.TMX.Models.Domain.IntradayEvidenceVersions.Provider,
            SourceContract = Core.TMX.Models.Domain.IntradayEvidenceVersions.SourceContract }, cancellationToken: ct));
        return rows.Select(r => new BrokerRiskHoldingBar(r.Symbol, new(r.EvidenceBarId,
            DateTime.SpecifyKind(r.EventUtc, DateTimeKind.Utc), DateTime.SpecifyKind(r.ReceivedUtc, DateTimeKind.Utc),
            5, r.Open, r.High, r.Low, r.Close))).ToImmutableArray();
    }
    private sealed record Row(string Symbol, Guid EvidenceBarId, DateTime EventUtc, DateTime ReceivedUtc,
        decimal Open, decimal High, decimal Low, decimal Close);
}
