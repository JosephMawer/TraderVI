#nullable enable

using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveDatabaseContractTests
{
    private const string MigrationFile =
        "20260905_022_AddDelphiLiveIdentityAndCollectionFoundation.sql";
    private const string PolicyVersionId =
        "C15C1A27-13A1-581A-8912-06C92941A01E";
    private const string SettingsSha256 =
        "A1944AC94212353A43D8291D1A6B9E3ACAB992F77E69FCFE559A814AEE2FDA99";

    private static readonly string[] TableFiles =
    {
        "DelphiLivePolicyVersion.sql",
        "DelphiLivePolicyAssignment.sql",
        "DelphiLiveHostLease.sql",
        "DelphiLiveSession.sql",
        "DelphiLiveSessionPolicy.sql",
        "DelphiLiveContinuityEpoch.sql",
        "DelphiLiveSessionSymbol.sql",
        "DelphiLiveFrozenCandidate.sql",
        "DelphiLiveFrozenCandidateLens.sql",
        "DelphiLiveDailyBaseline.sql",
        "IntradayCollectionCycle.sql",
        "IntradayCollectionSlot.sql",
        "IntradayEvidenceConflict.sql",
        "IntradayCollectionReceipt.sql"
    };

    [Fact]
    public void PolicySeed_IsTheExactInactiveV1IdentityAndUtf8SettingsDocument()
    {
        string table = ReadCanonicalTable("DelphiLivePolicyVersion.sql");
        string migration = ReadMigrationRaw();

        table.ShouldContain("[DelphiLivePolicyVersionId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        table.ShouldContain("[SettingsEncoding] = N'UTF-8'", Case.Insensitive);
        table.ShouldContain("[InitialActivationState] = N'Inactive'", Case.Insensitive);
        table.ShouldContain("ISJSON([SettingsJson]) = 1", Case.Insensitive);

        migration.ShouldContain(PolicyVersionId, Case.Insensitive);
        foreach (string identity in new[]
        {
            "DelphiLivePolicyV1",
            "DelphiLiveEvaluatorV1",
            "IntradayEvidenceCollectorV3",
            "DelphiLiveDecisionDossierV1",
            "DelphiLiveQuoteFillV1",
            "DelphiLiveShadowPortfolioV1",
            "LiveObservationOutcomeV1",
            "DelphiLiveDailyVsLiveTop5V1",
            "DelphiLivePromotionV1"
        })
        {
            migration.ShouldContain(identity, Case.Insensitive);
        }

        Match jsonMatch = Regex.Match(
            migration,
            @"DECLARE\s+@SettingsJson\s+NVARCHAR\(MAX\)\s*=\s*N'(?<json>\{[^\r\n]+\})';",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        jsonMatch.Success.ShouldBeTrue();
        string json = jsonMatch.Groups["json"].Value;

        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ShouldBe(SettingsSha256);
        migration.ShouldContain($"0x{SettingsSha256}", Case.Insensitive);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        root.EnumerateObject().Count().ShouldBe(48);
        root.GetProperty("marketTimeZone").GetString().ShouldBe("America/Toronto");
        root.GetProperty("barInterval").GetString().ShouldBe("00:05:00");
        root.GetProperty("collectionOffset").GetString().ShouldBe("00:02:00");
        root.GetProperty("volatilityRulers").GetProperty("operationalSessions").GetInt32().ShouldBe(10);
        root.GetProperty("volatilityRulers").GetProperty("challengerSessions").GetInt32().ShouldBe(14);
        root.GetProperty("rawMoveThresholds").GetProperty("operational").GetDecimal().ShouldBe(0.25m);
        root.GetProperty("excessMoveThresholds").GetProperty("operational").GetDecimal().ShouldBe(0.05m);
        root.GetProperty("directionalVolumeThreshold").GetDecimal().ShouldBe(0.10m);
        root.GetProperty("selectedRawMoveThreshold").GetDecimal().ShouldBe(0.25m);
        root.GetProperty("selectedExcessMoveThreshold").GetDecimal().ShouldBe(0.05m);
        root.GetProperty("selectedRulerSessions").GetInt32().ShouldBe(10);
        root.GetProperty("entryWindowStart").GetString().ShouldBe("09:50:00");
        root.GetProperty("entryCutoff").GetString().ShouldBe("15:45:00");
        root.GetProperty("promotionBootstrapResampleCount").GetInt32().ShouldBe(10_000);
        root.GetProperty("primaryExitReasonOrder").EnumerateArray().Select(x => x.GetString()).ShouldBe(
            new[]
            {
                "HardLoss5Pct",
                "FastDownside10Pct",
                "ProfitProtectionFloorBreach",
                "ConfirmedSupportFailure",
                "LiveWeakeningExit"
            });
    }

    [Fact]
    public void FrozenSessionSchema_PreservesDailyAndLiveIdentityWithExactCalibrationSources()
    {
        string session = ReadCanonicalTable("DelphiLiveSession.sql");
        string policy = ReadCanonicalTable("DelphiLiveSessionPolicy.sql");
        string symbol = ReadCanonicalTable("DelphiLiveSessionSymbol.sql");
        string candidate = ReadCanonicalTable("DelphiLiveFrozenCandidate.sql");
        string lens = ReadCanonicalTable("DelphiLiveFrozenCandidateLens.sql");
        string baseline = ReadCanonicalTable("DelphiLiveDailyBaseline.sql");

        session.ShouldContain("[CalibrationRunPurpose] = N'OfficialPaper'", Case.Insensitive);
        session.ShouldContain("[CalibrationRunAuditState] = N'Valid'", Case.Insensitive);
        session.ShouldContain("[CalibrationRunCreatedUtc] <= [FreezeBoundaryUtc]", Case.Insensitive);
        session.ShouldContain("[CalibrationMarketDataAsOf] = [ExpectedPriorCanonicalSessionDate]", Case.Insensitive);
        session.ShouldContain("[DailyStrategyVersionId] UNIQUEIDENTIFIER NULL", Case.Insensitive);
        policy.ShouldContain("[DelphiLivePolicyVersionId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        policy.ShouldContain("[DailyStrategyVersionId] UNIQUEIDENTIFIER NULL", Case.Insensitive);
        policy.ShouldContain("[FK_DelphiLiveSessionPolicy_Session]", Case.Insensitive);
        policy.ShouldContain("[FK_DelphiLiveSessionPolicy_SessionStrategy]", Case.Insensitive);
        policy.ShouldContain("[FK_DelphiLiveSessionPolicy_PolicySettings]", Case.Insensitive);
        policy.ShouldContain("UNIQUE ([SessionId], [RoleSlot])", Case.Insensitive);

        symbol.ShouldContain("UNIQUE ([SessionId], [Symbol])", Case.Insensitive);
        symbol.ShouldContain("[FrozenSourceLensCount] IN (1, 2)", Case.Insensitive);
        symbol.ShouldContain("[BestFrozenSourceLensRank] BETWEEN 1 AND 25", Case.Insensitive);
        symbol.ShouldContain("[HasPendingProtectiveSell]", Case.Insensitive);

        candidate.ShouldContain("[CalibrationCandidateId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[CalibrationRunId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[DailyStrategyVersionId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[CommonCompositeScore] FLOAT NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[CandidateSnapshotJson] NVARCHAR(MAX) NOT NULL", Case.Insensitive);
        candidate.ShouldContain("[FK_DelphiLiveFrozenCandidate_ObservationDate]", Case.Insensitive);

        lens.ShouldContain("[CalibrationLensEvaluationId] UNIQUEIDENTIFIER NOT NULL", Case.Insensitive);
        lens.ShouldContain("REFERENCES [dbo].[CalibrationLensEvaluation] ([LensEvaluationId])", Case.Insensitive);
        lens.ShouldContain("REFERENCES [dbo].[CalibrationLensEvaluation] ([CandidateId], [Lens])", Case.Insensitive);
        lens.ShouldContain("[Lens] IN (N'Continuation', N'Breakout')", Case.Insensitive);
        lens.ShouldContain("[IsPublished] = 1", Case.Insensitive);
        lens.ShouldContain("[FrozenRank] BETWEEN 1 AND 25", Case.Insensitive);
        lens.ShouldContain("UNIQUE ([FrozenCandidateId], [Lens])", Case.Insensitive);

        baseline.ShouldContain("[MedianTrueRangePct5]", Case.Insensitive);
        baseline.ShouldContain("[MedianTrueRangePct10]", Case.Insensitive);
        baseline.ShouldContain("[MedianTrueRangePct14]", Case.Insensitive);
        baseline.ShouldContain("[MedianTrueRangePct20]", Case.Insensitive);
        baseline.ShouldContain("[MedianFullDayVolume20]", Case.Insensitive);
        baseline.ShouldContain("[AlignedDailyBarCount] = 21", Case.Insensitive);
        baseline.ShouldContain("[FK_DelphiLiveDailyBaseline_SourceThroughDate]", Case.Insensitive);
    }

    [Fact]
    public void AssignmentLeaseAndContinuity_KeepActivationExplicitAndHostGapsAuditable()
    {
        string assignment = ReadCanonicalTable("DelphiLivePolicyAssignment.sql");
        string lease = ReadCanonicalTable("DelphiLiveHostLease.sql");
        string continuity = ReadCanonicalTable("DelphiLiveContinuityEpoch.sql");

        assignment.ShouldContain("[PolicyRole] = N'OperationalChampion' AND [RoleSlot] = 0", Case.Insensitive);
        assignment.ShouldContain("[RoleSlot] IN (1, 2)", Case.Insensitive);
        assignment.ShouldContain("[PolicyRole] = N'ResearchCounterfactual' AND [RoleSlot] >= 100", Case.Insensitive);
        assignment.ShouldContain("[EffectiveTradingDate]", Case.Insensitive);
        assignment.ShouldContain("[AuthorizedBy]", Case.Insensitive);
        assignment.ShouldContain("[DecisionRef]", Case.Insensitive);
        assignment.ShouldContain("ON [dbo].[DelphiLivePolicyAssignment] ([RoleSlot])", Case.Insensitive);

        lease.ShouldContain("[LeaseName] = N'DelphiLiveMonitor'", Case.Insensitive);
        lease.ShouldContain("[FencingToken] BIGINT NOT NULL", Case.Insensitive);
        lease.ShouldContain("[SourceContractVersion] INT NOT NULL", Case.Insensitive);
        lease.ShouldContain("[WorkingTreeState] IN (N'Clean', N'Dirty', N'Unknown')", Case.Insensitive);
        lease.ShouldContain("[RowVersion] ROWVERSION NOT NULL", Case.Insensitive);
        lease.ShouldContain("WHERE [IsHeld] = 1", Case.Insensitive);

        continuity.ShouldContain("[EpochNumber] > 1 AND [PreviousContinuityEpochId] IS NOT NULL", Case.Insensitive);
        continuity.ShouldContain("FOREIGN KEY ([PreviousContinuityEpochId], [SessionId])", Case.Insensitive);
        continuity.ShouldContain("[LeaseFencingToken]", Case.Insensitive);
        continuity.ShouldContain("[RestartDispositionJson]", Case.Insensitive);
        continuity.ShouldContain("[HostGapObserved]", Case.Insensitive);
        continuity.ShouldContain("WHERE [EndedUtc] IS NULL", Case.Insensitive);
    }

    [Fact]
    public void CollectionSchema_PersistsEveryExpectedSlotAndNeverRepairsOperationalLateness()
    {
        string cycle = ReadCanonicalTable("IntradayCollectionCycle.sql");
        string slot = ReadCanonicalTable("IntradayCollectionSlot.sql");
        string conflict = ReadCanonicalTable("IntradayEvidenceConflict.sql");
        string workflow = Compact(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "Core", "Trader", "DelphiLive", "DelphiLiveCollectionWorkflow.cs")));

        cycle.ShouldContain("[CollectorVersion] = N'IntradayEvidenceCollectorV3'", Case.Insensitive);
        cycle.ShouldContain("[SourceContractVersion] = 1", Case.Insensitive);
        cycle.ShouldContain("[ScheduledStartUtc] = DATEADD(MINUTE, 2, [BarEndUtc])", Case.Insensitive);
        cycle.ShouldContain("[DeadlineUtc] = DATEADD(MINUTE, [IntervalMinutes], [ScheduledStartUtc])", Case.Insensitive);
        cycle.ShouldContain("WHERE [CycleStatus] = N'Collecting'", Case.Insensitive);

        slot.ShouldContain("FOREIGN KEY ([SessionSymbolId], [SessionId], [Symbol])", Case.Insensitive);
        slot.ShouldContain("[ExpectedBarStartUtc]", Case.Insensitive);
        slot.ShouldContain("[ExpectedBarEndUtc]", Case.Insensitive);
        slot.ShouldContain("[DeadlineUtc]", Case.Insensitive);
        slot.ShouldContain("[RequiredByJson]", Case.Insensitive);
        slot.ShouldContain("[PollObservationId]", Case.Insensitive);
        slot.ShouldContain("[EvidenceBarId]", Case.Insensitive);
        slot.ShouldContain("[ReceivedUtc] < [DeadlineUtc]", Case.Insensitive);
        slot.ShouldContain("[ReceivedUtc] >= [DeadlineUtc]", Case.Insensitive);
        slot.ShouldContain("[MissedOperationalDeadline] = 1", Case.Insensitive);

        foreach (string disposition in new[]
        {
            "OperationalOnTime",
            "LateResearchOnly",
            "NoCompletedBar",
            "StaleNoNewBar",
            "FormingBarIgnored",
            "StructurallyInvalid",
            "CycleDeadlineExceeded",
            "CollectionFailed"
        })
        {
            workflow.ShouldContain($"\"{disposition}\"", Case.Insensitive);
            slot.ShouldContain($"N'{disposition}'", Case.Insensitive);
        }

        slot.ShouldContain("N'IdenticalDuplicate'", Case.Insensitive);
        slot.ShouldContain("N'ConflictingDuplicate'", Case.Insensitive);
        conflict.ShouldContain("[ExistingEvidenceBarId]", Case.Insensitive);
        conflict.ShouldContain("[IncomingPayloadSha256]", Case.Insensitive);
        conflict.ShouldContain("[IncomingEventUtc] = [ExistingBarEventUtc]", Case.Insensitive);
        conflict.ShouldContain("[ResolutionDisposition] = N'Unresolved'", Case.Insensitive);
        conflict.ShouldContain("N'CanonicalRetained'", Case.Insensitive);
    }

    [Fact]
    public void Migration022_IsGuardedTransactionalTrackedAndSeedsNoOperationalState()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "TraderDB", "TraderDB.sqlproj"));
        string migration = Compact(ReadMigrationRaw());

        foreach (string tableFile in TableFiles)
        {
            project.ShouldContain($"Build Include=\"dbo/Tables/{tableFile}\"", Case.Insensitive);
            migration.ShouldContain($":r TraderDB\\dbo\\Tables\\{tableFile}", Case.Insensitive);
        }
        project.ShouldContain($"None Include=\"Migrations/{MigrationFile}\"", Case.Insensitive);

        migration.ShouldContain(":ON ERROR EXIT", Case.Insensitive);
        migration.ShouldContain("SET XACT_ABORT ON", Case.Insensitive);
        migration.ShouldContain("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", Case.Insensitive);
        migration.ShouldContain("fresh verified backup", Case.Insensitive);
        migration.ShouldContain("explicit authorization", Case.Insensitive);
        migration.ShouldContain("BEGIN TRANSACTION", Case.Insensitive);
        migration.ShouldContain("COMMIT TRANSACTION", Case.Insensitive);
        migration.ShouldContain("review the partial or prior installation", Case.Insensitive);
        migration.ShouldContain("[InitialActivationState]", Case.Insensitive);
        migration.ShouldContain("N'Inactive'", Case.Insensitive);

        Regex.Matches(
            migration,
            @"INSERT\s+INTO\s+\[dbo\]\.\[",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count.ShouldBe(1);
        migration.ShouldContain("INSERT INTO [dbo].[DelphiLivePolicyVersion]", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO [dbo].[DelphiLivePolicyAssignment]", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO [dbo].[DelphiLiveHostLease]", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO [dbo].[DelphiLiveSession]", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO [dbo].[IntradayCollectionCycle]", Case.Insensitive);
        migration.ShouldNotContain("INSERT INTO [dbo].[IntradayCollectionSlot]", Case.Insensitive);
        migration.ShouldNotContain("UPDATE [dbo]", Case.Insensitive);
        migration.ShouldNotContain("DELETE FROM [dbo]", Case.Insensitive);
    }

    private static string ReadMigrationRaw() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "TraderDB", "Migrations", MigrationFile));

    private static string ReadCanonicalTable(string fileName) => Compact(File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "TraderDB", "dbo", "Tables", fileName)));

    private static string Compact(string value) => Regex.Replace(value, @"\s+", " ").Trim();

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
