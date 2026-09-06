#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveCorporateActionAudit(Guid AuditId, string Symbol, DateOnly AffectedFrom,
    DateOnly AffectedThrough, DateTime RecordedUtc, string AuthorizedBy, string Reason);
public interface IDelphiLiveCorporateActionStore
{
    Task RecordCorporateActionAsync(DelphiLiveCorporateActionAudit audit, DelphiLiveLease lease, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ReadAffectedSymbolsAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
}
