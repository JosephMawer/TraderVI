#nullable enable
using Core.Trader.Promotion;
using Core.Db;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Xunit;
using System.Threading.Tasks;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static (Fixture F, BrokerNativeTrialObservation Before, BrokerNativeTrialObservation After,
        BrokerNoTransferSessionScope Scope, ITsxSessionCalendar Calendar) NativeTrialFacts(DateTimeOffset? at = null)
    {
        var (f, baseline, _, originals, calendar) = TrialFacts(at);
        var buyAt = f.Now.AddMinutes(1); var sellAt = f.Now.AddMinutes(2);
        var buyId = Guid.NewGuid(); var sellId = Guid.NewGuid();
        f.Facts.Add(f.Fact("order-" + buyId.ToString("D"), "CCC", "BUY", 1, 10, buyAt));
        f.Facts.Add(f.Fact("order-" + sellId.ToString("D"), "CCC", "SELL", 1, 10, sellAt));
        var quote = originals[0].Intent.CheckedQuote;
        var buy = new ManualBuyRecord(new(buyId, "fixture-identity", "fixture-account", "security-CCC", "Synthetic TFSA", "CCC", "XTSE", 1,
            new("CCC", "Synthetic native trial only", "Manual"), 10.01m, quote, 1000, buyAt.AddSeconds(-5)), WealthsimpleSubmissionStatus.Acknowledged,
            [new("FILLED", 1, 1, new(10,"CAD"), new(10,"CAD"), new(10,"CAD"), new(0,"CAD"), new(0,"CAD"), buyAt.AddSeconds(-5), buyAt, null,"REGULAR",buyAt)], buyAt);
        var sale = new ManualSellRecord(new(sellId,"fixture-identity","fixture-account","security-CCC","Synthetic TFSA","CCC","XTSE",1,sellAt,quote,sellAt),
            WealthsimpleSubmissionStatus.Acknowledged,[new("FILLED",1,1,new(10,"CAD"),new(10,"CAD"),new(10,"CAD"),new(0,"CAD"),new(0,"CAD"),sellAt,sellAt,null,"REGULAR",sellAt)],sellAt,InventoryVerifiedAt:sellAt);
        f.Now = f.Now.AddMinutes(3); f.Risk = f.Risk with { Through = f.Now };
        var import = f.Project().Import; baseline = baseline with { ImportRequestId = import.RequestId, ObservationAt = f.Now, EstablishedAt = f.Now };
        var before = new BrokerNativeTrialObservation(Guid.NewGuid(),1,f.Now.AddHours(-1),f.Now,FirstStrategyReview(f),f.Now,
            import.Plan.Observation,baseline,new(baseline,"synthetic",Fixture.Hash),f.Risk,true,true,true,true,[buy],originals.Add(sale));
        var scope = BrokerSupervisedTrialPreparation.Prepare(baseline,import,f.Risk,originals,"synthetic",calendar,f.Now) with { FullFillOnlyNativeTrial = true };
        var started = f.Now.AddMinutes(1); f.Now = f.Now.AddMinutes(2);
        var after = before with { ObservationId = Guid.NewGuid(), ProcessId = 2, ProcessStartedAt = started, CapturedAt = f.Now,
            Account = before.Account with { SnapshotAt = f.Now, CheckedAt = f.Now }, Strategy = FirstStrategyReview(f), NormalUpdatedAt = f.Now,
            Buys = [buy with { LastCheckedAt = f.Now, Observations = buy.Observations.Select(o => o with { RetrievedAt = f.Now }).ToArray() }],
            Sales = before.Sales.Select(s => s with { LastCheckedAt = f.Now,
                Observations = s.Observations.Select(o => o with { RetrievedAt = f.Now }).ToArray() }).ToImmutableArray() };
        return (f,before,after,scope,calendar);
    }

    [Fact]
    public async Task NativeTrialPublicationNeedsActualObservationStagesAndCannotBecomeProductionAuthority()
    {
        var (f,before,after,scope,calendar) = NativeTrialFacts();
        var directory = Path.Combine(Path.GetTempPath(),"TraderVI-native-trial-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory,"qualification.dat"); var protector = new HostProtector(); var store = new BrokerNativeTrialEvidenceFileStore(path,protector);
            Assert.Throws<InvalidOperationException>(()=>store.Complete(after,scope,calendar,"synthetic",f.Now)); Assert.False(File.Exists(path));
            store.Capture(before); Assert.False(File.Exists(path));
            Assert.Throws<InvalidOperationException>(()=>store.Complete(before,scope,calendar,"synthetic",before.CapturedAt)); Assert.False(File.Exists(path));
            var packet = store.Complete(after,scope,calendar,"synthetic",f.Now);
            Assert.Equal(9,packet.Qualifications.Length); Assert.All(packet.Qualifications,q=>Assert.True(q.FirstSessionTrialOnly));
            Assert.Equal(scope.PurchaseExpiresAt,packet.ExpiresAt);
            var read = new BrokerExecutionQualificationFileStore(path,protector).Read(f.Now)!;
            Assert.Equal(BrokerAccountImport.Hash(packet),BrokerAccountImport.Hash(read));
            Assert.True(FirstReview(f,packet).CanApprove,string.Join(";",FirstReview(f,packet).Blocks));
            var saved = await f.Workflow().ApproveAsync(new(FirstReview(f,packet).Activation!,"synthetic","Offline only"));
            Assert.Null(saved.ActivatedAt); Assert.Equal(0,f.Transport.Sent);
            Assert.Throws<InvalidOperationException>(()=>(packet with { NoTransferScope = null }).Validate(f.Now));
            Assert.Throws<InvalidOperationException>(()=>(packet with { NoTransferScope = scope with { FullFillOnlyNativeTrial = false } }).Validate(f.Now));
            Assert.Equal(BrokerAccountImport.Hash(packet),BrokerAccountImport.Hash(store.Complete(after,scope,calendar,"synthetic",f.Now)));
            File.AppendAllText(packet.Qualifications[0].EvidenceReference,"altered");
            Assert.Throws<InvalidDataException>(()=>new BrokerExecutionQualificationFileStore(path,protector).Read(f.Now));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Theory]
    [InlineData("same process")][InlineData("no restart")][InlineData("no buy")][InlineData("partial buy")]
    [InlineData("cancelled sale")][InlineData("unknown submission")][InlineData("no status read")]
    [InlineData("missing native buy")][InlineData("changed order")][InlineData("cash mismatch")]
    [InlineData("changed reference")][InlineData("missing table")][InlineData("trigger disabled")][InlineData("wrong Find")]
    [InlineData("stale source")][InlineData("stale account")][InlineData("expired")][InlineData("old contract")]
    public void NativeTrialCannotQualifyMissingOrSyntheticReplacementClaims(string change)
    {
        var (f,before,after,scope,calendar) = NativeTrialFacts();
        if(change=="same process")after=after with { ProcessId=before.ProcessId };
        if(change=="no restart")after=after with { ProcessStartedAt=before.ProcessStartedAt };
        if(change=="no buy")after=after with { Buys=[] };
        if(change=="partial buy")after=after with { Buys=[after.Buys[0] with { Intent=after.Buys[0].Intent with { Quantity=2 } }] };
        if(change=="cancelled sale")after=after with { Sales=after.Sales.SetItem(2,after.Sales[2] with { Observations=after.Sales[2].Observations.Select(o=>o with { Status="CANCELLED" }).ToArray() }) };
        if(change=="unknown submission")after=after with { Buys=[after.Buys[0] with { Submission=WealthsimpleSubmissionStatus.OutcomeUnknown }] };
        if(change=="no status read")after=after with { Buys=before.Buys };
        if(change=="missing native buy")after=after with { Account=after.Account with { Orders=after.Account.Orders.Where(o=>o.ExternalId!="order-"+after.Buys[0].Intent.RequestId.ToString("D")).ToArray() } };
        if(change=="changed order")after=after with { Account=after.Account with { Orders=after.Account.Orders.Select(o=>o with { NetValue=o.NetValue+1 }).ToArray() } };
        if(change=="cash mismatch")after=after with { Account=after.Account with { Cash=after.Account.Cash+1,AccountValue=after.Account.AccountValue+1 } };
        if(change=="changed reference")after=after with { Baseline=after.Baseline with { SessionReference=after.Baseline.SessionReference with { Value=1234 } } };
        if(change=="missing table")after=after with { SessionInstalled=false };
        if(change=="trigger disabled")after=after with { ImmutableStorageVerified=false };
        if(change=="wrong Find")after=after with { FoundBaseline=after.FoundBaseline with { Baseline=after.Baseline with { BaselineId=Guid.NewGuid() } } };
        if(change=="stale source")after=after with { NormalUpdatedAt=f.Now.AddMinutes(-3) };
        if(change=="stale account")after=after with { Account=after.Account with { SnapshotAt=f.Now.AddSeconds(-31) } };
        if(change=="expired")f.Now=calendar.GetSessionBounds(scope.Session).CloseUtc;
        if(change=="old contract")scope=scope with { FullFillOnlyNativeTrial=false };
        Assert.Throws<InvalidOperationException>(()=>BrokerNativeTrialQualification.ValidateRecovery(before,after,scope,calendar,f.Now));
        Assert.Null(f.Store.State); Assert.Equal(0,f.Transport.Sent);
    }

    [Theory]
    [InlineData("PARTIALLY_FILLED",1,WealthsimpleSubmissionStatus.Acknowledged)]
    [InlineData("CANCELLED",1,WealthsimpleSubmissionStatus.Acknowledged)]
    [InlineData("EXPIRED",0,WealthsimpleSubmissionStatus.Acknowledged)]
    [InlineData("FILLED",2,WealthsimpleSubmissionStatus.OutcomeUnknown)]
    [InlineData("FILLED",2,null)]
    public async Task FullFillTrialLatchesOperatorReviewEvenAfterOriginalOrderRecovers(string status,int filled,WealthsimpleSubmissionStatus? submission)
    {
        var (f,before,_,scope,_) = NativeTrialFacts();
        var proof = FirstQualifications(f) with { NoTransferScope=scope,
            Qualifications=BrokerActivationReview.ChecksFor(scope).Select(c=>new BrokerQualification(c,f.Binding.BindingId,f.Strategy.Fingerprint,f.Now,Fixture.Hash,"Synthetic only") { FirstSessionTrialOnly=true }).ToImmutableArray() };
        var review=FirstReview(f,proof); Assert.True(review.CanApprove,string.Join(";",review.Blocks));
        var saved=await f.Workflow().ApproveAsync(new(review.Activation!,"synthetic","Offline trial"));
        var id=Guid.NewGuid(); var order=new BrokerStrategyOrder(id,Guid.NewGuid(),null,"security-DDD","DDD","BUY",2,10,"Initial",null,f.Now,submission);
        f.Store.State=saved with { Orders=[order] };
        var local = before.Buys[0] with { Intent=before.Buys[0].Intent with { RequestId=id, SecurityId="security-DDD", Symbol="DDD", Quantity=2 },
            Submission=submission, Observations=before.Buys[0].Observations.Select(o=>o with { SubmittedQuantity=2, FilledQuantity=filled, Status=status }).ToArray() };
        f.TrialOrderReview=orders=>BrokerNativeTrialQualification.RequiresOrderReview([local],[],orders);
        f.Facts.Add(f.Fact(order.ExternalId,"DDD","BUY",filled,filled*10,f.Now) with { SubmittedQuantity=2,Status=status }); f.Cash-=filled*10;
        var held=await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(held.TrialOrderReviewRequired); Assert.False(held.NewBuysStopped); Assert.Equal(0,f.Transport.Sent);
        f.Facts[f.Facts.Count-1]=f.Fact(order.ExternalId,"DDD","BUY",2,20,f.Now); f.Cash=1080;
        f.Store.Restart(); await f.Workflow().ResumeBuyingAsync(f.Binding.BindingId);
        var recovered=await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.TrialOrderReviewRequired); Assert.Equal(BrokerManagedPhase.Paused,recovered.Phase);
        Assert.Equal(id,recovered.Orders.Single().RequestId); Assert.Equal(0,f.Transport.Sent);
        var attempted = recovered with { TrialOrderReviewRequired=false, Revision=recovered.Revision+1,
            PreviousRevisionId=recovered.RevisionId, RevisionId=Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(()=>BrokerManagedSessionRepository.ValidateNext(recovered,attempted));
    }

    [Fact]
    public void EarlierObservedPartialCannotDisappearWhenLatestReadIsFull()
    {
        var (f,before,after,scope,calendar)=NativeTrialFacts();
        var full=after.Buys[0]; var partial=full.Latest! with { Status="PARTIALLY_FILLED",FilledQuantity=.5m };
        full=full with { Observations=new[] { partial,full.Latest! } };
        after=after with { Buys=[full] };
        Assert.Throws<InvalidOperationException>(()=>BrokerNativeTrialQualification.ValidateRecovery(before,after,scope,calendar,f.Now));
        var order=new BrokerStrategyOrder(full.Intent.RequestId,Guid.NewGuid(),null,full.Intent.SecurityId,full.Intent.Symbol,"BUY",1,10,"Initial",null,f.Now,WealthsimpleSubmissionStatus.Acknowledged);
        Assert.True(BrokerNativeTrialQualification.RequiresOrderReview([full],[],[order]));
    }

    [Theory]
    [InlineData("PENDING",0,false)][InlineData("PARTIALLY_FILLED",1,true)][InlineData("CANCELLED",0,true)]
    [InlineData("EXPIRED",0,true)][InlineData("FILLED",1,true)][InlineData("FILLED",2,false)]
    public void NativeJournalInspectionDistinguishesAcknowledgedPendingFromUnsupportedOutcome(string status,int filled,bool blocked)
    {
        var (_,before,_,_,_)=NativeTrialFacts(); var record=before.Buys[0];
        record=record with { Intent=record.Intent with { Quantity=2 },
            Observations=record.Observations.Select(o=>o with { SubmittedQuantity=2,FilledQuantity=filled,Status=status }).ToArray() };
        var order=new BrokerStrategyOrder(record.Intent.RequestId,Guid.NewGuid(),null,record.Intent.SecurityId,record.Intent.Symbol,"BUY",2,10,"Initial",null,before.CapturedAt,WealthsimpleSubmissionStatus.Acknowledged);
        Assert.Equal(blocked,BrokerNativeTrialQualification.RequiresOrderReview([record],[],[order]));
        var sale=before.Sales[0] with { Intent=before.Sales[0].Intent with { Quantity=2 },
            Observations=before.Sales[0].Observations.Select(o=>o with { SubmittedQuantity=2,FilledQuantity=filled,Status=status }).ToArray() };
        var sell=order with { RequestId=sale.Intent.RequestId,SecurityId=sale.Intent.SecurityId,Symbol=sale.Intent.Symbol,Side="SELL" };
        Assert.Equal(blocked,BrokerNativeTrialQualification.RequiresOrderReview([],[sale],[sell]));
    }

    [Fact]
    public async Task AcknowledgedPendingReadMayWaitButLocalPartialLatchesBeforeImportCanComplete()
    {
        var (f,_,_,scope,_) = NativeTrialFacts();
        var packet = FirstQualifications(f) with { NoTransferScope=scope,
            Qualifications=BrokerActivationReview.ChecksFor(scope).Select(c=>new BrokerQualification(c,f.Binding.BindingId,
                f.Strategy.Fingerprint,f.Now,Fixture.Hash,"Synthetic only") { FirstSessionTrialOnly=true }).ToImmutableArray() };
        var saved=await f.Workflow().ApproveAsync(new(FirstReview(f,packet).Activation!,"synthetic","Offline only"));
        var order=new BrokerStrategyOrder(Guid.NewGuid(),Guid.NewGuid(),null,"security-DDD","DDD","BUY",2,10,"Initial",null,f.Now,WealthsimpleSubmissionStatus.Acknowledged);
        f.Store.State=saved with { Orders=[order] };
        f.TransformAccount=_=>throw new InvalidOperationException("Synthetic pending import");
        f.TrialOrderReview=_=>false;
        var pending=await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.False(pending.TrialOrderReviewRequired); Assert.Equal(0,f.Transport.Sent);
        f.TrialOrderReview=_=>true;
        var partial=await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(partial.TrialOrderReviewRequired); Assert.Equal(order.RequestId,partial.Orders.Single().RequestId);
        Assert.Equal(0,f.Transport.Sent);
    }
}
