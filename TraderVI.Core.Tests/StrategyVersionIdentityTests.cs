using Core.Db;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class StrategyVersionIdentityTests
{
    [Fact]
    public void OfficialEvidenceIdentityRequiresVersionCodeAndDecision()
    {
        var identified = new StrategyVersionInfo
        {
            VersionId = Guid.NewGuid(),
            VersionName = "v3.1-rs-date-aligned",
            InitialCodeCommit = "c51c0849fd1311b3797cc664a19988e553bbe122",
            DecisionRef = "ADR-0041"
        };

        identified.HasOfficialEvidenceIdentity.ShouldBeTrue();
        new StrategyVersionInfo
        {
            VersionId = identified.VersionId,
            VersionName = identified.VersionName,
            DecisionRef = identified.DecisionRef
        }.HasOfficialEvidenceIdentity.ShouldBeFalse();
        new StrategyVersionInfo
        {
            VersionId = identified.VersionId,
            VersionName = identified.VersionName,
            InitialCodeCommit = identified.InitialCodeCommit,
            DecisionRef = " "
        }.HasOfficialEvidenceIdentity.ShouldBeFalse();
    }
}
