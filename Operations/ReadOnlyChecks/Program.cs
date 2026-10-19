using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Independent of Core: never constructs a collector, model, broker, or trading host.
// Install a reviewed build outside the workspace before approving its fixed invocation.
const string mailbox = @"C:\src\TraderVI\artifacts\read-only-db";
const string principal = "TraderVIReadOnlyChecks";
const int maxRows = 10000;
const int maxBytes = 8 * 1024 * 1024;
string? resultPath = null;
try
{
    if (args.Length == 1 && args[0] == "--self-test") return QueryPolicy.SelfTest();
    if (args.Length != 0) throw new InvalidOperationException("This helper accepts no command-line arguments.");
    CheckPath(mailbox);
    using var mutex = new FileStream(Path.Combine(mailbox, "running.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);
    var requestPath = Path.Combine(mailbox, "request.sql");
    CheckPath(requestPath);
    string query;
    using (var request = new FileStream(requestPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        if (request.Length is 0 or > 1048576) throw new InvalidOperationException("Query must be 1 byte to 1 MiB.");
        using var text = new StreamReader(request, Encoding.UTF8, true);
        query = text.ReadToEnd();
    }
    QueryPolicy.Validate(query);
    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
    resultPath = Path.Combine(mailbox, $"result-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var token = budget.Token;
    // Endpoint, authentication and principal are fixed; no environment/config overrides or pooling.
    await using var connection = new SqlConnection(new SqlConnectionStringBuilder
    {
        DataSource = "localhost", InitialCatalog = "TraderDB", IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
        Pooling = false, ConnectTimeout = 10, ApplicationName = "TraderVI.ReadOnlyChecks",
        Enlist = false
    }.ConnectionString);
    await connection.OpenAsync(token);
    await using (var guard = connection.CreateCommand())
    {
        guard.CommandTimeout = 10;
        // Only fixed SQL runs before irreversible impersonation. Caller-supplied SQL is separate.
        guard.CommandText = """
            SET NOCOUNT ON;
            SET LOCK_TIMEOUT 5000;
            SET DEADLOCK_PRIORITY LOW;
            IF DB_NAME() <> N'TraderDB' OR EXISTS
              (SELECT 1 FROM sys.databases WHERE database_id=DB_ID()
               AND (is_trustworthy_on=1 OR is_db_chaining_on=1))
              THROW 51000, 'Unexpected database security configuration.', 1;
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals
              WHERE name=N'TraderVIReadOnlyChecks' AND type='S' AND authentication_type=0)
              THROW 51001, 'Install the reviewed read-only principal first.', 1;
            IF EXISTS (SELECT 1 FROM sys.database_role_members
              WHERE member_principal_id=USER_ID(N'TraderVIReadOnlyChecks'))
              OR EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id=USER_ID(N'TraderVIReadOnlyChecks'))
              OR EXISTS (SELECT 1 FROM sys.objects WHERE principal_id=USER_ID(N'TraderVIReadOnlyChecks'))
              THROW 51002, 'Read-only principal has unexpected membership or ownership.', 1;
            IF EXISTS (SELECT 1 FROM sys.synonyms WHERE schema_id=SCHEMA_ID(N'dbo'))
              OR EXISTS (SELECT 1 FROM sys.assemblies WHERE is_user_defined=1)
              OR EXISTS (SELECT 1 FROM sys.external_tables WHERE schema_id=SCHEMA_ID(N'dbo'))
              OR EXISTS (SELECT 1 FROM sys.sql_expression_dependencies
                WHERE OBJECT_SCHEMA_NAME(referencing_id)=N'dbo'
                  AND (referenced_database_name IS NOT NULL OR referenced_server_name IS NOT NULL))
              THROW 51006, 'External or executable schema dependencies require separate review.', 1;
            EXECUTE AS USER = 'TraderVIReadOnlyChecks' WITH NO REVERT;
            IF USER_NAME() <> N'TraderVIReadOnlyChecks'
              THROW 51003, 'Read-only context was not established.', 1;
            IF EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, 'DATABASE')
              WHERE permission_name NOT IN ('CONNECT', 'SELECT', 'VIEW DEFINITION',
                'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION', 'VIEW ANY COLUMN MASTER KEY DEFINITION'))
              OR EXISTS (SELECT 1 FROM sys.fn_my_permissions('dbo', 'SCHEMA')
                WHERE permission_name NOT IN ('SELECT', 'VIEW DEFINITION'))
              THROW 51004, 'Read-only effective permissions have drifted.', 1;
            """;
        await guard.ExecuteNonQueryAsync(token);
    }
    // Guard against object/column grants overriding a wider DENY, and public-role drift.
    await using (var permissions = connection.CreateCommand())
    {
        permissions.CommandTimeout = 10;
        permissions.CommandText = """
            IF EXISTS (SELECT 1 FROM sys.database_permissions
              WHERE grantee_principal_id IN (USER_ID(N'TraderVIReadOnlyChecks'), DATABASE_PRINCIPAL_ID(N'public'))
                AND state IN ('G','W')
                AND permission_name NOT IN ('CONNECT','SELECT','VIEW DEFINITION',
                  'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION','VIEW ANY COLUMN MASTER KEY DEFINITION')
                AND NOT (class=1 AND major_id<0))
              THROW 51005, 'Unexpected public or diagnostic grant.', 1;
            """;
        await permissions.ExecuteNonQueryAsync(token);
    }
    await using var command = connection.CreateCommand();
    command.CommandTimeout = 30;
    command.CommandText = query;
    await using var reader = await command.ExecuteReaderAsync(token);
    using var buffer = new MemoryStream();
    using var writer = new Utf8JsonWriter(buffer);
    writer.WriteStartObject();
    writer.WriteString("CheckedUtc", DateTimeOffset.UtcNow);
    writer.WriteString("QuerySha256", hash);
    writer.WriteString("DatabaseUser", principal);
    writer.WriteStartArray("ResultSets");
    int rows = 0, sets = 0;
    do
    {
        if (reader.FieldCount == 0) continue;
        if (++sets > 100) throw new InvalidOperationException("Result-set limit exceeded; narrow the query.");
        writer.WriteStartObject();
        writer.WriteStartArray("Columns");
        for (int col = 0; col < reader.FieldCount; col++) writer.WriteStringValue(reader.GetName(col));
        writer.WriteEndArray();
        writer.WriteStartArray("Rows");
        while (await reader.ReadAsync(token))
        {
            if (++rows > maxRows) throw new InvalidOperationException("Row limit exceeded; narrow the query.");
            writer.WriteStartArray();
            for (int col = 0; col < reader.FieldCount; col++)
            {
                object value = reader.GetValue(col);
                // Avoid materializing unlimited LOBs; SqlClient still buffers a single field.
                if (value is string s && s.Length > maxBytes / 2 || value is byte[] b && b.Length > maxBytes)
                    throw new InvalidOperationException("Field limit exceeded; narrow the query.");
                JsonSerializer.Serialize(writer, value is DBNull ? null : value);
            }
            writer.WriteEndArray();
            if (buffer.Length + writer.BytesPending > maxBytes)
                throw new InvalidOperationException("Output limit exceeded; narrow the query.");
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    } while (await reader.NextResultAsync(token));
    writer.WriteEndArray();
    writer.WriteEndObject();
    writer.Flush();
    if (buffer.Length > maxBytes) throw new InvalidOperationException("Output limit exceeded; narrow the query.");
    CheckPath(mailbox);
    // CreateNew cannot overwrite an existing file or follow a pre-existing output hardlink.
    using (var output = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        buffer.WriteTo(output);
    Console.WriteLine(JsonSerializer.Serialize(new { Success = true, DatabaseUser = principal,
        QuerySha256 = hash, Rows = rows, ResultSets = sets, ResultFile = resultPath }));
    return 0;
}
catch (Exception error)
{
    // SQL messages can contain private values; return the error number, never the query/message.
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Success = false,
        SqlError = error is SqlException sql ? sql.Number : (int?)null,
        Error = error is SqlException ? "SQL check rejected or failed; no result published."
            : error is OperationCanceledException ? "Time limit exceeded; no result published."
            : error is InvalidOperationException ? error.Message : "Local helper failed; no result published." }));
    return 1;
}

static void CheckPath(string path)
{
    for (var current = new FileInfo(path).FullName; current is not null; current = Path.GetDirectoryName(current))
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Reparse points are not supported in the diagnostic mailbox path.");
}
