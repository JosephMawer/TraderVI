#nullable enable

using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record TraderDbBackupPaths(
    string StagingDirectory,
    string DestinationDirectory)
{
    public const string StagingEnvironmentVariable = "TRADERVI_BACKUP_STAGING_DIRECTORY";
    public const string DestinationEnvironmentVariable = "TRADERVI_BACKUP_DESTINATION_DIRECTORY";

    public static TraderDbBackupPaths FromEnvironment()
    {
        var staging = Environment.GetEnvironmentVariable(StagingEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(staging))
            staging = @"C:\ProgramData\TraderVI\Backups";

        var destination = Environment.GetEnvironmentVariable(DestinationEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(destination))
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (string.IsNullOrWhiteSpace(oneDrive))
            {
                throw new InvalidOperationException(
                    $"OneDrive is not configured. Set {DestinationEnvironmentVariable} to the approved backup destination.");
            }

            destination = Path.Combine(oneDrive, "Joseph", "Tradervi", "backups");
        }

        return new TraderDbBackupPaths(
            Normalize(staging),
            Normalize(destination));
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
}

public sealed record TraderDbBackupResult(
    string StagingFile,
    string DestinationFile,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CompletedAt);

public sealed class TraderDbBackupService
{
    private const string DatabaseName = "TraderDB";
    private readonly string _connectionString;
    private readonly TraderDbBackupPaths _paths;

    public TraderDbBackupService(string connectionString, TraderDbBackupPaths paths)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<TraderDbBackupResult> CreateAndReplicateAsync(
        Action<string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        RequireExistingDirectory(_paths.StagingDirectory, "backup staging");
        RequireExistingDirectory(_paths.DestinationDirectory, "backup destination");

        var now = DateTimeOffset.Now;
        var fileName = $"{DatabaseName}_FULL_{now:yyyyMMdd_HHmmss_fff}.bak";
        var stagingFile = Path.Combine(_paths.StagingDirectory, fileName);

        if (File.Exists(stagingFile))
            throw new IOException($"Refusing to overwrite existing backup file: {stagingFile}");

        reportProgress?.Invoke($"Creating compressed checksum backup: {stagingFile}");
        string incompleteFile = stagingFile + ".partial";
        if (File.Exists(incompleteFile)) throw new IOException("An incomplete backup already uses this timestamp.");
        await CreateAndVerifySqlBackupAsync(incompleteFile, cancellationToken);
        File.Move(incompleteFile, stagingFile, overwrite: false);

        var stagingInfo = new FileInfo(stagingFile);
        if (!stagingInfo.Exists || stagingInfo.Length == 0)
            throw new IOException($"SQL Server did not create a readable backup file: {stagingFile}");

        reportProgress?.Invoke("SQL checksum verification passed. Copying completed backup to OneDrive...");
        var copy = await VerifiedBackupFileCopier.CopyAsync(
            stagingFile,
            _paths.DestinationDirectory,
            cancellationToken);

        // A failed backup or copy never reaches retention. Keep the latest two in each location.
        // Cleanup failure must be visible without misreporting a verified backup as a failed backup.
        try
        {
            var completed = await ReadCompletedBackupNamesAsync(_connectionString, cancellationToken);
            TraderDbBackupRetention.Apply(stagingFile, copy.DestinationFile, copy.SizeBytes, completed, reportProgress);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or SqlException or OperationCanceledException)
        {
            reportProgress?.Invoke($"WARNING: Backup verified, but retention did not complete: {exception.Message}");
        }

        return new TraderDbBackupResult(
            stagingFile,
            copy.DestinationFile,
            copy.SizeBytes,
            copy.Sha256,
            DateTimeOffset.Now);
    }

    public static async Task<IReadOnlySet<string>> ReadCompletedBackupNamesAsync(string connectionString, CancellationToken token = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
SELECT DISTINCT f.physical_device_name
FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.backup_finish_date IS NOT NULL
 AND b.has_backup_checksums=1 AND b.is_damaged=0 AND f.device_type=2;
""", connection);
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string name = Path.GetFileName(reader.GetString(0));
            if (name.EndsWith(".bak.partial", StringComparison.Ordinal)) name = name[..^8];
            names.Add(name);
        }
        return names;
    }

    private async Task CreateAndVerifySqlBackupAsync(
        string backupFile,
        CancellationToken cancellationToken)
    {
        const string sql = """
            USE [master];
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            IF DB_ID(N'TraderDB') IS NULL
                THROW 51020, 'TraderDB does not exist on this SQL Server instance.', 1;

            IF CONVERT(nvarchar(60), DATABASEPROPERTYEX(N'TraderDB', N'Status')) <> N'ONLINE'
                THROW 51021, 'TraderDB must be ONLINE before it can be backed up.', 1;

            BACKUP DATABASE [TraderDB]
                TO DISK = @backupFile
                WITH COMPRESSION,
                     CHECKSUM,
                     STATS = 10;

            RESTORE VERIFYONLY
                FROM DISK = @backupFile
                WITH CHECKSUM;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 600
        };
        command.Parameters.Add(new SqlParameter("@backupFile", SqlDbType.NVarChar, 4000)
        {
            Value = backupFile
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void RequireExistingDirectory(string path, string role)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"The configured {role} directory does not exist: {path}");
        }
    }
}

public sealed record VerifiedBackupCopyResult(
    string DestinationFile,
    long SizeBytes,
    string Sha256);

public static class VerifiedBackupFileCopier
{
    public static async Task<VerifiedBackupCopyResult> CopyAsync(
        string sourceFile,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var sourceFull = Path.GetFullPath(sourceFile);
        var destinationDirectoryFull = Path.GetFullPath(destinationDirectory);

        if (!File.Exists(sourceFull))
            throw new FileNotFoundException("The verified staging backup does not exist.", sourceFull);
        if (!Directory.Exists(destinationDirectoryFull))
            throw new DirectoryNotFoundException($"The backup destination does not exist: {destinationDirectoryFull}");

        var destinationFile = Path.Combine(destinationDirectoryFull, Path.GetFileName(sourceFull));
        if (File.Exists(destinationFile))
            throw new IOException($"Refusing to overwrite existing destination backup: {destinationFile}");

        var partialFile = Path.Combine(
            destinationDirectoryFull,
            $".{Path.GetFileName(sourceFull)}.{Guid.NewGuid():N}.partial");

        try
        {
            await CopyFileAsync(sourceFull, partialFile, cancellationToken);

            var sourceHash = await ComputeSha256Async(sourceFull, cancellationToken);
            var partialHash = await ComputeSha256Async(partialFile, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, partialHash))
                throw new IOException("The copied backup failed SHA-256 verification.");

            File.Move(partialFile, destinationFile, overwrite: false);

            var destinationInfo = new FileInfo(destinationFile);
            return new VerifiedBackupCopyResult(
                destinationFile,
                destinationInfo.Length,
                Convert.ToHexString(sourceHash));
        }
        catch (Exception copyException)
        {
            try
            {
                if (File.Exists(partialFile))
                    File.Delete(partialFile);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"Backup copy failed and its temporary file could not be removed: {partialFile}",
                    copyException,
                    cleanupException);
            }

            throw;
        }
    }

    private static async Task CopyFileAsync(
        string sourceFile,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;

        await using var source = new FileStream(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, bufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ComputeSha256Async(
        string file,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }
}
