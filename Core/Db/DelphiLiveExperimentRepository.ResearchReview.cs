#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Core.Db;

public sealed partial class DelphiLiveExperimentRepository
{
    public Task RecordSessionReviewAsync(Guid sessionId, DateTime reviewedUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || reviewedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Review requires a frozen session and UTC cutoff.");
        return FencedWrite(lease, """
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveResearchSessionReview WHERE SessionId=@Session AND ReviewedUtc=@Reviewed)
 INSERT dbo.DelphiLiveResearchSessionReview(SessionId,ReviewedUtc) VALUES(@Session,@Reviewed);
""", cancellationToken, P("@Session", sessionId), P("@Reviewed", reviewedUtc));
    }
}
