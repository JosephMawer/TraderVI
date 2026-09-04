#nullable enable

using Core.Db;
using Shouldly;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DatabaseWriteContractTests
{
    [Fact]
    public async Task SymbolWriters_RejectInvalidKeysBeforeDatabaseAccess()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            new QuoteRepository().InsertSymbol(" ", "name", "TSX"));
        await Should.ThrowAsync<ArgumentException>(() =>
            new SymbolsRepository().AddSymbol("name", " "));
    }

    [Fact]
    public void DailyBarsSchema_SupportsRepositoryNaturalKeyUpsert()
    {
        string sql = ReadCanonicalTable("DailyBars.sql");

        sql.ShouldContain("[Id] INT IDENTITY (1, 1) NOT NULL", Case.Insensitive);
        sql.ShouldContain(
            "CONSTRAINT [UQ_DailyBars_Symbol_Date] UNIQUE ([Symbol], [Date])",
            Case.Insensitive);
    }

    [Fact]
    public void SymbolAndQuoteSchemas_SupplyWriterOmittedColumns()
    {
        string symbols = ReadCanonicalTable("Symbols.sql");
        string quotes = ReadCanonicalTable("Quotes.sql");

        symbols.ShouldContain("[IsActive] BIT NOT NULL CONSTRAINT [DF_Symbols_IsActive] DEFAULT ((1))", Case.Insensitive);
        symbols.ShouldContain("[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_Symbols_CreatedUtc] DEFAULT (SYSUTCDATETIME())", Case.Insensitive);
        symbols.ShouldContain("[SecurityType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Symbols_SecurityType] DEFAULT (N'Stock')", Case.Insensitive);
        quotes.ShouldContain("[IngestedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_Quotes_IngestedUtc] DEFAULT (SYSUTCDATETIME())", Case.Insensitive);
        quotes.ShouldContain("CONSTRAINT [FK_Quotes_Symbols] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol])", Case.Insensitive);
    }

    [Fact]
    public void OperationalSchemas_PreserveRepositoryRelationships()
    {
        string activePosition = ReadCanonicalTable("ActivePosition.sql");
        string dailyPick = ReadCanonicalTable("DailyPick.sql");
        string tradeLog = ReadCanonicalTable("TradeLog.sql");

        activePosition.ShouldContain("CONSTRAINT [FK_ActivePosition_Pick] FOREIGN KEY ([OriginalPickId]) REFERENCES [dbo].[DailyPick] ([PickId])", Case.Insensitive);
        dailyPick.ShouldContain("CONSTRAINT [FK_DailyPick_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId])", Case.Insensitive);
        tradeLog.ShouldContain("CONSTRAINT [FK_TradeLog_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ActivePosition] ([PositionId])", Case.Insensitive);
        tradeLog.ShouldContain("CONSTRAINT [FK_TradeLog_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId])", Case.Insensitive);
        tradeLog.ShouldContain("CONSTRAINT [CK_TradeLog_TradeType] CHECK ([TradeType] IN (N'BUY', N'SELL'))", Case.Insensitive);
    }

    [Fact]
    public void Migration018_IsTrackedAsManualAndDoesNotModifyRows()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "TraderDB", "TraderDB.sqlproj"));
        string migration = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderDB",
            "Migrations",
            "20260903_018_ReconcileCanonicalWriteContracts.sql")), @"\s+", " ");

        project.ShouldContain("Migrations/20260903_018_ReconcileCanonicalWriteContracts.sql");
        migration.ShouldContain("explicit authorization", Case.Insensitive);
        migration.ShouldNotContain("DELETE FROM", Case.Insensitive);
        migration.ShouldNotContain("UPDATE ", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO", Case.Insensitive);
    }

    [Fact]
    public void DatabaseCiJob_BuildsSchemaWithoutDeploymentAuthority()
    {
        string workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "dotnet-ci.yml"));
        int jobStart = workflow.IndexOf("  database-project-build:", StringComparison.Ordinal);

        jobStart.ShouldBeGreaterThanOrEqualTo(0);
        string databaseJob = workflow[jobStart..];

        databaseJob.ShouldContain("runs-on: windows-2025-vs2026", Case.Insensitive);
        databaseJob.ShouldContain("/target:Build", Case.Insensitive);
        databaseJob.ShouldContain("/property:DeployOnBuild=False", Case.Insensitive);
        databaseJob.ShouldContain("Microsoft.Data.Tools.Schema.SqlTasks.targets", Case.Insensitive);
        databaseJob.ShouldNotContain("/target:Deploy", Case.Insensitive);
        databaseJob.ShouldNotContain("sqlcmd", Case.Insensitive);
        databaseJob.ShouldNotContain("sqlpackage", Case.Insensitive);
        databaseJob.ShouldNotContain("secrets.", Case.Insensitive);
        databaseJob.ShouldNotContain("actions/upload-artifact", Case.Insensitive);
    }

    private static string ReadCanonicalTable(string fileName)
    {
        string text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "TraderDB",
            "dbo",
            "Tables",
            fileName));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TraderVI.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TraderVI repository root.");
    }
}
