using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PaperGateComparisonTests
{
    private static readonly DateTime Start = new(2026, 9, 9, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, false)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, false)]
    [InlineData(26, 25, false)]
    public void BypassPreservesPublishedRankBoundaries(int rank, int maximum, bool expected)
    {
        Assert.Equal(expected, PaperGateComparison.Admits(PaperGateProfile.PortfolioRulesOnlyV1, true, rank, false, maximum));
        Assert.False(PaperGateComparison.Admits(PaperGateProfile.NormalV1, true, rank, false, maximum));
        Assert.False(PaperGateComparison.Admits(PaperGateProfile.PortfolioRulesOnlyV1, false, rank, true, maximum));
    }

    [Fact]
    public void LiveUnionRetainsOriginalFailedFacts_AndNormalOnlyUsesPassingLenses()
    {
        Guid candidateId = Guid.NewGuid();
        var failed = new DelphiLiveLensSource(Guid.NewGuid(), candidateId, "Continuation", false, true, 1, 7m, "Breadth", "{\"Breadth\":false}");
        var passing = failed with { LensEvaluationId = Guid.NewGuid(), Lens = "Breakout", Eligible = true, Rank = 25, FirstFailure = null };
        var candidate = new DelphiLiveFrozenCandidate(candidateId, "AAA", .8m, "{}", [failed, passing]);
        var source = Source(candidate);
        var normal = PaperGateComparison.SelectLive(source, PaperGateProfile.NormalV1);
        var experimental = PaperGateComparison.SelectLive(source, PaperGateProfile.PortfolioRulesOnlyV1);
        Assert.Single(normal); Assert.Single(experimental);
        Assert.Equal("Breakout", Assert.Single(normal["AAA"].SourceLenses).Lens);
        Assert.Equal(failed, experimental["AAA"].SourceLenses[0]);
        Assert.False(source.Candidates.Single().SourceLenses[0].Eligible);
    }

    [Fact]
    public void AccountProfileCannotChangeDuringLedgerTransition()
    {
        var account = Account(PaperGateProfile.NormalV1);
        var request = new DelphiLiveGenerationRequest(Guid.NewGuid(), account.AccountId, account.LivePolicyVersionId!.Value,
            PaperGateComparison.LiveRole, null, 1000, "CAD", new(2026, 9, 9), Start, Start.AddMinutes(-1), "test", "Comparison")
            { ComparisonAccount = account };
        var state = DelphiLiveLedgerIntegrity.Create(request);
        var changed = state with { Revision = 1, UpdatedUtc = Start, ComparisonAccount = account with { Profile = PaperGateProfile.PortfolioRulesOnlyV1 } };
        Assert.Throws<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(state, changed));
        Assert.Throws<ArgumentException>(() => DelphiLiveLedgerIntegrity.Create(request with { StartingCapital = 500 }));
        Assert.Throws<ArgumentException>(() => DelphiLiveLedgerIntegrity.Create(request with { ComparisonAccount = null }));
    }

    [Fact]
    public void AdmissionRequiresOwnAccountPolicyAndProspectiveSource()
    {
        var account = Account(PaperGateProfile.PortfolioRulesOnlyV1);
        var source = Source() with { BatchId = account.BatchId, DailyStrategyVersionId = account.DailyStrategyVersionId };
        var admission = PaperGateComparison.Admission(account, source, account.LivePolicyVersionId!.Value);
        admission.Validate(source.RunId, account.DailyStrategyVersionId, account.LivePolicyVersionId.Value, Start);
        Assert.Throws<ArgumentException>(() => admission.Validate(source.RunId, account.DailyStrategyVersionId, account.LivePolicyVersionId.Value, Start.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => PaperGateComparison.Admission(account, source with { BatchId = Guid.NewGuid() }, account.LivePolicyVersionId.Value));
        Assert.Throws<ArgumentException>(() => admission.Validate(source.RunId, account.DailyStrategyVersionId, Guid.NewGuid(), Start));
    }

    private static PaperComparisonAccount Account(PaperGateProfile profile) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "DelphiLive", profile, Start, Guid.NewGuid(), Guid.NewGuid(), null, null);
    private static PaperComparisonSource Source(params DelphiLiveFrozenCandidate[] candidates) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Start.AddMinutes(-2), Start.AddSeconds(-1), Start, candidates,
        new Dictionary<string, DelphiLiveFrozenBaseline>());
}
