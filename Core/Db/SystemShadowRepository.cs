#nullable enable

using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class SystemShadowRepository : SQLBase
{
    public const string MigrationFileName = "20260904_021_HardenSystemShadowExecutionCausality.sql";

    public async Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT CAST(CASE WHEN
    OBJECT_ID(N'dbo.ShadowPortfolioGeneration', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowPortfolio', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowPortfolioSession', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowPortfolioCandidate', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowPosition', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowOrder', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowPortfolioEvent', N'U') IS NOT NULL AND
    OBJECT_ID(N'dbo.ShadowCapitalEvent', N'U') IS NOT NULL AND
    COL_LENGTH(N'dbo.ShadowPosition', N'LastFifteenMinuteBarUtc') IS NOT NULL
THEN 1 ELSE 0 END AS bit);
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<SystemShadowGenerationInfo?> GetLatestGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT TOP (1) [GenerationId],[PolicyVersion],[Status],[TotalAccountValue],
       [AvailableAccountCash],[RealSnapshotUtc],[ActivatedUtc],[UpdatedUtc]
FROM [dbo].[ShadowPortfolioGeneration]
ORDER BY [CreatedUtc] DESC,[GenerationId];
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadGeneration(reader) : null;
    }

    public async Task<SystemShadowAccountSnapshot?> GetLatestAccountSnapshotAsync(
        Guid generationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT TOP (1) [OccurredUtc],[TotalAccountValue],[AvailableAccountCash]
FROM [dbo].[ShadowCapitalEvent]
WHERE [GenerationId] = @GenerationId
  AND [EventType] IN (N'InitialSnapshot',N'AccountSnapshot')
ORDER BY [OccurredUtc] DESC,[CreatedUtc] DESC;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@GenerationId", SqlDbType.UniqueIdentifier, generationId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(Utc(reader.GetDateTime(0)), reader.GetDecimal(1), reader.GetDecimal(2))
            : null;
    }

    public async Task RecordAccountSnapshotAsync(
        Guid generationId,
        decimal totalAccountValue,
        decimal availableAccountCash,
        DateTime occurredUtc,
        CancellationToken cancellationToken = default)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("Generation ID is required.", nameof(generationId));
        if (totalAccountValue <= 0m || availableAccountCash < 0m || availableAccountCash > totalAccountValue)
            throw new ArgumentOutOfRangeException(nameof(totalAccountValue), "Account total and available cash are inconsistent.");
        if (occurredUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot time must be UTC.", nameof(occurredUtc));
        const string sql = """
INSERT INTO [dbo].[ShadowCapitalEvent]
([CapitalEventId],[GenerationId],[OccurredUtc],[EventType],[TotalAccountValue],
 [AvailableAccountCash],[ExternalFlowAmount],[Notes])
VALUES
(@CapitalEventId,@GenerationId,@OccurredUtc,N'AccountSnapshot',@TotalAccountValue,
 @AvailableAccountCash,NULL,N'Manual Wealthsimple TFSA comparison snapshot');
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@CapitalEventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@GenerationId", SqlDbType.UniqueIdentifier, generationId),
            P("@OccurredUtc", SqlDbType.DateTime2, occurredUtc),
            P("@TotalAccountValue", SqlDbType.Decimal, totalAccountValue, precision: 19, scale: 6),
            P("@AvailableAccountCash", SqlDbType.Decimal, availableAccountCash, precision: 19, scale: 6)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SystemShadowGenerationInfo> CreateAndActivateGenerationAsync(
        decimal totalAccountValue,
        decimal availableAccountCash,
        DateTime realSnapshotUtc,
        CancellationToken cancellationToken = default)
    {
        if (totalAccountValue <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalAccountValue));
        if (availableAccountCash < 0m || availableAccountCash > totalAccountValue)
            throw new ArgumentOutOfRangeException(nameof(availableAccountCash));
        if (realSnapshotUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The real-account snapshot timestamp must be UTC.", nameof(realSnapshotUtc));

        Guid generationId = Guid.NewGuid();
        DateTime nowUtc = DateTime.UtcNow;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string existingSql = """
SELECT COUNT(*)
FROM [dbo].[ShadowPortfolioGeneration] WITH (UPDLOCK, HOLDLOCK)
WHERE [Status] <> N'Stopped';
""";
            await using (var existing = new SqlCommand(existingSql, connection, transaction))
            {
                int count = Convert.ToInt32(await existing.ExecuteScalarAsync(cancellationToken));
                if (count != 0)
                    throw new InvalidOperationException("An unfinished Shadow generation already exists. Resume or stop it instead of replacing its history.");
            }

            const string generationSql = """
INSERT INTO [dbo].[ShadowPortfolioGeneration]
([GenerationId],[PolicyVersion],[Status],[TotalAccountValue],[AvailableAccountCash],
 [RealSnapshotUtc],[ActivatedUtc],[CreatedUtc],[UpdatedUtc])
VALUES
(@GenerationId,@PolicyVersion,N'Active',@TotalAccountValue,@AvailableAccountCash,
 @RealSnapshotUtc,@NowUtc,@NowUtc,@NowUtc);

INSERT INTO [dbo].[ShadowCapitalEvent]
([CapitalEventId],[GenerationId],[OccurredUtc],[EventType],[TotalAccountValue],
 [AvailableAccountCash],[ExternalFlowAmount],[Notes])
VALUES
(@CapitalEventId,@GenerationId,@RealSnapshotUtc,N'InitialSnapshot',@TotalAccountValue,
 @AvailableAccountCash,NULL,N'Manual Wealthsimple TFSA snapshot used to start Shadow V1');
""";
            await ExecuteAsync(
                connection,
                transaction,
                generationSql,
                cancellationToken,
                P("@GenerationId", SqlDbType.UniqueIdentifier, generationId),
                P("@PolicyVersion", SqlDbType.NVarChar, SystemShadowVersions.Policy, 32),
                P("@TotalAccountValue", SqlDbType.Decimal, totalAccountValue, precision: 19, scale: 6),
                P("@AvailableAccountCash", SqlDbType.Decimal, availableAccountCash, precision: 19, scale: 6),
                P("@RealSnapshotUtc", SqlDbType.DateTime2, realSnapshotUtc),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                P("@CapitalEventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()));

            foreach (SystemShadowPortfolioDefinition definition in SystemShadowPortfolioDefinition.Version1)
            {
                Guid portfolioId = Guid.NewGuid();
                const string portfolioSql = """
INSERT INTO [dbo].[ShadowPortfolio]
([PortfolioId],[GenerationId],[PortfolioCode],[DisplayName],[Lens],[MaximumPositions],
 [SelectionActor],[ExecutionMode],[Status],[CashBalance],[HighestClosingValue],[CreatedUtc],[UpdatedUtc])
VALUES
(@PortfolioId,@GenerationId,@PortfolioCode,@DisplayName,@Lens,@MaximumPositions,
 N'System',N'Ghost',N'Active',@StartingCash,@StartingCash,@NowUtc,@NowUtc);

INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES
(@EventId,@PortfolioId,@NowUtc,N'Lifecycle',N'Activated',@DetailsJson);
""";
                string details = JsonSerializer.Serialize(new
                {
                    generationId,
                    definition.Code,
                    policyVersion = SystemShadowVersions.Policy,
                    startingCash = totalAccountValue,
                    realAvailableCash = availableAccountCash
                });
                await ExecuteAsync(
                    connection,
                    transaction,
                    portfolioSql,
                    cancellationToken,
                    P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
                    P("@GenerationId", SqlDbType.UniqueIdentifier, generationId),
                    P("@PortfolioCode", SqlDbType.NVarChar, definition.Code, 32),
                    P("@DisplayName", SqlDbType.NVarChar, definition.DefaultDisplayName, 128),
                    P("@Lens", SqlDbType.NVarChar, definition.Lens, 16),
                    P("@MaximumPositions", SqlDbType.TinyInt, definition.MaximumPositions),
                    P("@StartingCash", SqlDbType.Decimal, totalAccountValue, precision: 19, scale: 6),
                    P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                    P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                    P("@DetailsJson", SqlDbType.NVarChar, details, -1));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new SystemShadowGenerationInfo(
            generationId,
            SystemShadowVersions.Policy,
            SystemShadowGenerationStatus.Active,
            totalAccountValue,
            availableAccountCash,
            realSnapshotUtc,
            nowUtc,
            nowUtc);
    }

    public async Task SetGenerationStatusAsync(
        Guid generationId,
        SystemShadowGenerationStatus status,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("Generation ID is required.", nameof(generationId));
        if (status is not (SystemShadowGenerationStatus.Active or SystemShadowGenerationStatus.Paused))
            throw new ArgumentOutOfRangeException(nameof(status), "Only explicit pause and resume are supported here.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A lifecycle reason is required.", nameof(reason));

        string value = status.ToString();
        DateTime nowUtc = DateTime.UtcNow;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string updateSql = """
UPDATE [dbo].[ShadowPortfolioGeneration]
SET [Status] = @Status,[UpdatedUtc] = @NowUtc
WHERE [GenerationId] = @GenerationId
  AND [Status] IN (N'Active',N'Paused');

UPDATE [dbo].[ShadowPortfolio]
SET [Status] = @Status,[PauseReason] = CASE WHEN @Status = N'Paused' THEN @Reason ELSE NULL END,
    [UpdatedUtc] = @NowUtc
WHERE [GenerationId] = @GenerationId
  AND [Status] IN (N'Active',N'Paused');

IF @Status = N'Paused'
BEGIN
    UPDATE o
    SET o.[Status] = N'Cancelled',o.[ReasonCode] = N'OperatorPaused',o.[UpdatedUtc] = @NowUtc
    FROM [dbo].[ShadowOrder] o
    JOIN [dbo].[ShadowPortfolio] p ON p.[PortfolioId] = o.[PortfolioId]
    WHERE p.[GenerationId] = @GenerationId
      AND o.[Status] = N'Pending' AND o.[Side] = N'Buy';
END;
""";
            await ExecuteAsync(
                connection,
                transaction,
                updateSql,
                cancellationToken,
                P("@GenerationId", SqlDbType.UniqueIdentifier, generationId),
                P("@Status", SqlDbType.NVarChar, value, 32),
                P("@Reason", SqlDbType.NVarChar, reason.Trim(), 128),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc));

            const string portfoliosSql = """
SELECT [PortfolioId]
FROM [dbo].[ShadowPortfolio]
WHERE [GenerationId] = @GenerationId;
""";
            var portfolioIds = new List<Guid>();
            await using (var command = new SqlCommand(portfoliosSql, connection, transaction))
            {
                command.Parameters.Add(P("@GenerationId", SqlDbType.UniqueIdentifier, generationId));
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    portfolioIds.Add(reader.GetGuid(0));
            }
            foreach (Guid portfolioId in portfolioIds)
            {
                const string eventSql = """
INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES (@EventId,@PortfolioId,@NowUtc,N'Lifecycle',@ReasonCode,@DetailsJson);
""";
                await ExecuteAsync(
                    connection,
                    transaction,
                    eventSql,
                    cancellationToken,
                    P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                    P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
                    P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                    P("@ReasonCode", SqlDbType.NVarChar, status == SystemShadowGenerationStatus.Paused ? "OperatorPaused" : "OperatorResumed", 64),
                    P("@DetailsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new { reason }), -1));
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ResumePortfolioAfterCapitalReviewAsync(
        Guid portfolioId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A review decision is required.", nameof(reason));
        const string sql = """
DECLARE @ReviewedValue decimal(19,6) =
(
    SELECT p.[CashBalance] + COALESCE(SUM(CASE WHEN x.[Status] = N'Open' THEN x.[Shares] * x.[LastPrice] ELSE 0 END),0)
    FROM [dbo].[ShadowPortfolio] p
    LEFT JOIN [dbo].[ShadowPosition] x ON x.[PortfolioId] = p.[PortfolioId]
    WHERE p.[PortfolioId] = @PortfolioId
    GROUP BY p.[CashBalance]
);

IF @ReviewedValue IS NULL OR @ReviewedValue <= 0
    THROW 51144, 'The reviewed Shadow portfolio has no positive value.', 1;

UPDATE [dbo].[ShadowPortfolio]
SET [Status] = N'Active',[PauseReason] = NULL,[HighestClosingValue] = @ReviewedValue,
    [UpdatedUtc] = SYSUTCDATETIME()
WHERE [PortfolioId] = @PortfolioId AND [Status] = N'CapitalReviewRequired';

IF @@ROWCOUNT <> 1 THROW 51142, 'The selected portfolio is not waiting for a capital review.', 1;

INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES
(@EventId,@PortfolioId,SYSUTCDATETIME(),N'Risk',N'CapitalReviewResumed',@DetailsJson);
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
            P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@DetailsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new
            {
                reason,
                drawdownBaselineRearmedAtReviewedValue = true
            }), -1)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateDisplayNameAsync(
        Guid portfolioId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        string normalized = displayName?.Trim() ?? string.Empty;
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));
        if (normalized.Length is < 1 or > 128)
            throw new ArgumentException("Display name must be 1 to 128 characters.", nameof(displayName));
        const string sql = """
UPDATE [dbo].[ShadowPortfolio]
SET [DisplayName] = @DisplayName,[UpdatedUtc] = SYSUTCDATETIME()
WHERE [PortfolioId] = @PortfolioId;
IF @@ROWCOUNT <> 1 THROW 51143, 'Shadow portfolio was not found.', 1;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
            P("@DisplayName", SqlDbType.NVarChar, normalized, 128)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SystemShadowPortfolioOverview>> GetPortfolioOverviewsAsync(
        Guid generationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT p.[PortfolioId],p.[GenerationId],p.[PortfolioCode],p.[DisplayName],p.[Lens],
       p.[MaximumPositions],p.[Status],p.[CashBalance],
       p.[CashBalance] + COALESCE(a.[OpenMarketValue],0) AS [NetAssetValue],
       COALESCE(a.[OpenPositions],0),COALESCE(a.[RealizedProfitLoss],0),
       COALESCE(a.[UnrealizedProfitLoss],0),
       (p.[CashBalance] + COALESCE(a.[OpenMarketValue],0)) / NULLIF(g.[TotalAccountValue],0) - 1 AS [TotalReturn],
       CASE WHEN s.[OpeningValue] > 0
            THEN (p.[CashBalance] + COALESCE(a.[OpenMarketValue],0)) / s.[OpeningValue] - 1
            ELSE NULL END AS [DailyReturn],
       (p.[CashBalance] + COALESCE(a.[OpenMarketValue],0)) / NULLIF(p.[HighestClosingValue],0) - 1 AS [Drawdown],
       a.[FreshestPriceEventUtc],s.[LatestCandidateEvaluationUtc],p.[UpdatedUtc],s.[Status]
FROM [dbo].[ShadowPortfolio] p
JOIN [dbo].[ShadowPortfolioGeneration] g ON g.[GenerationId] = p.[GenerationId]
OUTER APPLY
(
    SELECT
      SUM(CASE WHEN x.[Status] = N'Open' THEN 1 ELSE 0 END) AS [OpenPositions],
      SUM(CASE WHEN x.[Status] = N'Open' THEN x.[Shares] * x.[LastPrice] ELSE 0 END) AS [OpenMarketValue],
      SUM(CASE WHEN x.[Status] = N'Closed' THEN x.[RealizedProfitLoss] ELSE 0 END) AS [RealizedProfitLoss],
      SUM(CASE WHEN x.[Status] = N'Open' THEN x.[Shares] * x.[LastPrice] - x.[CostBasis] ELSE 0 END) AS [UnrealizedProfitLoss],
      MAX(CASE WHEN x.[Status] = N'Open' THEN x.[LastPriceEventUtc] ELSE NULL END) AS [FreshestPriceEventUtc]
    FROM [dbo].[ShadowPosition] x
    WHERE x.[PortfolioId] = p.[PortfolioId]
) a
OUTER APPLY
(
    SELECT TOP (1) z.[OpeningValue],z.[Status],
      (SELECT MAX(candidate.[LastEvaluatedUtc])
       FROM [dbo].[ShadowPortfolioCandidate] candidate
       WHERE candidate.[SessionId] = z.[SessionId]) AS [LatestCandidateEvaluationUtc]
    FROM [dbo].[ShadowPortfolioSession] z
    WHERE z.[PortfolioId] = p.[PortfolioId]
    ORDER BY z.[TradingDate] DESC,z.[CreatedUtc] DESC
) s
WHERE p.[GenerationId] = @GenerationId
ORDER BY p.[Lens],p.[MaximumPositions];
""";
        var rows = new List<SystemShadowPortfolioOverview>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@GenerationId", SqlDbType.UniqueIdentifier, generationId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SystemShadowPortfolioOverview(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetByte(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetInt32(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetDecimal(12), reader.IsDBNull(13) ? null : reader.GetDecimal(13), reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : DateTime.SpecifyKind(reader.GetDateTime(15), DateTimeKind.Utc),
                reader.IsDBNull(16) ? null : DateTime.SpecifyKind(reader.GetDateTime(16), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(17), DateTimeKind.Utc),
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<SystemShadowPositionInfo>> GetPositionsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT [PositionId],[PortfolioId],[Symbol],[Status],[Shares],[AverageCost],[CostBasis],
       [FullPositionTarget],[EntryUtc],[EntryTradingDate],[AddOnCount],[SameDayReentryCount],
       [HighestFifteenClose],[LastFifteenMinuteBarUtc],[TrailingStopPrice],[ProfitProtectionArmed],[LastPrice],
       [LastPriceEventUtc],[RealizedProfitLoss],[ExitUtc],[ExitReasonCode]
FROM [dbo].[ShadowPosition]
WHERE [PortfolioId] = @PortfolioId
ORDER BY CASE WHEN [Status] = N'Open' THEN 0 ELSE 1 END,[EntryUtc] DESC;
""";
        var rows = new List<SystemShadowPositionInfo>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SystemShadowPositionInfo(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                Utc(reader.GetDateTime(8)), reader.GetDateTime(9), reader.GetByte(10), reader.GetByte(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : Utc(reader.GetDateTime(13)),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14), reader.GetBoolean(15),
                reader.GetDecimal(16), Utc(reader.GetDateTime(17)), reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : Utc(reader.GetDateTime(19)),
                reader.IsDBNull(20) ? null : reader.GetString(20)));
        }
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<SystemShadowEventInfo>> GetRecentEventsAsync(
        Guid portfolioId,
        int maximumRows = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        const string sql = """
SELECT TOP (@MaximumRows) [EventId],[PortfolioId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson]
FROM [dbo].[ShadowPortfolioEvent]
WHERE [PortfolioId] = @PortfolioId
ORDER BY [OccurredUtc] DESC,[CreatedUtc] DESC;
""";
        var rows = new List<SystemShadowEventInfo>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@MaximumRows", SqlDbType.Int, maximumRows),
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId)
        ]);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), Utc(reader.GetDateTime(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<SystemShadowCandidateMonitorInfo>> GetCandidateMonitorAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH [LatestSession] AS
(
    SELECT TOP (1) [SessionId],[TradingDate]
    FROM [dbo].[ShadowPortfolioSession]
    WHERE [PortfolioId] = @PortfolioId
    ORDER BY [TradingDate] DESC,[CreatedUtc] DESC
)
SELECT candidate.[CandidateTrackingId],candidate.[Rank],candidate.[Symbol],candidate.[State],candidate.[ReasonCode],
       CONVERT(decimal(19,6),evidence.[ObservationClose]),priorBar.[Close],latestBar.[Close],latestBar.[EventUtc],
       candidate.[LastEvaluatedUtc]
FROM [dbo].[ShadowPortfolioCandidate] candidate
JOIN [LatestSession] session ON session.[SessionId] = candidate.[SessionId]
JOIN [dbo].[CalibrationCandidate] evidence ON evidence.[CandidateId] = candidate.[CalibrationCandidateId]
OUTER APPLY
(
    SELECT TOP (1) bar.[EventUtc],CONVERT(decimal(19,6),bar.[Close]) AS [Close]
    FROM [dbo].[IntradayEvidenceBar] bar
    WHERE bar.[Symbol] = candidate.[Symbol]
      AND bar.[IntervalMinutes] = 5
      AND candidate.[LastEvaluatedUtc] IS NOT NULL
      AND DATEADD(MINUTE,5,bar.[EventUtc]) <= candidate.[LastEvaluatedUtc]
      AND CONVERT(date,bar.[EventUtc] AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time') = session.[TradingDate]
    ORDER BY bar.[EventUtc] DESC
) latestBar
OUTER APPLY
(
    SELECT TOP (1) CONVERT(decimal(19,6),bar.[Close]) AS [Close]
    FROM [dbo].[IntradayEvidenceBar] bar
    WHERE bar.[Symbol] = candidate.[Symbol]
      AND bar.[IntervalMinutes] = 5
      AND latestBar.[EventUtc] IS NOT NULL
      AND bar.[EventUtc] < latestBar.[EventUtc]
      AND CONVERT(date,bar.[EventUtc] AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time') = session.[TradingDate]
    ORDER BY bar.[EventUtc] DESC
) priorBar
ORDER BY candidate.[Rank],candidate.[Symbol];
""";
        var rows = new List<SystemShadowCandidateMonitorInfo>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetByte(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : Utc(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : Utc(reader.GetDateTime(9))));
        }
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<SystemShadowRuntimePortfolio>> GetRunnablePortfoliosAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT p.[PortfolioId],p.[GenerationId],p.[PortfolioCode],p.[Lens],p.[MaximumPositions],
       p.[Status],p.[CashBalance],p.[HighestClosingValue],g.[ActivatedUtc]
FROM [dbo].[ShadowPortfolio] p
JOIN [dbo].[ShadowPortfolioGeneration] g ON g.[GenerationId] = p.[GenerationId]
WHERE g.[Status] IN (N'Active',N'Paused',N'CapitalReviewRequired')
  AND p.[Status] IN (N'Active',N'Paused',N'CapitalReviewRequired')
ORDER BY p.[Lens],p.[MaximumPositions];
""";
        var rows = new List<SystemShadowRuntimePortfolio>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetByte(4), reader.GetString(5), reader.GetDecimal(6), reader.GetDecimal(7),
                Utc(reader.GetDateTime(8))));
        }
        return rows.AsReadOnly();
    }

    public async Task<SystemShadowRuntimeSession> EnsureSessionAsync(
        SystemShadowRuntimePortfolio portfolio,
        DateTime tradingDate,
        SystemShadowDelphiRun? run,
        IReadOnlyList<SystemShadowDelphiCandidate> candidates,
        DateTime? activationBaselineUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(candidates);
        if (activationBaselineUtc.HasValue && activationBaselineUtc.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Activation baseline must be UTC.", nameof(activationBaselineUtc));
        if (run is null && candidates.Count != 0)
            throw new ArgumentException("Candidates require a frozen Delphi run.", nameof(candidates));
        if (candidates.Count > portfolio.MaximumPositions ||
            candidates.Count > 0 && candidates[^1].Rank > portfolio.MaximumPositions)
            throw new ArgumentException("Frozen candidates exceed this portfolio's rank boundary.", nameof(candidates));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            SystemShadowRuntimeSession? existing = await ReadSessionAsync(
                connection,
                transaction,
                portfolio.PortfolioId,
                tradingDate.Date,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            Guid sessionId = Guid.NewGuid();
            DateTime nowUtc = DateTime.UtcNow;
            const string sessionSql = """
DECLARE @OpeningValue decimal(19,6) =
(
    SELECT p.[CashBalance] + COALESCE(SUM(CASE WHEN x.[Status] = N'Open' THEN x.[Shares] * x.[LastPrice] ELSE 0 END),0)
    FROM [dbo].[ShadowPortfolio] p
    LEFT JOIN [dbo].[ShadowPosition] x ON x.[PortfolioId] = p.[PortfolioId]
    WHERE p.[PortfolioId] = @PortfolioId
    GROUP BY p.[CashBalance]
);

INSERT INTO [dbo].[ShadowPortfolioSession]
([SessionId],[PortfolioId],[TradingDate],[CalibrationRunId],[Status],[ActivationBaselineUtc],
 [OpeningValue],[StartedUtc],[CreatedUtc],[UpdatedUtc])
VALUES
(@SessionId,@PortfolioId,@TradingDate,@CalibrationRunId,@Status,@ActivationBaselineUtc,
 @OpeningValue,@NowUtc,@NowUtc,@NowUtc);
""";
            await ExecuteAsync(
                connection,
                transaction,
                sessionSql,
                cancellationToken,
                P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
                P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolio.PortfolioId),
                P("@TradingDate", SqlDbType.Date, tradingDate.Date),
                P("@CalibrationRunId", SqlDbType.UniqueIdentifier, run?.RunId),
                P("@Status", SqlDbType.NVarChar, run is null ? "NoValidDelphiRun" : "Active", 32),
                P("@ActivationBaselineUtc", SqlDbType.DateTime2, activationBaselineUtc),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc));

            foreach (SystemShadowDelphiCandidate candidate in candidates)
            {
                const string candidateSql = """
INSERT INTO [dbo].[ShadowPortfolioCandidate]
([CandidateTrackingId],[SessionId],[CalibrationCandidateId],[Symbol],[Rank],[State],[CreatedUtc],[UpdatedUtc])
VALUES
(@CandidateTrackingId,@SessionId,@CalibrationCandidateId,@Symbol,@Rank,N'Pending',@NowUtc,@NowUtc);
""";
                await ExecuteAsync(
                    connection,
                    transaction,
                    candidateSql,
                    cancellationToken,
                    P("@CandidateTrackingId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                    P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
                    P("@CalibrationCandidateId", SqlDbType.UniqueIdentifier, candidate.CandidateId),
                    P("@Symbol", SqlDbType.NVarChar, candidate.Symbol, 20),
                    P("@Rank", SqlDbType.TinyInt, candidate.Rank),
                    P("@NowUtc", SqlDbType.DateTime2, nowUtc));
            }

            const string eventSql = """
INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[SessionId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES
(@EventId,@PortfolioId,@SessionId,@NowUtc,N'Lifecycle',@ReasonCode,@DetailsJson);
""";
            await ExecuteAsync(
                connection,
                transaction,
                eventSql,
                cancellationToken,
                P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolio.PortfolioId),
                P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                P("@ReasonCode", SqlDbType.NVarChar, run is null ? "NoValidDelphiRun" : "CandidatesFrozen", 64),
                P("@DetailsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new
                {
                    tradingDate = tradingDate.Date,
                    calibrationRunId = run?.RunId,
                    candidateCount = candidates.Count,
                    activationBaselineUtc
                }), -1));

            SystemShadowRuntimeSession created = await ReadSessionAsync(
                connection,
                transaction,
                portfolio.PortfolioId,
                tradingDate.Date,
                cancellationToken)
                ?? throw new InvalidOperationException("Shadow session was not created.");
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SystemShadowRuntimeCandidate>> GetSessionCandidatesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT x.[CandidateTrackingId],x.[SessionId],x.[CalibrationCandidateId],x.[Symbol],x.[Rank],
       CONVERT(decimal(19,6),c.[ObservationClose]),x.[State],x.[ReasonCode],x.[LastEvaluatedUtc]
FROM [dbo].[ShadowPortfolioCandidate] x
JOIN [dbo].[CalibrationCandidate] c ON c.[CandidateId] = x.[CalibrationCandidateId]
WHERE x.[SessionId] = @SessionId
ORDER BY x.[Rank],x.[Symbol];
""";
        var rows = new List<SystemShadowRuntimeCandidate>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@SessionId", SqlDbType.UniqueIdentifier, sessionId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetByte(4), reader.GetDecimal(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : Utc(reader.GetDateTime(8))));
        }
        return rows.AsReadOnly();
    }

    public async Task<IReadOnlyList<SystemShadowPendingOrder>> GetPendingOrdersAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT [OrderId],[PortfolioId],[SessionId],[PositionId],[CandidateTrackingId],[Symbol],
       [Side],[OrderKind],[SignalReceivedUtc],[EarliestFillUtc],[Budget],[ReasonCode]
FROM [dbo].[ShadowOrder]
WHERE [PortfolioId] = @PortfolioId AND [Status] = N'Pending'
ORDER BY [EarliestFillUtc],[CreatedUtc];
""";
        var rows = new List<SystemShadowPendingOrder>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), Utc(reader.GetDateTime(8)),
                Utc(reader.GetDateTime(9)), reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.GetString(11)));
        }
        return rows.AsReadOnly();
    }

    public async Task<bool> TryCreateOrderAsync(
        Guid portfolioId,
        Guid? sessionId,
        Guid? positionId,
        Guid? candidateTrackingId,
        string symbol,
        string side,
        string orderKind,
        DateTime signalReceivedUtc,
        decimal? budget,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        if (signalReceivedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Signal receipt must be UTC.", nameof(signalReceivedUtc));
        if (side is not ("Buy" or "Sell"))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("Order symbol and reason are required.");
        DateTime earliestFillUtc = SystemShadowPolicy.EarliestFiveMinuteFillBoundary(signalReceivedUtc);
        Guid orderId = Guid.NewGuid();
        DateTime nowUtc = DateTime.UtcNow;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string sql = """
IF @Side = N'Sell'
BEGIN
    UPDATE [dbo].[ShadowOrder]
    SET [Status] = N'Cancelled',[ReasonCode] = N'SupersededByProtectiveSell',[UpdatedUtc] = @NowUtc
    WHERE [PortfolioId] = @PortfolioId AND [Symbol] = @Symbol
      AND [Status] = N'Pending' AND [Side] = N'Buy';
END;

IF EXISTS
(
    SELECT 1 FROM [dbo].[ShadowOrder] WITH (UPDLOCK,HOLDLOCK)
    WHERE [PortfolioId] = @PortfolioId AND [Symbol] = @Symbol AND [Status] = N'Pending'
)
    SELECT CAST(0 AS bit);
ELSE
BEGIN
    INSERT INTO [dbo].[ShadowOrder]
    ([OrderId],[PortfolioId],[SessionId],[PositionId],[CandidateTrackingId],[Symbol],
     [Side],[OrderKind],[Status],[SignalReceivedUtc],[EarliestFillUtc],[Budget],
     [FrictionRate],[ReasonCode],[CreatedUtc],[UpdatedUtc])
    VALUES
    (@OrderId,@PortfolioId,@SessionId,@PositionId,@CandidateTrackingId,@Symbol,
     @Side,@OrderKind,N'Pending',@SignalReceivedUtc,@EarliestFillUtc,@Budget,
     @FrictionRate,@ReasonCode,@NowUtc,@NowUtc);

    INSERT INTO [dbo].[ShadowPortfolioEvent]
    ([EventId],[PortfolioId],[SessionId],[PositionId],[OrderId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
    VALUES
    (@EventId,@PortfolioId,@SessionId,@PositionId,@OrderId,@SignalReceivedUtc,N'Order',@ReasonCode,@DetailsJson);
    SELECT CAST(1 AS bit);
END;
""";
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddRange([
                P("@OrderId", SqlDbType.UniqueIdentifier, orderId),
                P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
                P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
                P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                P("@CandidateTrackingId", SqlDbType.UniqueIdentifier, candidateTrackingId),
                P("@Symbol", SqlDbType.NVarChar, symbol.Trim().ToUpperInvariant(), 20),
                P("@Side", SqlDbType.NVarChar, side, 8),
                P("@OrderKind", SqlDbType.NVarChar, orderKind, 24),
                P("@SignalReceivedUtc", SqlDbType.DateTime2, signalReceivedUtc),
                P("@EarliestFillUtc", SqlDbType.DateTime2, earliestFillUtc),
                P("@Budget", SqlDbType.Decimal, budget, precision: 19, scale: 6),
                P("@FrictionRate", SqlDbType.Decimal,
                    side == "Buy" ? SystemShadowPolicyConfig.Version1.EntryFrictionRate : SystemShadowPolicyConfig.Version1.ExitFrictionRate,
                    precision: 9, scale: 6),
                P("@ReasonCode", SqlDbType.NVarChar, reasonCode, 64),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                P("@DetailsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new
                {
                    side,
                    orderKind,
                    candidateTrackingId,
                    signalReceivedUtc,
                    earliestFillUtc,
                    budget
                }), -1)
            ]);
            bool created = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> FillPendingOrderAsync(
        SystemShadowPendingOrder order,
        decimal rawFillPrice,
        DateTime fillUtc,
        DateTime tradingDate,
        int sameDayReentryCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (fillUtc.Kind != DateTimeKind.Utc || fillUtc < order.EarliestFillUtc)
            throw new ArgumentException("Fill must be a qualifying later UTC bar.", nameof(fillUtc));
        if (order.Side == "Buy" && fillUtc != order.EarliestFillUtc)
            throw new ArgumentException("A Shadow buy can fill only at its exact immediate fill bar.", nameof(fillUtc));
        decimal adjustedPrice = order.Side == "Buy"
            ? SystemShadowPolicy.AdjustedBuyPrice(rawFillPrice)
            : SystemShadowPolicy.AdjustedSellPrice(rawFillPrice);
        DateTime nowUtc = DateTime.UtcNow;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string lockSql = """
SELECT [Status] FROM [dbo].[ShadowOrder] WITH (UPDLOCK,HOLDLOCK) WHERE [OrderId] = @OrderId;
""";
            await using (var lockCommand = new SqlCommand(lockSql, connection, transaction))
            {
                lockCommand.Parameters.Add(P("@OrderId", SqlDbType.UniqueIdentifier, order.OrderId));
                string? status = Convert.ToString(await lockCommand.ExecuteScalarAsync(cancellationToken));
                if (status != "Pending")
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
            }

            int shares;
            Guid? positionId = order.PositionId;
            decimal fillValue;
            if (order.Side == "Buy")
            {
                const string cashSql = """
SELECT p.[CashBalance],p.[Status],g.[Status],CAST(COALESCE(s.[DailyLossGuardActive],0) AS bit)
FROM [dbo].[ShadowPortfolio] p WITH (UPDLOCK,HOLDLOCK)
JOIN [dbo].[ShadowPortfolioGeneration] g WITH (UPDLOCK,HOLDLOCK)
  ON g.[GenerationId] = p.[GenerationId]
LEFT JOIN [dbo].[ShadowPortfolioSession] s WITH (UPDLOCK,HOLDLOCK)
  ON s.[SessionId] = @SessionId
WHERE p.[PortfolioId] = @PortfolioId;
""";
                decimal cash;
                await using (var cashCommand = new SqlCommand(cashSql, connection, transaction))
                {
                    cashCommand.Parameters.AddRange([
                        P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId),
                        P("@SessionId", SqlDbType.UniqueIdentifier, order.SessionId)
                    ]);
                    await using SqlDataReader reader = await cashCommand.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                        throw new InvalidOperationException("The Shadow portfolio no longer exists.");
                    cash = reader.GetDecimal(0);
                    bool buyingSuspended = reader.GetString(1) != "Active" ||
                                           reader.GetString(2) != "Active" ||
                                           reader.GetBoolean(3);
                    await reader.DisposeAsync();
                    if (buyingSuspended)
                    {
                        await CancelLockedOrderAsync(
                            connection,
                            transaction,
                            order,
                            "NewRiskSuspended",
                            nowUtc,
                            cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return false;
                    }
                }
                decimal budget = System.Math.Min(order.Budget ?? 0m, cash);
                shares = SystemShadowPolicy.WholeSharesForBuy(budget, rawFillPrice);
                if (shares <= 0)
                {
                    await CancelLockedOrderAsync(connection, transaction, order, "InsufficientCashForWholeShare", nowUtc, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
                fillValue = adjustedPrice * shares;

                if (order.OrderKind == "AddOn")
                {
                    if (!positionId.HasValue)
                        throw new InvalidOperationException("An add-on requires an open position.");
                    const string addSql = """
UPDATE [dbo].[ShadowPosition]
SET [AverageCost] = ([CostBasis] + @FillValue) / ([Shares] + @Shares),
    [CostBasis] = [CostBasis] + @FillValue,
    [Shares] = [Shares] + @Shares,
    [AddOnCount] = [AddOnCount] + 1,
    [LastPrice] = @RawFillPrice,[LastPriceEventUtc] = @FillUtc,[UpdatedUtc] = @NowUtc
WHERE [PositionId] = @PositionId AND [Status] = N'Open' AND [AddOnCount] = 0;

IF @@ROWCOUNT <> 1 THROW 51140, 'The Shadow add-on position is no longer eligible.', 1;
""";
                    await ExecuteAsync(connection, transaction, addSql, cancellationToken,
                        P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                        P("@FillValue", SqlDbType.Decimal, fillValue, precision: 19, scale: 6),
                        P("@Shares", SqlDbType.Int, shares),
                        P("@RawFillPrice", SqlDbType.Decimal, rawFillPrice, precision: 19, scale: 6),
                        P("@FillUtc", SqlDbType.DateTime2, fillUtc),
                        P("@NowUtc", SqlDbType.DateTime2, nowUtc));
                }
                else
                {
                    positionId = Guid.NewGuid();
                    decimal target = order.Budget.HasValue
                        ? decimal.Round(order.Budget.Value / SystemShadowPolicyConfig.Version1.InitialAllocationFraction, 6)
                        : fillValue;
                    const string openSql = """
INSERT INTO [dbo].[ShadowPosition]
([PositionId],[PortfolioId],[Symbol],[Status],[Shares],[AverageCost],[CostBasis],
 [FullPositionTarget],[EntryUtc],[EntryTradingDate],[SameDayReentryCount],
 [HighestFifteenClose],[ProfitProtectionArmed],[LastPrice],[LastPriceEventUtc],[CreatedUtc],[UpdatedUtc])
VALUES
(@PositionId,@PortfolioId,@Symbol,N'Open',@Shares,@AdjustedPrice,@FillValue,
 @Target,@FillUtc,@TradingDate,@ReentryCount,@RawFillPrice,0,@RawFillPrice,@FillUtc,@NowUtc,@NowUtc);
""";
                    await ExecuteAsync(connection, transaction, openSql, cancellationToken,
                        P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                        P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId),
                        P("@Symbol", SqlDbType.NVarChar, order.Symbol, 20),
                        P("@Shares", SqlDbType.Int, shares),
                        P("@AdjustedPrice", SqlDbType.Decimal, adjustedPrice, precision: 19, scale: 6),
                        P("@FillValue", SqlDbType.Decimal, fillValue, precision: 19, scale: 6),
                        P("@Target", SqlDbType.Decimal, target, precision: 19, scale: 6),
                        P("@FillUtc", SqlDbType.DateTime2, fillUtc),
                        P("@TradingDate", SqlDbType.Date, tradingDate.Date),
                        P("@ReentryCount", SqlDbType.TinyInt, sameDayReentryCount),
                        P("@RawFillPrice", SqlDbType.Decimal, rawFillPrice, precision: 19, scale: 6),
                        P("@NowUtc", SqlDbType.DateTime2, nowUtc));
                }

                const string debitSql = """
UPDATE [dbo].[ShadowPortfolio]
SET [CashBalance] = [CashBalance] - @FillValue,[UpdatedUtc] = @NowUtc
WHERE [PortfolioId] = @PortfolioId AND [CashBalance] >= @FillValue;
IF @@ROWCOUNT <> 1 THROW 51141, 'Shadow cash changed before the buy could fill.', 1;
""";
                await ExecuteAsync(connection, transaction, debitSql, cancellationToken,
                    P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId),
                    P("@FillValue", SqlDbType.Decimal, fillValue, precision: 19, scale: 6),
                    P("@NowUtc", SqlDbType.DateTime2, nowUtc));
            }
            else
            {
                if (!positionId.HasValue)
                    throw new InvalidOperationException("A sell requires an open position.");
                const string positionSql = """
SELECT [Shares],[CostBasis]
FROM [dbo].[ShadowPosition] WITH (UPDLOCK,HOLDLOCK)
WHERE [PositionId] = @PositionId AND [Status] = N'Open';
""";
                decimal costBasis;
                await using (var positionCommand = new SqlCommand(positionSql, connection, transaction))
                {
                    positionCommand.Parameters.Add(P("@PositionId", SqlDbType.UniqueIdentifier, positionId));
                    await using SqlDataReader reader = await positionCommand.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        await reader.DisposeAsync();
                        await CancelLockedOrderAsync(connection, transaction, order, "PositionAlreadyClosed", nowUtc, cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return false;
                    }
                    shares = reader.GetInt32(0);
                    costBasis = reader.GetDecimal(1);
                }
                fillValue = adjustedPrice * shares;
                decimal realized = fillValue - costBasis;
                const string closeSql = """
UPDATE [dbo].[ShadowPosition]
SET [Status] = N'Closed',[LastPrice] = @RawFillPrice,[LastPriceEventUtc] = @FillUtc,
    [RealizedProfitLoss] = @Realized,[ExitUtc] = @FillUtc,[ExitReasonCode] = @ReasonCode,[UpdatedUtc] = @NowUtc
WHERE [PositionId] = @PositionId AND [Status] = N'Open';

UPDATE [dbo].[ShadowPortfolio]
SET [CashBalance] = [CashBalance] + @FillValue,[UpdatedUtc] = @NowUtc
WHERE [PortfolioId] = @PortfolioId;
""";
                await ExecuteAsync(connection, transaction, closeSql, cancellationToken,
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@RawFillPrice", SqlDbType.Decimal, rawFillPrice, precision: 19, scale: 6),
                    P("@FillUtc", SqlDbType.DateTime2, fillUtc),
                    P("@Realized", SqlDbType.Decimal, realized, precision: 19, scale: 6),
                    P("@ReasonCode", SqlDbType.NVarChar, order.ReasonCode, 64),
                    P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                    P("@FillValue", SqlDbType.Decimal, fillValue, precision: 19, scale: 6),
                    P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId));
            }

            const string fillSql = """
UPDATE [dbo].[ShadowOrder]
SET [Status] = N'Filled',[PositionId] = @PositionId,[Shares] = @Shares,
    [RawFillPrice] = @RawFillPrice,[AdjustedFillPrice] = @AdjustedFillPrice,
    [FillUtc] = @FillUtc,[UpdatedUtc] = @NowUtc
WHERE [OrderId] = @OrderId AND [Status] = N'Pending';

UPDATE [dbo].[ShadowPortfolioCandidate]
SET [State] = CASE WHEN @Side = N'Buy' THEN N'Entered' ELSE N'Exited' END,
    [UpdatedUtc] = @NowUtc
WHERE (@Side = N'Buy' AND [CandidateTrackingId] = @CandidateTrackingId)
   OR (@Side = N'Sell' AND [SessionId] = @SessionId AND [Symbol] = @Symbol);

INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[SessionId],[PositionId],[OrderId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES
(@EventId,@PortfolioId,@SessionId,@PositionId,@OrderId,@FillUtc,N'Order',N'Filled',@DetailsJson);
""";
            await ExecuteAsync(connection, transaction, fillSql, cancellationToken,
                P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                P("@Shares", SqlDbType.Int, shares),
                P("@RawFillPrice", SqlDbType.Decimal, rawFillPrice, precision: 19, scale: 6),
                P("@AdjustedFillPrice", SqlDbType.Decimal, adjustedPrice, precision: 19, scale: 6),
                P("@FillUtc", SqlDbType.DateTime2, fillUtc),
                P("@NowUtc", SqlDbType.DateTime2, nowUtc),
                P("@OrderId", SqlDbType.UniqueIdentifier, order.OrderId),
                P("@Side", SqlDbType.NVarChar, order.Side, 8),
                P("@Symbol", SqlDbType.NVarChar, order.Symbol, 20),
                P("@CandidateTrackingId", SqlDbType.UniqueIdentifier, order.CandidateTrackingId),
                P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId),
                P("@SessionId", SqlDbType.UniqueIdentifier, order.SessionId),
                P("@DetailsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new
                {
                    order.Side,
                    order.OrderKind,
                    shares,
                    rawFillPrice,
                    adjustedPrice,
                    fillUtc
                }), -1));

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> CancelPendingOrderAsync(
        SystemShadowPendingOrder order,
        string reason,
        DateTime occurredUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));
        if (occurredUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Cancellation time must be UTC.", nameof(occurredUtc));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string lockSql = """
SELECT [Status] FROM [dbo].[ShadowOrder] WITH (UPDLOCK,HOLDLOCK) WHERE [OrderId] = @OrderId;
""";
            await using (var lockCommand = new SqlCommand(lockSql, connection, transaction))
            {
                lockCommand.Parameters.Add(P("@OrderId", SqlDbType.UniqueIdentifier, order.OrderId));
                string? status = Convert.ToString(await lockCommand.ExecuteScalarAsync(cancellationToken));
                if (status != "Pending")
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
            }

            await CancelLockedOrderAsync(
                connection,
                transaction,
                order,
                reason.Trim(),
                occurredUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateCandidateDecisionAsync(
        Guid candidateTrackingId,
        string state,
        string reasonCode,
        DateTime evaluatedUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE [dbo].[ShadowPortfolioCandidate]
SET [State] = @State,[ReasonCode] = @ReasonCode,[LastEvaluatedUtc] = @EvaluatedUtc,[UpdatedUtc] = SYSUTCDATETIME()
WHERE [CandidateTrackingId] = @CandidateTrackingId;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@CandidateTrackingId", SqlDbType.UniqueIdentifier, candidateTrackingId),
            P("@State", SqlDbType.NVarChar, state, 24),
            P("@ReasonCode", SqlDbType.NVarChar, reasonCode, 64),
            P("@EvaluatedUtc", SqlDbType.DateTime2, evaluatedUtc)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePositionEvidenceAsync(
        Guid positionId,
        decimal lastPrice,
        DateTime lastPriceEventUtc,
        SystemShadowTrailingState trailingState,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE [dbo].[ShadowPosition]
SET [LastPrice] = @LastPrice,[LastPriceEventUtc] = @LastPriceEventUtc,
    [HighestFifteenClose] = @HighestFifteenClose,[LastFifteenMinuteBarUtc] = @LastFifteenMinuteBarUtc,
    [TrailingStopPrice] = @TrailingStopPrice,
    [ProfitProtectionArmed] = @ProfitProtectionArmed,[UpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId AND [Status] = N'Open'
  AND [LastPriceEventUtc] <= @LastPriceEventUtc;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
            P("@LastPrice", SqlDbType.Decimal, lastPrice, precision: 19, scale: 6),
            P("@LastPriceEventUtc", SqlDbType.DateTime2, lastPriceEventUtc),
            P("@HighestFifteenClose", SqlDbType.Decimal, trailingState.HighestCompletedFifteenMinuteClose, precision: 19, scale: 6),
            P("@LastFifteenMinuteBarUtc", SqlDbType.DateTime2, trailingState.LastProcessedFifteenMinuteBarUtc),
            P("@TrailingStopPrice", SqlDbType.Decimal, trailingState.TrailingStopPrice, precision: 19, scale: 6),
            P("@ProfitProtectionArmed", SqlDbType.Bit, trailingState.ProfitProtectionArmed)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetRiskGuardAsync(
        Guid portfolioId,
        Guid sessionId,
        bool dailyLossGuard,
        bool capitalReviewRequired,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
DECLARE @DailyActivated bit = 0;
DECLARE @ReviewActivated bit = 0;

IF @DailyLossGuard = 1
BEGIN
    UPDATE [dbo].[ShadowPortfolioSession]
    SET [DailyLossGuardActive] = 1,[UpdatedUtc] = SYSUTCDATETIME()
    WHERE [SessionId] = @SessionId AND [DailyLossGuardActive] = 0;
    IF @@ROWCOUNT = 1 SET @DailyActivated = 1;
END;

IF @CapitalReviewRequired = 1
BEGIN
    UPDATE [dbo].[ShadowPortfolio]
    SET [Status] = N'CapitalReviewRequired',[PauseReason] = N'TotalDrawdown10Percent',[UpdatedUtc] = SYSUTCDATETIME()
    WHERE [PortfolioId] = @PortfolioId AND [Status] NOT IN (N'CapitalReviewRequired',N'Stopped');
    IF @@ROWCOUNT = 1 SET @ReviewActivated = 1;
END;

IF @DailyLossGuard = 1 OR @CapitalReviewRequired = 1
BEGIN
    UPDATE [dbo].[ShadowOrder]
    SET [Status] = N'Cancelled',
        [ReasonCode] = CASE WHEN @CapitalReviewRequired = 1
                            THEN N'CapitalReviewRequired' ELSE N'DailyLossGuard' END,
        [UpdatedUtc] = SYSUTCDATETIME()
    WHERE [PortfolioId] = @PortfolioId
      AND [Status] = N'Pending' AND [Side] = N'Buy';
END;

IF @DailyActivated = 1
BEGIN
    INSERT INTO [dbo].[ShadowPortfolioEvent]
    ([EventId],[PortfolioId],[SessionId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
    VALUES (@DailyEventId,@PortfolioId,@SessionId,SYSUTCDATETIME(),N'Risk',N'DailyLossGuard',N'{"threshold":-0.03}');
END;

IF @ReviewActivated = 1
BEGIN
    INSERT INTO [dbo].[ShadowPortfolioEvent]
    ([EventId],[PortfolioId],[SessionId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
    VALUES (@ReviewEventId,@PortfolioId,@SessionId,SYSUTCDATETIME(),N'Risk',N'CapitalReviewRequired',N'{"threshold":-0.10}');
END;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
            P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
            P("@DailyLossGuard", SqlDbType.Bit, dailyLossGuard),
            P("@CapitalReviewRequired", SqlDbType.Bit, capitalReviewRequired),
            P("@DailyEventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@ReviewEventId", SqlDbType.UniqueIdentifier, Guid.NewGuid())
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteSessionAsync(
        Guid sessionId,
        decimal closingValue,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE s
SET s.[Status] = N'Completed',s.[ClosingValue] = @ClosingValue,s.[CompletedUtc] = @CompletedUtc,s.[UpdatedUtc] = SYSUTCDATETIME()
FROM [dbo].[ShadowPortfolioSession] s
WHERE s.[SessionId] = @SessionId AND s.[Status] <> N'Completed';

UPDATE p
SET p.[HighestClosingValue] = CASE WHEN @ClosingValue > p.[HighestClosingValue] THEN @ClosingValue ELSE p.[HighestClosingValue] END,
    p.[UpdatedUtc] = SYSUTCDATETIME()
FROM [dbo].[ShadowPortfolio] p
JOIN [dbo].[ShadowPortfolioSession] s ON s.[PortfolioId] = p.[PortfolioId]
WHERE s.[SessionId] = @SessionId;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
            P("@ClosingValue", SqlDbType.Decimal, closingValue, precision: 19, scale: 6),
            P("@CompletedUtc", SqlDbType.DateTime2, completedUtc)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetPositionSessionOrdinalAsync(
        Guid portfolioId,
        DateTime entryTradingDate,
        DateTime currentTradingDate,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT COUNT(DISTINCT [TradingDate])
FROM [dbo].[ShadowPortfolioSession]
WHERE [PortfolioId] = @PortfolioId
  AND [TradingDate] >= @EntryTradingDate
  AND [TradingDate] <= @CurrentTradingDate;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
            P("@EntryTradingDate", SqlDbType.Date, entryTradingDate.Date),
            P("@CurrentTradingDate", SqlDbType.Date, currentTradingDate.Date)
        ]);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task ExpirePendingBuysAndMarkNoEntryAsync(
        Guid sessionId,
        DateTime occurredUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE [dbo].[ShadowOrder]
SET [Status] = N'Expired',[ReasonCode] = N'MarketClosed',[UpdatedUtc] = SYSUTCDATETIME()
WHERE [SessionId] = @SessionId AND [Side] = N'Buy' AND [Status] = N'Pending';

UPDATE [dbo].[ShadowPortfolioCandidate]
SET [State] = N'NoEntry',
    [ReasonCode] = COALESCE([ReasonCode],N'MarketClosed'),
    [LastEvaluatedUtc] = COALESCE([LastEvaluatedUtc],@OccurredUtc),
    [UpdatedUtc] = SYSUTCDATETIME()
WHERE [SessionId] = @SessionId AND [State] IN (N'Pending',N'Qualified',N'Blocked');
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
            P("@OccurredUtc", SqlDbType.DateTime2, occurredUtc)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ExpirePendingBuysBeforeDateAsync(
        DateTime currentTradingDate,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE o
SET o.[Status] = N'Expired',o.[ReasonCode] = N'PriorSessionExpired',o.[UpdatedUtc] = SYSUTCDATETIME()
FROM [dbo].[ShadowOrder] o
JOIN [dbo].[ShadowPortfolioSession] s ON s.[SessionId] = o.[SessionId]
WHERE o.[Status] = N'Pending' AND o.[Side] = N'Buy' AND s.[TradingDate] < @CurrentTradingDate;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@CurrentTradingDate", SqlDbType.Date, currentTradingDate.Date));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SystemShadowRuntimeSession?> ReadSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid portfolioId,
        DateTime tradingDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT [SessionId],[PortfolioId],[TradingDate],[CalibrationRunId],[Status],
       [ActivationBaselineUtc],[OpeningValue],[DailyLossGuardActive]
FROM [dbo].[ShadowPortfolioSession] WITH (UPDLOCK,HOLDLOCK)
WHERE [PortfolioId] = @PortfolioId AND [TradingDate] = @TradingDate;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([
            P("@PortfolioId", SqlDbType.UniqueIdentifier, portfolioId),
            P("@TradingDate", SqlDbType.Date, tradingDate.Date)
        ]);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : Utc(reader.GetDateTime(5)), reader.GetDecimal(6), reader.GetBoolean(7));
    }

    private static async Task CancelLockedOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SystemShadowPendingOrder order,
        string reason,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE [dbo].[ShadowOrder]
SET [Status] = N'Cancelled',[ReasonCode] = @Reason,[UpdatedUtc] = @NowUtc
WHERE [OrderId] = @OrderId AND [Status] = N'Pending';

INSERT INTO [dbo].[ShadowPortfolioEvent]
([EventId],[PortfolioId],[SessionId],[PositionId],[OrderId],[OccurredUtc],[EventType],[ReasonCode],[DetailsJson])
VALUES
(@EventId,@PortfolioId,@SessionId,@PositionId,@OrderId,@NowUtc,N'Order',@Reason,N'{}');
""";
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            P("@Reason", SqlDbType.NVarChar, reason, 64),
            P("@NowUtc", SqlDbType.DateTime2, nowUtc),
            P("@OrderId", SqlDbType.UniqueIdentifier, order.OrderId),
            P("@EventId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@PortfolioId", SqlDbType.UniqueIdentifier, order.PortfolioId),
            P("@SessionId", SqlDbType.UniqueIdentifier, order.SessionId),
            P("@PositionId", SqlDbType.UniqueIdentifier, order.PositionId));
    }

    private static SystemShadowGenerationInfo ReadGeneration(SqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            Enum.Parse<SystemShadowGenerationStatus>(reader.GetString(2)),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            Utc(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : Utc(reader.GetDateTime(6)),
            Utc(reader.GetDateTime(7)));

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter P(
        string name,
        SqlDbType type,
        object? value,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
    {
        var parameter = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
        if (size != 0) parameter.Size = size;
        if (precision != 0) parameter.Precision = precision;
        if (scale != 0) parameter.Scale = scale;
        return parameter;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
