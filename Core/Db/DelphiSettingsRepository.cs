#nullable enable
using Core.ML.Engine.Profit;
using Core.Runtime;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Settings use migration 026's existing append-only binding/event contract; no DDL.</summary>
public sealed class DelphiSettingsRepository : StrategyVersionRepository
{
    public async Task<DelphiSettingsCatalog> LoadAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
        var result = await ReadCatalogAsync(connection, transaction);
        transaction.Commit();
        return result;
    }

    private async Task<DelphiSettingsCatalog> ReadCatalogAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var versions = new List<StrategyVersionInfo>();
        await using (var command = new SqlCommand($"SELECT {Fields} FROM dbo.StrategyVersion ORDER BY CreatedUtc DESC", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) versions.Add(MapVersion(reader));

        var bindings = new Dictionary<Guid, (StoredProfitModelSet? Set, string Hash, string? Error)>();
        await using (var command = new SqlCommand("SELECT StrategyVersionId,ModelSetJson,ModelSetSha256 FROM dbo.StrategyModelBinding", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                Guid id = reader.GetGuid(0);
                string hash = reader.GetString(2);
                try { bindings.Add(id, (StoredProfitModelSet.Read(reader.GetString(1), hash), hash, null)); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException)
                { bindings.Add(id, (null, hash, "The preserved model assignment is damaged; it cannot be selected.")); }
            }
        }
        return new(versions.Select(v => bindings.TryGetValue(v.VersionId, out var binding)
            ? new DelphiStrategySettings(v, binding.Set, binding.Hash, binding.Error)
            : new DelphiStrategySettings(v, null, null, "No preserved model set is registered for this strategy.")).ToArray());
    }

    public Task SaveVersionAsync(DelphiSettingsChange change)
    {
        if (!change.CreatesVersion) throw new InvalidOperationException("Change thresholds and name the new version before saving.");
        return WriteAsync(change, false);
    }

    public Task ApplyAsync(DelphiSettingsChange change)
    {
        if (change.CreatesVersion) throw new InvalidOperationException("Save the version first, then assign the saved version.");
        return WriteAsync(change, true);
    }

    private async Task WriteAsync(DelphiSettingsChange change, bool assign)
    {
        // Same per-user lock used by the nightly runner and the existing manual selection tool.
        string nightlyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Nightly");
        Directory.CreateDirectory(nightlyDirectory);
        using var nightlyLock = new FileStream(Path.Combine(nightlyDirectory, "nightly.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var artifactLocks = new List<FileStream>();
        try
        {
            // Prevent replacement between verification and commit. Runtime checks hashes again on every load.
            foreach (var model in change.Source.ModelSet!.Models)
                artifactLocks.Add(new FileStream(model.Registry.ZipPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            change.VerifyArtifacts();
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            await using (var command = new SqlCommand("""
                SET XACT_ABORT ON;
                SET QUOTED_IDENTIFIER ON;
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=N'TraderVI.StrategyModelSelection',
                    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
                IF @result<0 THROW 51301, 'Delphi is running or another selection is in progress. Retry after it completes.', 1;
                """, connection, transaction))
                await command.ExecuteNonQueryAsync();

            change.ValidateCurrent(await ReadCatalogAsync(connection, transaction));
            string targetHash = change.Source.ModelSetHash!;
            if (change.CreatesVersion)
            {
                var binding = change.CreateBinding(DateTime.UtcNow);
                binding.Validate(change.TargetId, change.Source.Strategy.DecisionRef);
                string json = JsonSerializer.Serialize(binding);
                targetHash = StoredProfitModelSet.HashJson(json);
                await using var register = new SqlCommand(RegisterSql, connection, transaction);
                register.Parameters.AddWithValue("@target", change.TargetId);
                register.Parameters.AddWithValue("@source", change.Source.Strategy.VersionId);
                register.Parameters.AddWithValue("@name", change.TargetName);
                register.Parameters.AddWithValue("@code", change.CodeIdentity);
                register.Parameters.AddWithValue("@note", $"\nADR-0059 settings derived from {change.Source.Strategy.VersionName}. {change.ReviewNote}");
                register.Parameters.AddWithValue("@composite", change.Gates.MinCompositeScore);
                register.Parameters.AddWithValue("@up", change.Gates.MinUpProb);
                register.Parameters.AddWithValue("@breakout", change.Gates.MinBreakoutProb);
                register.Parameters.AddWithValue("@edge", change.Gates.MinDirectionEdge);
                register.Parameters.AddWithValue("@down", change.Gates.MaxDownProb);
                register.Parameters.AddWithValue("@breadth", change.Gates.BreadthVetoThreshold);
                register.Parameters.AddWithValue("@strongBreakout", change.Gates.StrongBreakoutOverride);
                register.Parameters.AddWithValue("@strongEdge", change.Gates.StrongEdgeOverride);
                register.Parameters.AddWithValue("@set", binding.ModelSetId);
                register.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
                register.Parameters.AddWithValue("@hash", targetHash);
                await register.ExecuteNonQueryAsync();
            }

            if (assign)
            await using (var select = new SqlCommand("""
                UPDATE dbo.StrategyVersion SET IsActive=0 WHERE VersionId=@previous AND IsActive=1;
                IF @@ROWCOUNT<>1 THROW 51302, 'The active strategy changed. Reload settings.', 1;
                UPDATE dbo.StrategyVersion SET IsActive=1 WHERE VersionId=@target AND IsActive=0;
                IF @@ROWCOUNT<>1 THROW 51303, 'The selected strategy is unavailable.', 1;
                INSERT dbo.StrategyModelSelectionEvent
                    (EventId,PreviousStrategyVersionId,SelectedStrategyVersionId,SelectedModelSetSha256,ReviewReference)
                    VALUES(@event,@previous,@target,@hash,@review);
                """, connection, transaction))
            {
                select.Parameters.AddWithValue("@previous", change.ExpectedActive.Strategy.VersionId);
                select.Parameters.AddWithValue("@target", change.TargetId);
                select.Parameters.AddWithValue("@event", change.EventId);
                select.Parameters.AddWithValue("@hash", targetHash);
                string review = $"ADR-0059; {Environment.UserName}; {change.ReviewNote}";
                select.Parameters.AddWithValue("@review", review[..System.Math.Min(512, review.Length)]);
                await select.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        finally { foreach (var file in artifactLocks) file.Dispose(); }
    }

    private const string RegisterSql = """
        INSERT dbo.StrategyVersion(VersionId,VersionName,Description,IsActive,MinCompositeScore,MinDirectionProb,
            RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,Notes,InitialCodeCommit,DecisionRef,
            MinBreakoutProb,MinDirectionEdge,MaxDownProb,BreadthVetoThreshold,StrongBreakoutOverride,StrongEdgeOverride)
        SELECT @target,@name,Description,0,@composite,@up,
            RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,CONCAT(Notes,@note),@code,DecisionRef,
            @breakout,@edge,@down,@breadth,@strongBreakout,@strongEdge
        FROM dbo.StrategyVersion WHERE VersionId=@source;
        IF @@ROWCOUNT<>1 THROW 51304, 'The source strategy is unavailable.', 1;
        INSERT dbo.StrategyModelBinding(StrategyVersionId,ModelSetId,ModelSetJson,ModelSetSha256)
            VALUES(@target,@set,@json,@hash);
        INSERT dbo.StrategyVersionModel(VersionId,ModelId,CompositeWeight,IsRequired,Role)
            SELECT @target,CONVERT(uniqueidentifier,JSON_VALUE(value,'$.Registry.ModelId')),
                CONVERT(float,JSON_VALUE(value,'$.CompositeWeight')),1,JSON_VALUE(value,'$.Role')
            FROM OPENJSON(@json,'$.Models');
        """;
}
