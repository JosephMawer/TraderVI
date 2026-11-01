#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class MarketCollectorSqlTests
{
    private static Task<string> ReadScript(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "CollectorSqlRehearsal", name));

    private static async Task CreateOriginalReceiptTableAsync(SqlConnection control)
    {
        var migration = await ReadScript("20261028_052_AddIndependentCollectorEvidence.sql");
        var ddl = Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Single(batch => batch.TrimStart().StartsWith("CREATE TABLE [dbo].[MarketCollectorRequest]", StringComparison.Ordinal));
        await control.ExecuteAsync(ddl);
    }

    private async Task RehearseReceiptMigrationAsync(SqlConnection control, string database)
    {
        Assert.Matches("^TraderVI_CollectorQualification_[a-f0-9]{32}$", database);
        Assert.Equal(database, control.Database);
        var fixture = """
DECLARE @open datetime2='2026-10-28T13:30:00', @close datetime2='2026-10-28T20:00:00', @owner uniqueidentifier=NEWID();
INSERT dbo.MarketCollectorBook(SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc) VALUES(@source,N'ABC',@open,@close);
DECLARE @i int=0;
WHILE @i<@rows
BEGIN
    SET @i+=1;
    DECLARE @id uniqueidentifier=NEWID(), @end datetime2=DATEADD(MINUTE,@i,@open);
    DECLARE @json nvarchar(max)=N'{"FailureCode":null,"memo":"café 🧪","series":['
        +REPLICATE(CONVERT(nvarchar(max),N'{"minute":1,"v":1234},'),1800)+N'{}],"n":'+CONVERT(nvarchar(10),@i)+N'}  ';
    INSERT dbo.MarketCollectorAttempt(RequestId,SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc,ExpectedEndUtc,ReservedUtc,OwnerId,FencingToken)
        VALUES(@id,@source,N'ABC',@open,@close,@end,DATEADD(SECOND,1,@end),@owner,1);
    INSERT dbo.MarketCollectorRequest(RequestId,SourceContract,Symbol,SessionOpenUtc,OwnerId,FencingToken,ContentHash,ContentJson)
        VALUES(@id,@source,N'ABC',@open,@owner,1,CASE WHEN @bad=1 AND @i=@rows THEN REPLICATE('0',64)
            ELSE CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),@json COLLATE Latin1_General_100_BIN2_UTF8)),2) END,@json);
END;
""";
        await control.ExecuteAsync(fixture, new { source = "Historical", rows = 65, bad = false });
        const string logical = "SELECT RequestId,ContentHash,ContentJson FROM dbo.MarketCollectorRequest ORDER BY Sequence;";
        var original = (await control.QueryAsync<(Guid Id,string Hash,string Json)>(logical)).ToArray();
        Assert.All(original, row => Assert.Equal(DelphiLiveCollectorBatches.Hash(row.Json), row.Hash));
        var dataFile = await control.QuerySingleAsync<string>("SELECT physical_name FROM sys.database_files WHERE file_id=1;");
        var backup = Path.Combine(Path.GetDirectoryName(dataFile)!, database + ".bak");
        await control.ExecuteAsync($"BACKUP DATABASE [{database}] TO DISK=@backup WITH COPY_ONLY,CHECKSUM;", new { backup });

        async Task Run(string file, int execute = 0, int limit = 1)
        {
            var script = await ReadScript(file);
            Assert.Contains("USE [TraderDB];", script);
            script = script.Replace(":ON ERROR EXIT", "").Replace("USE [TraderDB];", $"USE [{database}];")
                .Replace("$(VerifiedBackupFile)", backup.Replace("'", "''"))
                .Replace("$(ExecuteBatch)", execute.ToString()).Replace("$(MaxBatches)", limit.ToString());
            foreach (var part in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(part)) await control.ExecuteAsync(part);
        }

        await Run("20261101_056_PackCollectorReceipts.sql");
        await Run("20261101_057_AddRuleRequestedMinuteHistory.sql");
        const string plain = "SELECT COUNT(*) FROM dbo.MarketCollectorRequest WHERE PlainContentJson IS NOT NULL;";
        const string pack = "Pack-CollectorReceiptBatch.sql";
        await Run(pack);
        Assert.Equal(65, await control.QuerySingleAsync<int>(plain));
        await Run(pack, execute: 1);
        Assert.Equal(1, await control.QuerySingleAsync<int>(plain));
        Assert.Equal(original, (await control.QueryAsync<(Guid,string,string)>(logical)).ToArray());
        await Run(pack, execute: 1, limit: 2);
        await Run(pack, execute: 1);
        Assert.Equal(0, await control.QuerySingleAsync<int>(plain));
        Assert.Equal(original, (await control.QueryAsync<(Guid,string,string)>(logical)).ToArray());
        var sizes = await control.QuerySingleAsync<(long Plain,long Packed)>(
            "SELECT SUM(DATALENGTH(ContentJson)),SUM(DATALENGTH(PackedContent)) FROM dbo.MarketCollectorRequest;");
        Assert.True(sizes.Packed < sizes.Plain);
        output.WriteLine($"Synthetic conversion: {original.Length} verified receipts, {sizes.Plain} original bytes, {sizes.Packed} packed bytes.");
        await control.ExecuteAsync(fixture.Replace("ContentHash,ContentJson)", "ContentHash,PlainContentJson)"),
            new { source = "HashMismatch", rows = 2, bad = true });
        var rejected = await Assert.ThrowsAsync<SqlException>(() => Run(pack, execute: 1));
        Assert.Equal(54644, rejected.Number);
        Assert.Equal(2, await control.QuerySingleAsync<int>(plain));
        Assert.Equal(0, await control.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
        await Assert.ThrowsAsync<SqlException>(() => control.ExecuteAsync("UPDATE dbo.MarketCollectorRequest SET ContentHash=ContentHash;"));
    }
}
