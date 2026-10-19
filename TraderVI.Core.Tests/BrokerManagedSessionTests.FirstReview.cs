#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static Fixture FirstReviewFixture()
    { var f = new Fixture(); UseNormalLive(f); f.Facts.Clear(); f.Cash = 1100; return f; }
    private static BrokerStrategyReview FirstStrategyReview(Fixture f) => new(f.Strategy,
        new(f.Strategy.PaperAccountId, "Synthetic Normal", "DelphiLive", "{}", ["Synthetic fixture; no native qualification."]), f.Now.UtcDateTime, []);
    private static BrokerExecutionQualificationBundle FirstQualifications(Fixture f) => new(f.Binding.BindingId,
        f.Strategy.Fingerprint, f.ImplementationIdentity, f.Risk.TradingDate, f.Now, f.Now.AddHours(1), "synthetic fixture",
        f.Approval().Review.Qualifications);
    private static BrokerFirstExecutionReview FirstReview(Fixture f, BrokerExecutionQualificationBundle? evidence) =>
        BrokerFirstExecutionReviewPolicy.Prepare(FirstStrategyReview(f), f.Project(), evidence, f.ImplementationIdentity, f.Now);

    [Fact]
    public async Task FirstCashReviewUsesActualAvailableCapitalAndApprovalStillSendsNothing()
    {
        var f = FirstReviewFixture(); var review = FirstReview(f, FirstQualifications(f));
        Assert.True(review.CanApprove, string.Join("; ", review.Blocks));
        Assert.Equal(1000m, review.PurchaseCeiling); // Lower of actual cash and buying power, not the earlier fixture's 900 ceiling.
        Assert.All(review.Checks, c => Assert.True(c.Verified));
        Assert.Equal(BrokerExecutionDuration.Ongoing, review.Activation!.Duration);
        var saved = await f.Workflow().ApproveAsync(new(review.Activation, "synthetic fixture", "Offline review path only"));
        Assert.Equal(0, f.Transport.Sent); Assert.Null(saved.ActivatedAt);
        f.Store.Restart(); Assert.Equal(saved.Approval.Fingerprint, f.Store.State!.Approval.Fingerprint);
        Assert.Equal(1000m, f.Store.State.Approval.Review.PurchaseCeiling);
    }

    [Fact]
    public void FirstReviewListsEveryMissingNativeQualificationWithoutOfferingApproval()
    {
        var f = FirstReviewFixture(); var review = FirstReview(f, null);
        Assert.False(review.CanApprove); Assert.Null(review.Activation);
        Assert.Equal(BrokerActivationReview.RequiredChecks, review.Checks.Select(c => c.Check));
        Assert.All(review.Checks, c => Assert.False(c.Verified));
        Assert.Contains(review.Blocks, b => b.Contains("No independently reviewed"));
    }

    [Theory]
    [InlineData("cash-events")][InlineData("opening-values")][InlineData("native-order-lifecycle")]
    [InlineData("strategy-evidence")][InlineData("restart-recovery")][InlineData("installed-storage")]
    public void EveryOriginalQualificationRemainsRequiredByTheFirstReview(string missing)
    {
        var f = FirstReviewFixture(); var evidence = FirstQualifications(f);
        var review = FirstReview(f, evidence with { Qualifications = evidence.Qualifications.Where(q => q.Check != missing).ToImmutableArray() });
        Assert.False(review.CanApprove); Assert.False(review.Checks.Single(c => c.Check == missing).Verified);
    }

    [Theory]
    [InlineData("account")][InlineData("strategy")][InlineData("build")][InlineData("session")]
    [InlineData("expired")][InlineData("future")][InlineData("duplicate")]
    public void FirstReviewCannotReuseIncompatibleOrUnreviewedQualificationReferences(string change)
    {
        var f = FirstReviewFixture(); var e = FirstQualifications(f);
        e = change switch
        {
            "account" => e with { BindingId = Guid.NewGuid() },
            "strategy" => e with { StrategyFingerprint = new string('B', 64) },
            "build" => e with { ImplementationIdentity = "different implementation" },
            "session" => e with { Session = e.Session.AddDays(1) },
            "expired" => e with { ExpiresAt = f.Now },
            "future" => e with { ReviewedAt = f.Now.AddTicks(1) },
            _ => e with { Qualifications = e.Qualifications.Add(e.Qualifications[0]) }
        };
        Assert.False(FirstReview(f, e).CanApprove);
    }

    [Fact]
    public void ScopedReferencesSupplementRatherThanReplaceAllSixOriginalChecks()
    {
        var f = FirstReviewFixture(); var evidence = FirstQualifications(f);
        var scope = new BrokerNoTransferSessionScope(Guid.NewGuid(), f.Binding.BindingId, f.Risk.BaselineId,
            f.Risk.TradingDate, f.Now, "synthetic fixture", f.Project().Import.Plan.Observation);
        evidence = evidence with { NoTransferScope = scope,
            Qualifications = BrokerActivationReview.AdditionalScopedChecks.Select(c => new BrokerQualification(c,
                f.Binding.BindingId, f.Strategy.Fingerprint, f.Now, Fixture.Hash, "Synthetic reference only")).ToImmutableArray() };
        var review = FirstReview(f, evidence);
        Assert.False(review.CanApprove); Assert.Equal(11, review.Checks.Length);
        Assert.All(review.Checks.Where(c => BrokerActivationReview.RequiredChecks.Contains(c.Check)), c => Assert.False(c.Verified));
    }

    [Theory]
    [InlineData("holding")][InlineData("daily hold")][InlineData("capital hold")][InlineData("coverage")]
    public void QualificationPacketCannotOverrideActualHoldingsRiskOrCashBlocks(string reason)
    {
        var f = FirstReviewFixture();
        if (reason == "holding") f.Facts.Add(f.Fact("outside-buy", "AAA", "BUY", 10, 100, f.Now));
        if (reason == "daily hold") f.Risk = f.Risk with { DailyLossHold = true };
        if (reason == "capital hold") f.Risk = f.Risk with { CapitalReviewRequired = true };
        var account = f.Project(); if (reason == "coverage") account = account with { NewRiskBlocks = ["Native coverage missing"] };
        Assert.False(BrokerFirstExecutionReviewPolicy.Prepare(FirstStrategyReview(f), account,
            FirstQualifications(f), f.ImplementationIdentity, f.Now).CanApprove);
    }

    [Fact]
    public async Task AccountChangesAfterReviewPreventSavingTheFirstApproval()
    {
        var f = FirstReviewFixture(); var prepared = FirstReview(f, FirstQualifications(f));
        Assert.True(prepared.CanApprove); f.Cash -= 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(new(prepared.Activation!, "fixture", "synthetic")));
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("stale source")][InlineData("wrong source")][InlineData("stale account")]
    public void FirstApprovalRequiresTheCurrentSourceReviewAndFreshActualAccount(string reason)
    {
        var f = FirstReviewFixture(); var strategy = FirstStrategyReview(f); var account = f.Project();
        if (reason == "stale source") strategy = strategy with { PreparedUtc = f.Now.UtcDateTime.AddMinutes(-3) };
        if (reason == "wrong source") strategy = strategy with { Evidence = strategy.Evidence with { PaperAccountId = Guid.NewGuid() } };
        if (reason == "stale account") account = account with { Account = account.Account with { ReportedUtc = f.Now.UtcDateTime.AddSeconds(-31) } };
        Assert.False(BrokerFirstExecutionReviewPolicy.Prepare(strategy, account, FirstQualifications(f), f.ImplementationIdentity, f.Now).CanApprove);
    }

    [Fact]
    public void ProtectedQualificationReaderChecksEvidenceBytesAndNeverCreatesARecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TraderVI-qualification-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "qualification.dat"); var proof = Path.Combine(directory, "synthetic-proof.json");
        try
        {
            var protector = new HostProtector(); var reader = new BrokerExecutionQualificationFileStore(path, protector);
            Assert.Null(reader.Read(DateTimeOffset.UtcNow)); Assert.False(Directory.Exists(directory));
            Directory.CreateDirectory(directory); File.WriteAllText(proof, "{\"synthetic\":true}");
            var f = FirstReviewFixture(); var e = FirstQualifications(f);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(proof)));
            e = e with { Qualifications = e.Qualifications.Select(q => q with { EvidenceReference = proof, EvidenceHash = hash }).ToImmutableArray() };
            File.WriteAllBytes(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(e)));
            Assert.DoesNotContain("synthetic fixture", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(e.StrategyFingerprint, reader.Read(f.Now)!.StrategyFingerprint);
            File.WriteAllText(proof, "{\"changed\":true}"); Assert.Throws<InvalidDataException>(() => reader.Read(f.Now));
            File.Delete(proof); Assert.Throws<InvalidDataException>(() => reader.Read(f.Now));
            File.WriteAllText(path, "corrupt"); Assert.ThrowsAny<Exception>(() => reader.Read(f.Now));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(proof)) File.Delete(proof); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
