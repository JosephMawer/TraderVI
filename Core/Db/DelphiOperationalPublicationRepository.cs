#nullable enable

using Core.Indicators.Granville;
using Core.Oracle;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record DelphiOperationalPick(
    Guid PickId,
    DateTime PickDate,
    string Symbol,
    int Rank,
    string Direction,
    double CompositeScore,
    double? BreakoutProb,
    double? DirectionProb,
    double? VolExpansionProb,
    double? RelStrengthProb,
    double? ExpectedReturn,
    decimal? SuggestedSize,
    double? AllocationPercent,
    Guid? StrategyVersionId,
    string? Notes,
    string Lens,
    DecisionDossier? Dossier);

public sealed record DelphiOperationalPublicationResult(
    DateTime PickDate,
    int PickCount,
    int DossierCount,
    int GranvilleLogCount);

/// <summary>
/// Replaces one recommendation date's mutable Delphi projection atomically.
/// Immutable calibration evidence is deliberately outside this transaction.
/// Empty input is a valid publication that clears stale same-date rows.
/// </summary>
public sealed class DelphiOperationalPublicationRepository : SQLBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<DelphiOperationalPublicationResult> ReplaceAsync(
        DateTime pickDate,
        IReadOnlyList<DelphiOperationalPick> picks,
        GranvilleDailyForecast? granvilleForecast,
        CancellationToken cancellationToken = default)
    {
        Validate(pickDate, picks);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            await ExecuteAsync(
                "DELETE FROM [dbo].[LlmNarrative] WHERE [PickDate] = @PickDate;",
                connection,
                transaction,
                pickDate,
                cancellationToken);
            await ExecuteAsync(
                "DELETE FROM [dbo].[DecisionDossier] WHERE [PickDate] = @PickDate;",
                connection,
                transaction,
                pickDate,
                cancellationToken);
            await ExecuteAsync(
                "DELETE FROM [dbo].[DailyPick] WHERE [PickDate] = @PickDate;",
                connection,
                transaction,
                pickDate,
                cancellationToken);
            await ExecuteAsync(
                "DELETE FROM [dbo].[GranvilleIndicatorLog] WHERE [EvalDate] = @PickDate;",
                connection,
                transaction,
                pickDate,
                cancellationToken);

            foreach (DelphiOperationalPick pick in picks)
                await InsertPickAsync(connection, transaction, pick, cancellationToken);

            int dossierCount = 0;
            foreach (DelphiOperationalPick pick in picks.Where(pick => pick.Dossier is not null))
            {
                await InsertDossierAsync(
                    connection,
                    transaction,
                    pick.Dossier!,
                    cancellationToken);
                dossierCount++;
            }

            int granvilleCount = 0;
            if (granvilleForecast is not null)
            {
                foreach (GranvilleResult result in granvilleForecast.Results)
                {
                    await InsertGranvilleAsync(
                        connection,
                        transaction,
                        pickDate,
                        granvilleForecast,
                        result,
                        cancellationToken);
                    granvilleCount++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new DelphiOperationalPublicationResult(
                pickDate.Date,
                picks.Count,
                dossierCount,
                granvilleCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static void Validate(DateTime pickDate, IReadOnlyList<DelphiOperationalPick> picks)
    {
        if (pickDate == default)
            throw new ArgumentException("Publication date is required.", nameof(pickDate));
        ArgumentNullException.ThrowIfNull(picks);

        if (picks.Any(pick => pick.PickId == Guid.Empty))
            throw new ArgumentException("Every operational pick requires a stable ID.", nameof(picks));
        if (picks.Any(pick => pick.PickDate.Date != pickDate.Date))
            throw new ArgumentException("Every operational pick must match the publication date.", nameof(picks));
        if (picks.Select(pick => pick.PickId).Distinct().Count() != picks.Count)
            throw new ArgumentException("Operational pick IDs must be unique.", nameof(picks));
        if (picks.GroupBy(pick => new { Lens = pick.Lens.ToUpperInvariant(), pick.Rank })
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Operational ranks must be unique within each lens.", nameof(picks));
        }

        foreach (DelphiOperationalPick pick in picks)
        {
            if (string.IsNullOrWhiteSpace(pick.Symbol) || pick.Symbol.Length > 16)
                throw new ArgumentException("Pick symbols are required and limited to 16 characters.", nameof(picks));
            if (pick.Rank <= 0)
                throw new ArgumentException("Pick ranks must be positive.", nameof(picks));
            if (string.IsNullOrWhiteSpace(pick.Direction) || pick.Direction.Length > 8)
                throw new ArgumentException("Pick directions are required and limited to 8 characters.", nameof(picks));
            if (string.IsNullOrWhiteSpace(pick.Lens) || pick.Lens.Length > 16)
                throw new ArgumentException("Pick lenses are required and limited to 16 characters.", nameof(picks));
            if (pick.Notes?.Length > 512)
                throw new ArgumentException("Pick notes cannot exceed 512 characters.", nameof(picks));
            if (pick.Dossier is not null &&
                (pick.Dossier.PickId != pick.PickId ||
                 pick.Dossier.PickDate.Date != pickDate.Date ||
                 !string.Equals(pick.Dossier.Symbol, pick.Symbol, StringComparison.OrdinalIgnoreCase) ||
                 pick.Dossier.Rank != pick.Rank))
            {
                throw new ArgumentException("A dossier must match its operational pick.", nameof(picks));
            }
        }
    }

    private static async Task ExecuteAsync(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        DateTime pickDate,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPickAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DelphiOperationalPick pick,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO [dbo].[DailyPick]
([PickId],[PickDate],[Symbol],[Rank],[Direction],[CompositeScore],[BreakoutProb],
 [DirectionProb],[VolExpansionProb],[RelStrengthProb],[ExpectedReturn],[SuggestedSize],
 [AllocationPercent],[StrategyVersionId],[CreatedUtc],[Notes],[Lens])
VALUES
(@PickId,@PickDate,@Symbol,@Rank,@Direction,@CompositeScore,@BreakoutProb,
 @DirectionProb,@VolExpansionProb,@RelStrengthProb,@ExpectedReturn,@SuggestedSize,
 @AllocationPercent,@StrategyVersionId,SYSUTCDATETIME(),@Notes,@Lens);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@PickId", SqlDbType.UniqueIdentifier, pick.PickId),
            P("@PickDate", SqlDbType.Date, pick.PickDate.Date),
            P("@Symbol", SqlDbType.NVarChar, pick.Symbol, 16),
            P("@Rank", SqlDbType.Int, pick.Rank),
            P("@Direction", SqlDbType.NVarChar, pick.Direction, 8),
            P("@CompositeScore", SqlDbType.Float, pick.CompositeScore),
            P("@BreakoutProb", SqlDbType.Float, pick.BreakoutProb),
            P("@DirectionProb", SqlDbType.Float, pick.DirectionProb),
            P("@VolExpansionProb", SqlDbType.Float, pick.VolExpansionProb),
            P("@RelStrengthProb", SqlDbType.Float, pick.RelStrengthProb),
            P("@ExpectedReturn", SqlDbType.Float, pick.ExpectedReturn),
            DecimalParameter("@SuggestedSize", pick.SuggestedSize, 18, 2),
            P("@AllocationPercent", SqlDbType.Float, pick.AllocationPercent),
            P("@StrategyVersionId", SqlDbType.UniqueIdentifier, pick.StrategyVersionId),
            P("@Notes", SqlDbType.NVarChar, pick.Notes, 512),
            P("@Lens", SqlDbType.NVarChar, pick.Lens, 16)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDossierAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DecisionDossier dossier,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO [dbo].[DecisionDossier]
([DossierId],[PickDate],[PickId],[Symbol],[Rank],[SchemaVersion],[DossierJson],[CreatedUtc])
VALUES
(@DossierId,@PickDate,@PickId,@Symbol,@Rank,@SchemaVersion,@DossierJson,SYSUTCDATETIME());
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@DossierId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@PickDate", SqlDbType.Date, dossier.PickDate.Date),
            P("@PickId", SqlDbType.UniqueIdentifier, dossier.PickId),
            P("@Symbol", SqlDbType.NVarChar, dossier.Symbol, 16),
            P("@Rank", SqlDbType.Int, dossier.Rank),
            P("@SchemaVersion", SqlDbType.Int, dossier.SchemaVersion),
            P("@DossierJson", SqlDbType.NVarChar, JsonSerializer.Serialize(dossier, JsonOptions), -1)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGranvilleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DateTime pickDate,
        GranvilleDailyForecast forecast,
        GranvilleResult result,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO [dbo].[GranvilleIndicatorLog]
([LogId],[EvalDate],[IndicatorNumber],[Category],[Name],[Signal],[GranvillePoints],
 [Description],[NetPoints],[CompositeAdjustment],[CreatedUtc])
VALUES
(@LogId,@EvalDate,@IndicatorNumber,@Category,@Name,@Signal,@GranvillePoints,
 @Description,@NetPoints,@CompositeAdjustment,SYSUTCDATETIME());
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@LogId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
            P("@EvalDate", SqlDbType.Date, pickDate.Date),
            P("@IndicatorNumber", SqlDbType.Int, result.IndicatorNumber),
            P("@Category", SqlDbType.NVarChar, result.Category.ToString(), 50),
            P("@Name", SqlDbType.NVarChar, result.Name, 128),
            P("@Signal", SqlDbType.NVarChar, result.Signal.ToString(), 20),
            P("@GranvillePoints", SqlDbType.Int, result.GranvillePoints),
            P("@Description", SqlDbType.NVarChar, result.Description, 512),
            P("@NetPoints", SqlDbType.Int, forecast.NetPoints),
            P("@CompositeAdjustment", SqlDbType.Float, forecast.CompositeAdjustment)
        ]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter P(string name, SqlDbType type, object? value, int? size = null)
    {
        var parameter = size.HasValue
            ? new SqlParameter(name, type, size.Value)
            : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    private static SqlParameter DecimalParameter(
        string name,
        decimal? value,
        byte precision,
        byte scale) =>
        new(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value ?? (object)DBNull.Value
        };
}
