#nullable enable
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Core.Db;

public sealed partial class DelphiLiveExperimentRepository : IDelphiLiveCorporateActionStore
{
    public async Task RecordCorporateActionAsync(DelphiLiveCorporateActionAudit audit, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (audit.AuditId == Guid.Empty || audit.AffectedThrough < audit.AffectedFrom || audit.RecordedUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(audit.Symbol) || audit.Symbol != audit.Symbol.Trim().ToUpperInvariant() || audit.Symbol.Length > 20 ||
            string.IsNullOrWhiteSpace(audit.AuthorizedBy) || audit.AuthorizedBy.Length > 128 ||
            string.IsNullOrWhiteSpace(audit.Reason) || audit.Reason.Length > 1024)
            throw new ArgumentException("A corporate-action audit requires symbol, affected dates, operator identity and reason.");
        await FencedWrite(lease, """
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveCorporateActionAudit WHERE AuditId=@Id)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveCorporateActionAudit WHERE AuditId=@Id AND AuditJson=@Json)
  THROW 51274, 'Corporate-action audits are append-only.', 1;
END
ELSE INSERT dbo.DelphiLiveCorporateActionAudit(AuditId,Symbol,AffectedFrom,AffectedThrough,RecordedUtc,AuditJson)
VALUES(@Id,@Symbol,@From,@Through,@Now,@Json);
""", cancellationToken, P("@Id", audit.AuditId), P("@Symbol", audit.Symbol, 20), P("@From", audit.AffectedFrom),
            P("@Through", audit.AffectedThrough), P("@Now", audit.RecordedUtc), P("@Json", Json(audit)));
    }

    public async Task<IReadOnlyList<string>> ReadAffectedSymbolsAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        if (through < from) throw new ArgumentException("Affected range is reversed.");
        await using var connection = await Open(cancellationToken);
        await using var command = new SqlCommand("SELECT DISTINCT Symbol FROM dbo.DelphiLiveCorporateActionAudit WHERE AffectedFrom<=@Through AND AffectedThrough>=@From ORDER BY Symbol;", connection);
        command.Parameters.AddRange([P("@From", from), P("@Through", through)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }
}
