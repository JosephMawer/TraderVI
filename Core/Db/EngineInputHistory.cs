#nullable enable
using Dapper;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Read-only history compatibility. Execution still selects only independent publications explicitly.</summary>
internal static class EngineInputHistory
{
    internal static Task<bool> InstalledAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct) =>
        c.QuerySingleAsync<bool>(new CommandDefinition("SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EngineInputPublication',N'U') IS NULL THEN 0 ELSE 1 END AS bit);",
            transaction: t, cancellationToken: ct));
    internal const string Source = """
(SELECT EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,LeaseFencingToken,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson,RecordedUtc FROM dbo.DelphiLiveEvaluation
 UNION ALL SELECT EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,LeaseFencingToken,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson,RecordedUtc FROM dbo.EngineInputPublication)
""";
}
