#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveResearchPersistenceTests
{
    [Theory]
    [InlineData("abc", false)]
    [InlineData("XIU", false)]
    [InlineData("ABC", true)]
    public async Task ExpectedSlotsRejectNonCanonicalIdentitiesBeforeOpeningSql(string symbol, bool benchmark)
    {
        DateTime endpoint = new(2026, 9, 8, 13, 35, 0, DateTimeKind.Utc);
        Guid session = Guid.NewGuid();
        var slot = new DelphiLiveExpectedResearchSlot(
            DelphiLiveResearchCoordinator.StableId($"slot/{session:D}/{symbol}/{endpoint:O}"),
            session, new(2026, 9, 8), endpoint, symbol, benchmark, null, "MissingScheduledSlot", false);
        var lease = new DelphiLiveLease(Guid.NewGuid(), "pure-validation", 1, endpoint, endpoint.AddMinutes(15));
        await Should.ThrowAsync<ArgumentException>(() => new DelphiLiveExperimentRepository().RecordExpectedSlotsAsync([slot], lease));
    }

    [Fact]
    public void ExpectedSlotsRequireFrozenCalendarMembershipAndDurablySettledOperationalFacts()
    {
        string sql = ReadSource("DelphiLiveExperimentRepository.cs");
        sql.ShouldContain("s.TradingDate=@Date AND m.Symbol=@Symbol AND m.IsXiuBenchmark=@Benchmark");
        sql.ShouldContain("@End BETWEEN m.RequiredFromBarEndUtc AND m.RequiredThroughBarEndUtc");
        sql.ShouldContain("DATEDIFF(MINUTE,s.SessionOpenUtc,@End)%5=0");
        sql.ShouldContain("c.CycleStatus NOT IN(N'Planned',N'Collecting')");
        sql.ShouldContain("@Disposition=sl.Disposition AND @Operational=sl.OperationallyUsable");
        sql.ShouldContain("SYSUTCDATETIME()<DATEADD(MINUTE,7,@End)");
        sql.ShouldContain("Expected research slots cannot be replaced or operationally repaired by later data.");
    }

    [Fact]
    public void ResearchSourceRetainsCanonicalProvenanceAndCausalConflictEvidence()
    {
        string sql = ReadSource("DelphiLiveResearchEvidenceRepository.cs");
        sql.ShouldContain("p.Provider=N'TMXMoney' AND p.SourceContractVersion=N'TmxChartIntradayNoFreqV1'");
        sql.ShouldContain("p.EvidenceSchemaVersion=1 AND p.CollectorVersion IN(N'IntradayEvidenceCollectorV2',N'IntradayEvidenceCollectorV3')");
        sql.ShouldContain("x.ExistingEvidenceBarId=b.EvidenceBarId");
        sql.ShouldContain("x.ReceivedUtc<=@AsOf AND x.CreatedUtc<=@AsOf");
        sql.ShouldContain("HasConflictingEvidence = conflicts.Count > 0");
        sql.ShouldContain("ConflictingAnchors = conflicts");
        sql.ShouldContain("b.CreatedUtc<=@AsOf AND p.CreatedUtc<=@AsOf");
    }

    [Fact]
    public void ChangedSessionSelectionUsesCompletedReviewsAndDetectsOlderLateEvidence()
    {
        string sql = ReadSource("DelphiLiveResearchEvidenceRepository.cs");
        sql.ShouldContain("MAX(r.ReviewedUtc)");
        sql.ShouldContain("dbo.DelphiLiveResearchSessionReview");
        sql.ShouldContain("review.ReviewedUtc<=h.ThirdCloseUtc");
        sql.ShouldContain("review.ReviewedUtc<=h.FifthCloseUtc");
        sql.ShouldContain("b.CreatedAt>review.ReviewedUtc");
        sql.ShouldContain("x.CreatedUtc>review.ReviewedUtc");
        sql.ShouldContain("d.RecordedUtc>review.ReviewedUtc");
        sql.ShouldNotContain("DATEADD(DAY,-8"); // Older cohorts with new audits must remain discoverable.
    }

    private static string ReadSource(string name)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "Core", "Db", name));
    }
}
