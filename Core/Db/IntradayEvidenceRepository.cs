#nullable enable

using Core.TMX.Models.Domain;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record StoredIntradayOutcomeBar(
    DateTime EventUtc,
    DateTime FirstReceivedUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string AuditState,
    int EvidenceSchemaVersion,
    string SourceContractVersion);

public sealed class IntradayEvidenceRepository : SQLBase
{
    public async Task<IReadOnlyList<StoredIntradayOutcomeBar>> GetOutcomeBarsAsync(
        string symbol,
        int intervalMinutes,
        DateTime fromUtc,
        DateTime throughUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (intervalMinutes is not (5 or 15))
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        if (fromUtc.Kind != DateTimeKind.Utc || throughUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Outcome evidence bounds must be UTC.");
        if (throughUtc < fromUtc)
            throw new ArgumentOutOfRangeException(nameof(throughUtc));

        const string sql = """
SELECT b.[EventUtc],o.[ReceivedUtc],b.[Open],b.[High],b.[Low],b.[Close],b.[Volume],
       o.[AuditState],o.[EvidenceSchemaVersion],o.[SourceContractVersion]
FROM [dbo].[IntradayEvidenceBar] b
JOIN [dbo].[IntradayPollObservation] o
  ON o.[ObservationId] = b.[FirstObservationId]
WHERE b.[Symbol] = @Symbol
  AND b.[IntervalMinutes] = @IntervalMinutes
  AND b.[EventUtc] >= @FromUtc
  AND b.[EventUtc] <= @ThroughUtc
  AND o.[ReceivedUtc] IS NOT NULL
  AND o.[AuditState] <> N'Invalid'
  AND o.[EvidenceSchemaVersion] = @EvidenceSchemaVersion
  AND o.[SourceContractVersion] = @SourceContractVersion
  AND o.[PolicyVersion] = @PolicyVersion
ORDER BY b.[EventUtc];
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@Symbol", SqlDbType.NVarChar, symbol.Trim().ToUpperInvariant(), 20));
        command.Parameters.Add(P("@IntervalMinutes", SqlDbType.SmallInt, intervalMinutes));
        command.Parameters.Add(P("@FromUtc", SqlDbType.DateTime2, fromUtc));
        command.Parameters.Add(P("@ThroughUtc", SqlDbType.DateTime2, throughUtc));
        command.Parameters.Add(P("@EvidenceSchemaVersion", SqlDbType.Int, IntradayEvidenceVersions.Schema));
        command.Parameters.Add(P("@SourceContractVersion", SqlDbType.NVarChar, IntradayEvidenceVersions.SourceContract, 64));
        command.Parameters.Add(P("@PolicyVersion", SqlDbType.NVarChar, IntradayEvidenceVersions.Policy, 64));
        var result = new List<StoredIntradayOutcomeBar>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StoredIntradayOutcomeBar(
                DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetString(9)));
        }

        return result.AsReadOnly();
    }

    public async Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT CASE
    WHEN OBJECT_ID(N'dbo.IntradayPollObservation', N'U') IS NOT NULL
     AND OBJECT_ID(N'dbo.IntradayEvidenceBar', N'U') IS NOT NULL
    THEN CAST(1 AS BIT)
    ELSE CAST(0 AS BIT)
END;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Intraday schema check returned no result."));
    }

    public async Task<IntradayEvidenceAppendResult> AppendCompletedBatchAsync(
        IntradayPollContext context,
        TmxIntradayBatch batch,
        CancellationToken cancellationToken = default)
    {
        IntradayEvidencePersistencePlanner.Validate(
            context,
            batch,
            Array.Empty<StoredIntradayEvidenceBar>());

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            IReadOnlyList<StoredIntradayEvidenceBar> existing =
                await LoadExistingAsync(connection, transaction, batch, cancellationToken);
            IntradayEvidenceWritePlan plan =
                IntradayEvidencePersistencePlanner.Create(context, batch, existing);
            Guid observationId = Guid.NewGuid();
            await InsertObservationAsync(
                connection,
                transaction,
                observationId,
                context,
                batch,
                plan,
                cancellationToken);

            if (plan.ConflictingBars.Count == 0)
            {
                foreach (OhlcvBar bar in plan.NewBars)
                {
                    await InsertBarAsync(
                        connection,
                        transaction,
                        observationId,
                        batch.Symbol,
                        batch.IntervalMinutes,
                        bar,
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new IntradayEvidenceAppendResult(
                observationId,
                plan.AuditState,
                plan.AuditCode,
                plan.CompletedBars.Count,
                plan.NewBars.Count,
                plan.ConflictingBars.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Guid> AppendFailedObservationAsync(
        IntradayPollContext context,
        string symbol,
        int intervalMinutes,
        DateTime requestedStartUtc,
        DateTime requestedEndUtc,
        DateTime fetchStartedUtc,
        int attemptCount,
        int requestCount,
        string auditCode,
        CancellationToken cancellationToken = default)
    {
        ValidateFailure(
            context,
            symbol,
            intervalMinutes,
            requestedStartUtc,
            requestedEndUtc,
            fetchStartedUtc,
            attemptCount,
            requestCount,
            auditCode);

        const string sql = """
INSERT INTO [dbo].[IntradayPollObservation]
([ObservationId],[PollCycleId],[Purpose],[Symbol],[IntervalMinutes],[EvidenceSchemaVersion],
 [Provider],[SourceContractVersion],[CollectorVersion],[PolicyVersion],[CodeCommit],[WorkingTreeState],
 [RequestedStartUtc],[RequestedEndUtc],[FetchStartedUtc],[ReceivedUtc],[AttemptCount],[RequestCount],
 [ReturnedBarCount],[CompletedBarCount],[PersistedNewBarCount],[LatestReturnedEventUtc],
 [LatestCompletedEventUtc],[AuditState],[AuditCode])
VALUES
(@ObservationId,@PollCycleId,@Purpose,@Symbol,@IntervalMinutes,@EvidenceSchemaVersion,
 @Provider,@SourceContractVersion,@CollectorVersion,@PolicyVersion,@CodeCommit,@WorkingTreeState,
 @RequestedStartUtc,@RequestedEndUtc,@FetchStartedUtc,NULL,@AttemptCount,@RequestCount,
 0,0,0,NULL,NULL,'Failed',@AuditCode);
""";
        Guid observationId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(ContextParameters(observationId, context, symbol, intervalMinutes));
        command.Parameters.Add(P("@RequestedStartUtc", SqlDbType.DateTime2, requestedStartUtc));
        command.Parameters.Add(P("@RequestedEndUtc", SqlDbType.DateTime2, requestedEndUtc));
        command.Parameters.Add(P("@FetchStartedUtc", SqlDbType.DateTime2, fetchStartedUtc));
        command.Parameters.Add(P("@AttemptCount", SqlDbType.Int, attemptCount));
        command.Parameters.Add(P("@RequestCount", SqlDbType.Int, requestCount));
        command.Parameters.Add(P("@AuditCode", SqlDbType.NVarChar, auditCode, 64));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return observationId;
    }

    public async Task<IReadOnlyList<IntradayPollObservationInfo>> GetRecentObservationsAsync(
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        count = System.Math.Clamp(count, 1, 1000);
        string sql = $"""
SELECT TOP {count}
 [ObservationId],[PollCycleId],[Symbol],[IntervalMinutes],[ReceivedUtc],
 [ReturnedBarCount],[CompletedBarCount],[PersistedNewBarCount],
 [LatestCompletedEventUtc],[AuditState],[AuditCode],[CreatedUtc]
FROM [dbo].[IntradayPollObservation]
ORDER BY [CreatedUtc] DESC;
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        var result = new List<IntradayPollObservationInfo>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new IntradayPollObservationInfo(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt16(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDateTime(11)));
        }
        return result.AsReadOnly();
    }

    private static async Task<IReadOnlyList<StoredIntradayEvidenceBar>> LoadExistingAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        TmxIntradayBatch batch,
        CancellationToken cancellationToken)
    {
        List<OhlcvBar> completed = batch.Bars
            .Where(bar =>
                bar.TimestampUtc.AddMinutes(batch.IntervalMinutes) <= batch.ReceivedUtc)
            .OrderBy(bar => bar.TimestampUtc)
            .ToList();
        if (completed.Count == 0)
            return Array.Empty<StoredIntradayEvidenceBar>();

        const string sql = """
SELECT [EventUtc],[Open],[High],[Low],[Close],[Volume]
FROM [dbo].[IntradayEvidenceBar]
WHERE [Symbol] = @Symbol
  AND [IntervalMinutes] = @IntervalMinutes
  AND [EventUtc] BETWEEN @FirstEventUtc AND @LastEventUtc
ORDER BY [EventUtc];
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(P("@Symbol", SqlDbType.NVarChar, batch.Symbol, 20));
        command.Parameters.Add(P("@IntervalMinutes", SqlDbType.SmallInt, batch.IntervalMinutes));
        command.Parameters.Add(P("@FirstEventUtc", SqlDbType.DateTime2, completed[0].TimestampUtc));
        command.Parameters.Add(P("@LastEventUtc", SqlDbType.DateTime2, completed[^1].TimestampUtc));
        var result = new List<StoredIntradayEvidenceBar>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StoredIntradayEvidenceBar(
                reader.GetDateTime(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetInt64(5)));
        }
        return result.AsReadOnly();
    }

    private static async Task InsertObservationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid observationId,
        IntradayPollContext context,
        TmxIntradayBatch batch,
        IntradayEvidenceWritePlan plan,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO [dbo].[IntradayPollObservation]
([ObservationId],[PollCycleId],[Purpose],[Symbol],[IntervalMinutes],[EvidenceSchemaVersion],
 [Provider],[SourceContractVersion],[CollectorVersion],[PolicyVersion],[CodeCommit],[WorkingTreeState],
 [RequestedStartUtc],[RequestedEndUtc],[FetchStartedUtc],[ReceivedUtc],[AttemptCount],[RequestCount],
 [ReturnedBarCount],[CompletedBarCount],[PersistedNewBarCount],[LatestReturnedEventUtc],
 [LatestCompletedEventUtc],[AuditState],[AuditCode])
VALUES
(@ObservationId,@PollCycleId,@Purpose,@Symbol,@IntervalMinutes,@EvidenceSchemaVersion,
 @Provider,@SourceContractVersion,@CollectorVersion,@PolicyVersion,@CodeCommit,@WorkingTreeState,
 @RequestedStartUtc,@RequestedEndUtc,@FetchStartedUtc,@ReceivedUtc,@AttemptCount,@RequestCount,
 @ReturnedBarCount,@CompletedBarCount,@PersistedNewBarCount,@LatestReturnedEventUtc,
 @LatestCompletedEventUtc,@AuditState,@AuditCode);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(
            ContextParameters(observationId, context, batch.Symbol, batch.IntervalMinutes));
        command.Parameters.Add(P("@RequestedStartUtc", SqlDbType.DateTime2, batch.RequestedStartUtc));
        command.Parameters.Add(P("@RequestedEndUtc", SqlDbType.DateTime2, batch.RequestedEndUtc));
        command.Parameters.Add(P("@FetchStartedUtc", SqlDbType.DateTime2, batch.FetchStartedUtc));
        command.Parameters.Add(P("@ReceivedUtc", SqlDbType.DateTime2, batch.ReceivedUtc));
        command.Parameters.Add(P("@AttemptCount", SqlDbType.Int, batch.AttemptCount));
        command.Parameters.Add(P("@RequestCount", SqlDbType.Int, batch.RequestCount));
        command.Parameters.Add(P("@ReturnedBarCount", SqlDbType.Int, batch.Bars.Count));
        command.Parameters.Add(P("@CompletedBarCount", SqlDbType.Int, plan.CompletedBars.Count));
        command.Parameters.Add(P("@PersistedNewBarCount", SqlDbType.Int, plan.NewBars.Count));
        command.Parameters.Add(P("@LatestReturnedEventUtc", SqlDbType.DateTime2, batch.LatestEventUtc));
        command.Parameters.Add(P(
            "@LatestCompletedEventUtc",
            SqlDbType.DateTime2,
            plan.CompletedBars.LastOrDefault()?.TimestampUtc));
        command.Parameters.Add(P("@AuditState", SqlDbType.NVarChar, plan.AuditState.ToString(), 16));
        command.Parameters.Add(P("@AuditCode", SqlDbType.NVarChar, plan.AuditCode, 64));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertBarAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid observationId,
        string symbol,
        int intervalMinutes,
        OhlcvBar bar,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO [dbo].[IntradayEvidenceBar]
([EvidenceBarId],[FirstObservationId],[Symbol],[IntervalMinutes],[EventUtc],
 [Open],[High],[Low],[Close],[Volume])
VALUES
(@EvidenceBarId,@FirstObservationId,@Symbol,@IntervalMinutes,@EventUtc,
 @Open,@High,@Low,@Close,@Volume);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(P("@EvidenceBarId", SqlDbType.UniqueIdentifier, Guid.NewGuid()));
        command.Parameters.Add(P("@FirstObservationId", SqlDbType.UniqueIdentifier, observationId));
        command.Parameters.Add(P("@Symbol", SqlDbType.NVarChar, symbol, 20));
        command.Parameters.Add(P("@IntervalMinutes", SqlDbType.SmallInt, intervalMinutes));
        command.Parameters.Add(P("@EventUtc", SqlDbType.DateTime2, bar.TimestampUtc));
        command.Parameters.Add(DecimalParameter("@Open", bar.Open));
        command.Parameters.Add(DecimalParameter("@High", bar.High));
        command.Parameters.Add(DecimalParameter("@Low", bar.Low));
        command.Parameters.Add(DecimalParameter("@Close", bar.Close));
        command.Parameters.Add(P("@Volume", SqlDbType.BigInt, bar.Volume));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter[] ContextParameters(
        Guid observationId,
        IntradayPollContext context,
        string symbol,
        int intervalMinutes) =>
    [
        P("@ObservationId", SqlDbType.UniqueIdentifier, observationId),
        P("@PollCycleId", SqlDbType.UniqueIdentifier, context.PollCycleId),
        P("@Purpose", SqlDbType.NVarChar, context.Purpose.ToString(), 32),
        P("@Symbol", SqlDbType.NVarChar, symbol, 20),
        P("@IntervalMinutes", SqlDbType.SmallInt, intervalMinutes),
        P("@EvidenceSchemaVersion", SqlDbType.Int, IntradayEvidenceVersions.Schema),
        P("@Provider", SqlDbType.NVarChar, IntradayEvidenceVersions.Provider, 32),
        P("@SourceContractVersion", SqlDbType.NVarChar, IntradayEvidenceVersions.SourceContract, 64),
        P("@CollectorVersion", SqlDbType.NVarChar, context.CollectorVersion, 64),
        P("@PolicyVersion", SqlDbType.NVarChar, context.PolicyVersion, 64),
        P("@CodeCommit", SqlDbType.NVarChar, context.Code.Commit, 128),
        P("@WorkingTreeState", SqlDbType.NVarChar, context.Code.WorkingTreeState, 16)
    ];

    private static void ValidateFailure(
        IntradayPollContext context,
        string symbol,
        int intervalMinutes,
        DateTime requestedStartUtc,
        DateTime requestedEndUtc,
        DateTime fetchStartedUtc,
        int attemptCount,
        int requestCount,
        string auditCode)
    {
        var emptyBatch = new TmxIntradayBatch(
            symbol,
            intervalMinutes,
            requestedStartUtc,
            requestedEndUtc,
            fetchStartedUtc,
            fetchStartedUtc,
            attemptCount,
            requestCount,
            Array.Empty<OhlcvBar>());
        IntradayEvidencePersistencePlanner.Validate(
            context,
            emptyBatch,
            Array.Empty<StoredIntradayEvidenceBar>());
        if (string.IsNullOrWhiteSpace(auditCode) || auditCode.Length > 64)
            throw new ArgumentException("A bounded failure audit code is required.", nameof(auditCode));
    }

    private static SqlParameter P(
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = size.HasValue
            ? new SqlParameter(name, type, size.Value)
            : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    private static SqlParameter DecimalParameter(string name, decimal value) =>
        new(name, SqlDbType.Decimal)
        {
            Precision = 19,
            Scale = 6,
            Value = value
        };
}
