#nullable enable
using Core.ML.Engine.Profit;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class StrategyModelBindingRepository : SQLBase
{
    public async Task<StoredProfitModelSet?> GetAsync(Guid strategyVersionId)
    {
        // Existing strategies can run before the additive migration. ADR-0056
        // still fails closed in the workflow if its assignment is unavailable.
        var rows = await ExecuteReaderAsync(@"
IF OBJECT_ID(N'dbo.StrategyModelBinding', N'U') IS NOT NULL
    SELECT ModelSetJson, ModelSetSha256 FROM dbo.StrategyModelBinding WHERE StrategyVersionId = @version;",
            [new SqlParameter("@version", SqlDbType.UniqueIdentifier) { Value = strategyVersionId }],
            reader => StoredProfitModelSet.Read(reader.GetString(0), reader.GetString(1)));
        return rows.Count == 0 ? null : rows[0];
    }
}
