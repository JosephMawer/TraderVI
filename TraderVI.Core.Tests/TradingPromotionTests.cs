#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingPromotionTests
{
    [Fact]
    public void ManualApprovalAcceptsExplicitlyDisclosedMissingPerformanceEvidence()
    {
        var review = Review();
        Assert.True(review.CanApprove);
        new TradingPromotionApproval(review, "operator", "I accept the comparison limitations").Validate();
        Assert.Contains("No qualifying performance evidence", review.Evidence.Limitations);
    }
    [Theory]
    [InlineData(false, true, 0, 0)]
    [InlineData(true, false, 0, 0)]
    [InlineData(true, true, 1, 0)]
    [InlineData(true, true, 0, 1)]
    public void ExistingPauseAndEmptyRulesRemainInForce(bool installed, bool paused, int holdings, int orders)
    {
        var blocks = TradingPromotionPolicy.OperationalBlocks(installed, new(installed, paused, holdings, orders), Account(), Hash);
        Assert.NotEmpty(blocks);
    }
    [Fact]
    public void MissingIncompatibleOrChangedReportedAccountIsNotAnEvidenceWaiver()
    {
        TradingSettingsAccess access = new(true, true, 0, 0);
        Assert.NotEmpty(TradingPromotionPolicy.OperationalBlocks(true, access, null, Hash));
        Assert.NotEmpty(TradingPromotionPolicy.OperationalBlocks(true, access, Account() with { Currency = "USD" }, Hash));
        Assert.NotEmpty(TradingPromotionPolicy.OperationalBlocks(true, access, Account(), new string('B', 64)));
        Assert.NotEmpty(TradingPromotionPolicy.OperationalBlocks(true, access, Account() with { PendingOrderCount = null }, Hash));
    }
    [Fact]
    public void ChangedPriorAssignmentReportPolicyOrAccountRequiresANewReview()
    {
        var review = Review(); var approval = new TradingPromotionApproval(review, "operator", "reviewed");
        TradingPromotionPolicy.Recheck(approval, review);
        foreach (var changed in new[] {review with {PriorPromotionId=Guid.NewGuid()},review with {AccountReportId=Guid.NewGuid()},
            review with {AccountLabel="RRSP"},review with {Strategy=review.Strategy with {MaximumHoldings=5}},
            review with {OperationalBlocks=["Trading resumed"]}})
            Assert.Throws<InvalidOperationException>(() => TradingPromotionPolicy.Recheck(approval, changed));
    }
    [Fact]
    public void SerializedRetryIsIdempotentButAlteredRequestCannotReuseApprovalIdentity()
    {
        var approval = new TradingPromotionApproval(Review(), "operator", "reviewed");
        var persisted = JsonSerializer.Deserialize<TradingPromotionApproval>(JsonSerializer.Serialize(approval))!;
        Assert.Equal(approval.RequestHash, persisted.RequestHash);
        var saved = TradingStrategyResolutionTests.Assignment(approval.Review.Strategy, TradingStrategyResolutionTests.Utc);
        Assert.Same(saved, TradingPromotionPolicy.Retry(approval.RequestHash, saved, persisted));
        Assert.Throws<InvalidOperationException>(() => TradingPromotionPolicy.Retry(approval.RequestHash, saved, persisted with { Reason = "different" }));
        Assert.Equal(64, approval.Review.Fingerprint.Length);
    }
    [Fact]
    public void ReviewAndApprovalNeverMutatePaperEvidenceOrAccountHoldings()
    {
        var review = Review(); string original = JsonSerializer.Serialize(review);
        var approval = new TradingPromotionApproval(review, "operator", "accept"); approval.Validate();
        Assert.Equal(original, JsonSerializer.Serialize(review));
        Assert.Equal("{\"paperCash\":1000000}", review.Evidence.FactsJson);
        Assert.Empty(Account().Holdings);
    }
    internal static string Hash = new('A', 64);
    internal static ReportedTradingAccount Account() => new(Guid.NewGuid(), "TFSA", "CAD", new(2026, 9, 8),
        TradingStrategyResolutionTests.Utc, "operator", Hash, 3000m, 3000m, 3000m, 3000m, 0, false, false, []);
    private static TradingPromotionReview Review() => new(Guid.NewGuid(), "TFSA", TradingStrategyResolutionTests.Strategy(),
        new(Guid.NewGuid(), "Shadow research", "DailyShadow", "{\"paperCash\":1000000}", ["No qualifying performance evidence"]), null, Guid.NewGuid(), TradingStrategyResolutionTests.Utc, []);
}
