#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class PortfolioStrategyWorkspaceTests
{
    private static WealthsimpleTfsaSnapshot Snapshot(BrokerManagedSessionTests.Fixture f, string id = "fixture-account") =>
        new(new(id, "Fixture TFSA"), f.Now, new(1000, "CAD"), new(1000, "CAD"), new(1000, "CAD"), null, null, null, null, null, []);

    [Fact]
    public void MissingRealEvidenceDoesNotBecomeCashOrAnUnfilledOrder()
    {
        var real = PortfolioStrategyWorkspace.Real(null, null, "ABC");
        Assert.Contains("unavailable", real.Confirmation);
        Assert.Contains("unavailable", real.Outcome);
        Assert.Contains("Load broker holdings", real.Next);
        Assert.DoesNotContain("No broker holding", real.Next);
        Assert.All(PortfolioStrategyWorkspace.Compare(null, null).Take(4), r => Assert.Equal("Unavailable", r.Real));
    }

    [Fact]
    public async Task WrongBrokerAccountCannotBorrowSavedStrategyOrOrderEvidence()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        Assert.Null(PortfolioStrategyWorkspace.MatchSession(Snapshot(f, "different-account"), session));
        var real = PortfolioStrategyWorkspace.Real(Snapshot(f, "different-account"), session, "AAA");
        Assert.Contains("unavailable", real.Outcome);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task PriceRejectionIsAttributedOnlyToItsSymbolAndMissingPricesStayUnknown()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        session = session with { LatestDecision = new(f.Now.UtcDateTime, f.Now, [], [], [], "PreflightFailed", "ABC", "BUY")
            { FailedOrderCheck = BrokerOrderCheck.AskAboveDecisionReference } };
        var abc = PortfolioStrategyWorkspace.Real(Snapshot(f), session, "ABC");
        Assert.Contains("Historical price pair unavailable", abc.Checks);
        Assert.DoesNotContain("10.02", abc.Checks);
        Assert.DoesNotContain("Buy stopped", PortfolioStrategyWorkspace.Real(Snapshot(f), session, "XYZ").Checks);
        Assert.Equal("Time unavailable", PortfolioStrategyWorkspace.Real(Snapshot(f), session, "XYZ").AsOf);
        Assert.Contains("No order", abc.Outcome);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task CapturedPricePairUsesRetainedValuesWithoutRecalculatingPermission()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        session = session with { LatestDecision = new(f.Now.UtcDateTime, f.Now, [], [], [], "PreflightFailed", "ABC", "BUY")
            { FailedOrderCheck = BrokerOrderCheck.AskAboveDecisionReference, RejectedBuyPrices = new(10m, 10.02m, f.Now, f.Now, f.Now) } };
        var real = PortfolioStrategyWorkspace.Real(Snapshot(f), session, "ABC");
        Assert.Contains(10m.ToString("N4"), real.Checks); Assert.Contains(10.02m.ToString("N4"), real.Checks);
        Assert.Empty(session.Orders); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WealthsimpleSubmissionStatus.Acknowledged)]
    [InlineData(WealthsimpleSubmissionStatus.OutcomeUnknown)]
    public async Task IntentOrAcknowledgementNeverClaimsAFill(WealthsimpleSubmissionStatus? submission)
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        session = session with { Orders = [new(Guid.NewGuid(), Guid.NewGuid(), null, "security-ABC", "ABC", "BUY", 100, 10, "Entry", null, f.Now, submission)] };
        var real = PortfolioStrategyWorkspace.Real(Snapshot(f), session, "ABC");
        Assert.Contains("fill unconfirmed", real.Outcome);
        Assert.DoesNotContain("Broker-reported", real.Outcome);
        Assert.Contains("outstanding order", real.Next);
    }

    [Fact]
    public async Task BrokerFillReportsActualQuantityAndDoesNotInventAHolding()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        var fact = f.Fact("fixture-order", "ABC", "BUY", 3, 30, f.Now) with { Status = "PARTIALLY_FILLED", SubmittedQuantity = 100 };
        session = session with { Orders = [new(Guid.NewGuid(), Guid.NewGuid(), null, "security-ABC", "ABC", "BUY", 100, 10, "Entry", null, f.Now, WealthsimpleSubmissionStatus.Acknowledged, fact)] };
        var real = PortfolioStrategyWorkspace.Real(Snapshot(f), session, "ABC");
        Assert.Contains("3 shares", real.Outcome); Assert.Contains("PARTIALLY_FILLED", real.Outcome);
        Assert.Contains("No broker holding", real.Next); // holdings come from the independently observed broker snapshot
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task SourceIdentityAndVersionDifferencesAreNotPresentedAsSharedExactRules()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        Assert.Contains("another Paper account", PortfolioStrategyWorkspace.Relationship(Guid.NewGuid(), null, null, session));
        Assert.Contains("differs", PortfolioStrategyWorkspace.Relationship(session.Strategy.PaperAccountId, Guid.NewGuid(), null, session));
        Assert.Contains("does not prove", PortfolioStrategyWorkspace.Relationship(session.Strategy.PaperAccountId, session.Strategy.Strategy.PolicyVersionId, session.Strategy.GateProfile, session));
        Assert.Contains("unavailable", PortfolioStrategyWorkspace.Relationship(session.Strategy.PaperAccountId, null, null, null));
    }

    [Fact]
    public async Task PaperFillLeavesRealConfirmationAndHoldingsIndependent()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        var open = new DateTime(2026, 9, 23, 13, 30, 0, DateTimeKind.Utc);
        var policy = DelphiLivePolicyDefinition.Version1;
        var paper = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), policy.PolicyVersionId,
            "OperationalChampion", null, 1000, "CAD", new(2026, 9, 23), open, open.AddDays(-1), "Fixture", "Offline test"));
        var position = Guid.NewGuid(); var action = Guid.NewGuid();
        paper = paper with { Cash = 0, Positions = [new(position, "ABC", 100, 10, open, action, "{}", DelphiLiveProfitProtectionState.Open(position, 10))],
            Fills = [new(Guid.NewGuid(), action, position, Guid.NewGuid(), "ABC", DelphiLiveActionSide.Buy, 100, 10,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.EstimatedFill, open, new(2026, 9, 23))] };
        var before = paper;
        var p = PortfolioStrategyWorkspace.Paper(paper, "ABC");
        var r = PortfolioStrategyWorkspace.Real(Snapshot(f), session, "ABC");
        Assert.Contains("Assumed buy fill", p.Outcome); Assert.Contains("protection and exits", p.Next);
        Assert.Equal("Time unavailable", p.AsOf); // An account update is not a candidate observation.
        Assert.Contains("not inherited", r.Confirmation); Assert.Contains("No broker holding", r.Next);
        Assert.Empty(session.Memory.Symbols); Assert.Empty(session.Orders); Assert.Equal(before, paper);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task SavedApprovalDoesNotClaimExecutionIsEnabled()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        Assert.Contains("Execution enable is separate", PortfolioStrategyWorkspace.Approval(session, f.Now));
        Assert.Contains("Review required", PortfolioStrategyWorkspace.Approval(session with { ReviewInvalidated = true }, f.Now));
    }
}
