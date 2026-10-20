#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static (Fixture F, BrokerAccountRiskBaseline Baseline, BrokerImportReceipt Import,
        ImmutableArray<ManualSellRecord> Sales, ITsxSessionCalendar Calendar) TrialFacts(DateTimeOffset? at = null)
    {
        var f = new Fixture();
        if (at is { } changed) { f.Now = changed; f.Risk = f.Risk with { TradingDate = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(changed.UtcDateTime)) }; }
        UseNormalLive(f);
        var records = ImmutableArray.CreateBuilder<ManualSellRecord>();
        var quote = new ManualBuyQuote(10.01m, 10, f.Now, "OPEN", f.Now.AddHours(-1), f.Now.AddHours(1), f.Now);
        foreach (string symbol in new[] { "AAA", "BBB" })
        {
            if (symbol == "BBB") f.Facts.Add(f.Fact("second-buy", symbol, "BUY", 10, 100, f.Now.AddDays(-1)));
            var id = Guid.NewGuid(); f.Facts.Add(f.Fact("order-" + id.ToString("D"), symbol, "SELL", 10, 100, f.Now));
            var intent = new ManualSellIntent(id, "fixture-identity", "fixture-account", "security-" + symbol,
                "Fixture TFSA", symbol, "XTSE", 10, f.Now, quote, f.Now);
            records.Add(new(intent, WealthsimpleSubmissionStatus.Acknowledged,
                [new("FILLED", 10, 10, new(10, "CAD"), new(100, "CAD"), new(100, "CAD"),
                    new(0, "CAD"), new(0, "CAD"), f.Now, f.Now, null, "REGULAR", f.Now)],
                f.Now, InventoryVerifiedAt: f.Now));
        }
        f.Cash = 1100;
        var reference = new BrokerRiskReference(1100, BrokerRiskReferenceSource.SetupObservation, Guid.NewGuid(), f.Now);
        f.Risk = f.Risk with { Through = f.Now, SessionReference = reference, DrawdownReference = reference };
        var import = f.Project().Import;
        var baseline = new BrokerAccountRiskBaseline(f.Risk.BaselineId, f.Risk.BindingId, "TFSA", import.RequestId,
            f.Now, f.Risk.TradingDate, reference, reference, null, null, TradingRecommendationMemory.Empty)
            { ObservationAt = f.Now };
        ITsxSessionCalendar calendar = new ReviewedTsxSessionCalendar(new("fixture", "synthetic only",
            f.Risk.TradingDate, f.Risk.TradingDate, [f.Risk.TradingDate]));
        return (f, baseline, import, records.ToImmutable(), calendar);
    }

    [Fact]
    public async Task ExplicitTrialUsesNineHonestChecksAndStillRequiresIndependentOtherQualifications()
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, sales, "synthetic", calendar, f.Now);
        Assert.True(scope.LimitedFirstSessionTrial); Assert.True(scope.OpeningSalesVerified);
        var checks = BrokerActivationReview.ChecksFor(scope);
        Assert.Equal(9, checks.Length); Assert.DoesNotContain("cash-events", checks); Assert.DoesNotContain("opening-values", checks);
        var qualifications = checks.Select(c => new BrokerQualification(c, f.Binding.BindingId,
            f.Strategy.Fingerprint, f.Now, Fixture.Hash, "Synthetic proof only")).ToImmutableArray();
        var evidence = FirstQualifications(f) with { Qualifications = qualifications, NoTransferScope = scope };
        var review = FirstReview(f, evidence);
        Assert.True(review.CanApprove, string.Join("; ", review.Blocks));
        Assert.Equal(BrokerExecutionDuration.Ongoing, review.Activation!.Duration); // Retained protection may continue; scope limits buys.
        var saved = await f.Workflow().ApproveAsync(new(review.Activation, "synthetic", "Offline scoped trial only"));
        Assert.Equal(0, f.Transport.Sent); Assert.Null(saved.ActivatedAt);
        f.Store.Restart(); Assert.True(f.Store.State!.Approval.Review.NoTransferScope!.LimitedFirstSessionTrial);
        foreach (string required in checks)
            Assert.False(FirstReview(f, evidence with { Qualifications = qualifications.Where(q => q.Check != required).ToImmutableArray() }).CanApprove);
        Assert.False(FirstReview(f, evidence with { NoTransferScope = scope with { LimitedFirstSessionTrial = false } }).CanApprove);
    }

    [Theory]
    [InlineData("old baseline")][InlineData("wrong import")][InlineData("changed reference")]
    [InlineData("hold")][InlineData("incomplete sales")][InlineData("partial sale")][InlineData("unverified inventory")]
    [InlineData("after close")]
    public void TrialPreparationCannotInventOpeningBooksOrResetKnownReferences(string change)
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        if (change == "old baseline") baseline = baseline with { TradingDate = baseline.TradingDate.AddDays(-1) };
        if (change == "wrong import") import = import with { RequestId = Guid.NewGuid() };
        if (change == "changed reference") f.Risk = f.Risk with { DrawdownReference = f.Risk.DrawdownReference with { Value = 1200 } };
        if (change == "hold") f.Risk = f.Risk with { CapitalReviewRequired = true };
        if (change == "incomplete sales") sales = sales.RemoveAt(0);
        if (change == "partial sale") sales = sales.SetItem(0, sales[0] with { Intent = sales[0].Intent with { Quantity = 11 } });
        if (change == "unverified inventory") sales = sales.SetItem(0, sales[0] with { InventoryVerifiedAt = null });
        if (change == "after close") f.Now = calendar.GetSessionBounds(f.Risk.TradingDate).CloseUtc;
        Assert.Throws<InvalidOperationException>(() => BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk,
            sales, "synthetic", calendar, f.Now));
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task RestoredTrialSourceReconcilesOwnedCashAndExpiresAtTheExactClose()
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, sales, "synthetic", calendar, f.Now);
        scope = JsonSerializer.Deserialize<BrokerNoTransferSessionScope>(JsonSerializer.Serialize(scope))!;
        var clock = new MutableTrialClock(f.Now);
        var source = new BrokerNoTransferRiskEvidenceSource(scope, calendar, clock);
        Assert.Empty((await source.ReadAsync(import, f.Risk, [], CancellationToken.None)).Blocks);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-CCC", "CCC", "BUY", 2, 10, "Initial", null, f.Now);
        var fact = f.Fact(order.ExternalId, "CCC", "BUY", 2, 20.25m, f.Now);
        var current = import with { Plan = import.Plan with { Observation = import.Plan.Observation with {
            Cash = 1079.75m, Orders = import.Plan.Observation.Orders.Append(fact).ToArray() } } };
        Assert.Empty((await source.ReadAsync(current, f.Risk, [order], CancellationToken.None)).Blocks);
        Assert.NotEmpty((await source.ReadAsync(current, f.Risk, [], CancellationToken.None)).Blocks);
        clock.Now = calendar.GetSessionBounds(f.Risk.TradingDate).CloseUtc;
        var expired = await source.ReadAsync(current, f.Risk, [order], CancellationToken.None);
        Assert.Null(expired.Coverage); Assert.Empty(expired.Continuations);
        Assert.Equal(baseline.SessionReference, f.Risk.SessionReference);
        Assert.Equal(baseline.DrawdownReference, f.Risk.DrawdownReference);
    }

    [Fact]
    public void OlderScopeJsonKeepsTheOriginalProductionContract()
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, sales, "synthetic", calendar, f.Now)
            with { LimitedFirstSessionTrial = false };
        string json = JsonSerializer.Serialize(scope); Assert.DoesNotContain("LimitedFirstSessionTrial", json);
        var old = JsonSerializer.Deserialize<BrokerNoTransferSessionScope>(json)!;
        Assert.Equal(11, BrokerActivationReview.ChecksFor(old).Length);
        Assert.All(BrokerActivationReview.RequiredChecks, c => Assert.Contains(c, BrokerActivationReview.ChecksFor(old)));
    }

    [Fact]
    public void LimitedTrialCannotExtendItsPurchaseDeadlineOrUseAnUnreviewedStrategyProfile()
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, sales, "synthetic", calendar, f.Now);
        Assert.Throws<InvalidOperationException>(() => (scope with { PurchaseExpiresAt = scope.PurchaseExpiresAt!.Value.AddDays(1) }).Validate(calendar));
        Assert.Throws<InvalidOperationException>(() => (scope with { PurchaseExpiresAt = null }).ValidateAnchor());
        Assert.Throws<InvalidOperationException>(() => (scope with { Session = scope.Session.AddDays(1) }).ValidateAnchor());
        Assert.Equal(calendar.GetSessionBounds(scope.Session).CloseUtc, scope.PurchaseExpiresAt);
    }

    private sealed class MutableTrialClock(DateTimeOffset value) : TimeProvider
    { internal DateTimeOffset Now = value; public override DateTimeOffset GetUtcNow() => Now; }

    [Fact]
    public void OperatorCanSelectOriginalSalesWithoutAbsorbingALaterVerificationLiquidation()
    {
        var (f, baseline, import, sales, calendar) = TrialFacts();
        var testSale = sales[0] with { Intent = sales[0].Intent with { RequestId = Guid.NewGuid(),
            Symbol = "CCC", SecurityId = "security-CCC", Quantity = 1 } };
        testSale = testSale with { Observations = testSale.Observations.Select(o => o with {
            SubmittedQuantity = 1, FilledQuantity = 1 }).ToImmutableArray() };
        var anchor = import.Plan.Observation;
        var testFact = anchor.Orders.First(o => o.ExternalId == "order-" + sales[0].Intent.RequestId.ToString("D")) with {
            ExternalId = "order-" + testSale.Intent.RequestId.ToString("D"), Symbol = "CCC", SecurityId = "security-CCC",
            SubmittedQuantity = 1, FilledQuantity = 1 };
        anchor = anchor with { Orders = anchor.Orders.Append(testFact).ToArray() };
        var journal = sales.Add(testSale);
        var candidates = BrokerSupervisedTrialPreparation.OpeningSaleCandidates(anchor, journal, baseline.TradingDate);
        Assert.Equal(3, candidates.Length);
        var ids = sales.Reverse().Select(s => s.Intent.RequestId).ToImmutableArray();
        var selected = BrokerSupervisedTrialPreparation.SelectOpeningSales(anchor, journal, baseline.TradingDate, ids);
        Assert.Equal(ids.ToArray(), selected.Select(s => s.Intent.RequestId).ToArray());
        Assert.DoesNotContain(selected, s => s.Intent.RequestId == testSale.Intent.RequestId);
        import = import with { Plan = import.Plan with { Observation = anchor } };
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, selected, "synthetic", calendar, f.Now);
        Assert.True(scope.OpeningSalesVerified); Assert.Equal(ids.ToArray(), scope.OpeningSales.Select(s => s.Intent.RequestId).ToArray());
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("one")][InlineData("duplicate selection")][InlineData("unknown")][InlineData("duplicate journal")]
    [InlineData("same holding")][InlineData("other account")][InlineData("other identity")][InlineData("other day")]
    [InlineData("partial")][InlineData("unverified inventory")][InlineData("later inventory")]
    [InlineData("native fact missing")][InlineData("native fact differs")][InlineData("conflict")]
    public void OriginalSaleSelectionRejectsUnverifiedOrAmbiguousRecords(string change)
    {
        var (f, baseline, import, sales, _) = TrialFacts();
        var ids = sales.Select(s => s.Intent.RequestId).ToImmutableArray();
        if (change == "one") ids = ids.RemoveAt(1);
        if (change == "duplicate selection") ids = ids.SetItem(1, ids[0]);
        if (change == "unknown") ids = ids.SetItem(1, Guid.NewGuid());
        if (change == "duplicate journal") sales = sales.Add(sales[0]);
        var first = sales[0];
        if (change == "same holding") first = first with { Intent = first.Intent with {
            SecurityId = sales[1].Intent.SecurityId, Symbol = sales[1].Intent.Symbol } };
        if (change == "other account") first = first with { Intent = first.Intent with { AccountId = "other-account" } };
        if (change == "other identity") first = first with { Intent = first.Intent with { IdentityId = "other-identity" } };
        if (change == "other day") first = first with { Intent = first.Intent with { CreatedAt = f.Now.AddDays(-1) } };
        if (change == "partial") first = first with { Intent = first.Intent with { Quantity = first.Intent.Quantity + 1 } };
        if (change == "unverified inventory") first = first with { InventoryVerifiedAt = null };
        if (change == "later inventory") first = first with { InventoryVerifiedAt = f.Now.AddSeconds(1) };
        if (change == "conflict") first = first with { ConflictingObservation = true };
        sales = sales.SetItem(0, first);
        var anchor = import.Plan.Observation;
        var externalId = "order-" + first.Intent.RequestId.ToString("D");
        if (change == "native fact missing") anchor = anchor with { Orders = anchor.Orders.Where(o => o.ExternalId != externalId).ToArray() };
        if (change == "native fact differs") anchor = anchor with { Orders = anchor.Orders.Select(o =>
            o.ExternalId == externalId ? o with { NetValue = o.NetValue + 1 } : o).ToArray() };
        if (change == "same holding") anchor = anchor with { Orders = anchor.Orders.Select(o =>
            o.ExternalId == externalId ? o with { SecurityId = first.Intent.SecurityId, Symbol = first.Intent.Symbol } : o).ToArray() };
        Assert.Throws<InvalidOperationException>(() => BrokerSupervisedTrialPreparation.SelectOpeningSales(anchor,
            sales, baseline.TradingDate, ids));
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void TrialEvidencePublicationNeverManufacturesTheRemainingNativeQualifications(bool nativeProofPresent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TraderVI-trial-proof-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "qualification.dat"); var proof = Path.Combine(directory, "independent-proof.json");
            var (f, baseline, import, sales, calendar) = TrialFacts(); var protector = new HostProtector();
            if (nativeProofPresent)
            {
                File.WriteAllText(proof, "Synthetic stand-in for independent native evidence; never install.");
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(proof)));
                var packet = FirstQualifications(f) with { Qualifications = FirstQualifications(f).Qualifications
                    .Where(q => q.Check is not ("cash-events" or "opening-values"))
                    .Select(q => q with { EvidenceHash = hash, EvidenceReference = proof }).ToImmutableArray() };
                File.WriteAllBytes(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(packet)));
            }
            var scope = BrokerSupervisedTrialPreparation.Prepare(baseline, import, f.Risk, sales, "synthetic", calendar, f.Now);
            var store = new BrokerSupervisedTrialEvidenceFileStore(path, protector);
            var prepared = store.Prepare(scope, f.Strategy, f.ImplementationIdentity, calendar, f.Now);
            Assert.DoesNotContain("fixture-account", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(nativeProofPresent ? 9 : 4, prepared.Qualifications.Length);
            Assert.DoesNotContain(prepared.Qualifications, q => q.Check is "cash-events" or "opening-values");
            var review = FirstReview(f, prepared); Assert.Equal(nativeProofPresent, review.CanApprove);
            if (!nativeProofPresent)
                Assert.All(BrokerActivationReview.RequiredChecks.Where(c => c is not ("cash-events" or "opening-values")),
                    c => Assert.Contains(review.Checks, q => q.Check == c && !q.Verified));
            var retry = store.Prepare(scope with { ApprovedAt = f.Now.AddSeconds(1) }, f.Strategy, f.ImplementationIdentity, calendar, f.Now.AddSeconds(1));
            Assert.Equal(BrokerAccountImport.Hash(prepared), BrokerAccountImport.Hash(retry));
            Assert.Single(Directory.GetFiles(directory, "trial-evidence-*.dat"));
            var altered = prepared.NoTransferScope! with { CashOnlyAnchor = scope.CashOnlyAnchor with { Cash = 1200, AccountValue = 1200 } };
            Assert.Throws<InvalidOperationException>(() => store.Prepare(altered, f.Strategy, f.ImplementationIdentity, calendar, f.Now));
            Assert.Equal(BrokerAccountImport.Hash(prepared), BrokerAccountImport.Hash(new BrokerExecutionQualificationFileStore(path, protector).Read(f.Now)));
            Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
