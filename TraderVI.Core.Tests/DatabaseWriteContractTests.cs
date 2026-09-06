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
    public void ShadowV1Schema_PreservesFrozenEvidenceAndCausalFillContracts()
    {
        string portfolio = ReadCanonicalTable("ShadowPortfolio.sql");
        string session = ReadCanonicalTable("ShadowPortfolioSession.sql");
        string candidate = ReadCanonicalTable("ShadowPortfolioCandidate.sql");
        string position = ReadCanonicalTable("ShadowPosition.sql");
        string order = ReadCanonicalTable("ShadowOrder.sql");

        portfolio.ShouldContain("[SelectionActor] = N'System' AND [ExecutionMode] = N'Ghost'", Case.Insensitive);
        portfolio.ShouldContain("[MaximumPositions] IN (3, 5)", Case.Insensitive);
        session.ShouldContain("[CalibrationRunId] UNIQUEIDENTIFIER NULL", Case.Insensitive);
        session.ShouldContain("[ActivationBaselineUtc] DATETIME2 NULL", Case.Insensitive);
        candidate.ShouldContain("[CalibrationCandidateId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[Rank] BETWEEN 1 AND 5", Case.Insensitive);
        position.ShouldContain("[LastFifteenMinuteBarUtc] DATETIME2 NULL", Case.Insensitive);
        order.ShouldContain("[EarliestFillUtc] > [SignalReceivedUtc]", Case.Insensitive);
        order.ShouldContain("[Status] IN (N'Pending', N'Filled', N'Expired', N'Cancelled')", Case.Insensitive);
    }

    [Fact]
    public void Migration019_IsTrackedAsManualSchemaOnlyAndLeavesShadowOff()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "TraderDB", "TraderDB.sqlproj"));
        string migration = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderDB",
            "Migrations",
            "20260904_019_AddSystemShadowPortfolioLedger.sql")), @"\s+", " ");

        project.ShouldContain("Migrations/20260904_019_AddSystemShadowPortfolioLedger.sql");
        migration.ShouldContain("fresh verified backup", Case.Insensitive);
        migration.ShouldContain("Shadow remains off by default", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO", Case.Insensitive);
        migration.ShouldNotContain("UPDATE ", Case.Insensitive);
        migration.ShouldNotContain("DELETE FROM", Case.Insensitive);
    }

    [Fact]
    public void Migration020_IsExactGuardedAndRefusesToDeleteShadowTrades()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "TraderDB", "TraderDB.sqlproj"));
        string migration = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderDB",
            "Migrations",
            "20260904_020_ReclassifyDirtyOfficialRunAndResetEmptyShadow.sql")), @"\s+", " ");

        project.ShouldContain("Migrations/20260904_020_ReclassifyDirtyOfficialRunAndResetEmptyShadow.sql");
        migration.ShouldContain("fresh verified backup", Case.Insensitive);
        migration.ShouldContain("463416D8-8229-4C51-B255-107F96DF21D4", Case.Insensitive);
        migration.ShouldContain("246C3C70-37B8-4CB7-AD24-F044FAF16477", Case.Insensitive);
        migration.ShouldContain("WorkingTreeState = N'Dirty'", Case.Insensitive);
        migration.ShouldContain("SET AuditState = N'Valid'", Case.Insensitive);
        migration.ShouldContain("IF EXISTS ( SELECT 1 FROM dbo.ShadowPosition", Case.Insensitive);
        migration.ShouldContain("IF EXISTS ( SELECT 1 FROM dbo.ShadowOrder", Case.Insensitive);
        migration.ShouldContain("BEGIN TRANSACTION", Case.Insensitive);
        migration.ShouldContain("ROLLBACK TRANSACTION", Case.Insensitive);
    }

    [Fact]
    public void Migration021_AddsOnlyTheShadowBarIdentityNeededForIdempotentTrailingState()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "TraderDB", "TraderDB.sqlproj"));
        string migration = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderDB",
            "Migrations",
            "20260904_021_HardenSystemShadowExecutionCausality.sql")), @"\s+", " ");

        project.ShouldContain("Migrations/20260904_021_HardenSystemShadowExecutionCausality.sql");
        migration.ShouldContain("fresh verified backup", Case.Insensitive);
        migration.ShouldContain("ADD [LastFifteenMinuteBarUtc] DATETIME2 NULL", Case.Insensitive);
        migration.ShouldNotContain("DELETE FROM", Case.Insensitive);
        migration.ShouldNotContain("UPDATE ", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO", Case.Insensitive);
    }

    [Fact]
    public void ShadowRepository_CancelsPendingBuysWheneverNewRiskIsSuspended()
    {
        string repository = Regex.Replace(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Core",
            "Db",
            "SystemShadowRepository.cs")), @"\s+", " ");

        repository.ShouldContain("IF @Status = N'Paused'", Case.Insensitive);
        repository.ShouldContain("o.[ReasonCode] = N'OperatorPaused'", Case.Insensitive);
        repository.ShouldContain("IF @DailyLossGuard = 1 OR @CapitalReviewRequired = 1", Case.Insensitive);
        repository.ShouldContain("[Status] = N'Pending' AND [Side] = N'Buy'", Case.Insensitive);
        repository.ShouldContain("IF @Side = N'Sell'", Case.Insensitive);
        repository.ShouldContain("[ReasonCode] = N'SupersededByProtectiveSell'", Case.Insensitive);
        repository.ShouldContain("bool buyingSuspended", Case.Insensitive);
        repository.ShouldContain("\"NewRiskSuspended\"", Case.Insensitive);
        repository.ShouldContain("[HighestClosingValue] = @ReviewedValue", Case.Insensitive);
    }

    [Fact]
    public void ShadowRepository_ProjectsNullableSqlBitAsBitBeforeBooleanRead()
    {
        string repository = Regex.Replace(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Core",
            "Db",
            "SystemShadowRepository.cs")), @"\s+", " ");

        repository.ShouldContain(
            "CAST(COALESCE(s.[DailyLossGuardActive],0) AS bit)",
            Case.Insensitive);
        repository.ShouldContain("reader.GetBoolean(3)", Case.Insensitive);
    }

    [Fact]
    public void ShadowCandidateMonitor_UsesCurrentSessionEvidenceAndRefreshesTheSelectedPortfolio()
    {
        string root = FindRepositoryRoot();
        string repository = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "Core",
            "Db",
            "SystemShadowRepository.cs")), @"\s+", " ");
        string viewModel = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderVI.WPF",
            "Viewmodels",
            "PortfoliosViewModel.cs")), @"\s+", " ");
        string view = Regex.Replace(File.ReadAllText(Path.Combine(
            root,
            "TraderVI.WPF",
            "Views",
            "PortfoliosView.xaml")), @"\s+", " ");

        repository.ShouldContain("GetCandidateMonitorAsync", Case.Insensitive);
        repository.ShouldContain("AT TIME ZONE 'Eastern Standard Time'", Case.Insensitive);
        repository.ShouldContain("candidate.[LastEvaluatedUtc]", Case.Insensitive);
        repository.ShouldContain("DATEADD(MINUTE,5,bar.[EventUtc]) <= candidate.[LastEvaluatedUtc]", Case.Insensitive);
        repository.ShouldContain("bar.[EventUtc] < latestBar.[EventUtc]", Case.Insensitive);
        viewModel.ShouldContain("await RefreshDetailsAsync(cancellationToken)", Case.Insensitive);
        viewModel.ShouldContain("LatestCandidateEvaluationUtc", Case.Insensitive);
        view.ShouldContain("ItemsSource=\"{Binding Candidates}\"", Case.Insensitive);
        view.ShouldContain("Header=\"vs yesterday\"", Case.Insensitive);
        view.ShouldContain("Header=\"Evaluated\"", Case.Insensitive);
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
