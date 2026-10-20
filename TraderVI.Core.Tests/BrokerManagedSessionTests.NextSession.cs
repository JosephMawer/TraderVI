#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static (Fixture F, BrokerNativeTrialObservation Before, BrokerNativeTrialObservation After,
        BrokerNoTransferSessionScope Scope) NextSessionFacts()
    {
        var (f, original, _, _, _) = NativeTrialFacts(BrokerTrialCarryForwardTests.OriginalAt);
        var baseline=original.Baseline; var calendar=BrokerTrialCarryForwardTests.Calendar();
        var import=new BrokerImportReceipt(baseline.ImportRequestId,baseline.BindingId,Fixture.Hash,original.CapturedAt,
            new("TFSA",BrokerAccountImport.Key(original.Account),Fixture.Hash,null,original.Account,[],[]));
        f.Now=BrokerTrialCarryForwardTests.Now;
        var account=original.Account with {SnapshotAt=f.Now,CheckedAt=f.Now};
        var risk=BrokerAccountRiskContinuation.Start(baseline);
        risk=BrokerAccountRiskContinuation.Apply(risk,BrokerTrialCarryForward.Prepare(baseline,import,risk,account,
            "synthetic",true,calendar,f.Now),calendar,f.Now);
        f.Risk=risk;
        BrokerNativeTrialObservation Observe(int process, DateTimeOffset started) => original with {
            ObservationId=Guid.NewGuid(),ProcessId=process,ProcessStartedAt=started,CapturedAt=f.Now,
            Account=account with {SnapshotAt=f.Now,CheckedAt=f.Now},Risk=risk,
            Strategy=FirstStrategyReview(f),NormalUpdatedAt=f.Now,
            Buys=original.Buys.Select(b=>b with {LastCheckedAt=f.Now,
                Observations=b.Observations.Select(o=>o with {RetrievedAt=f.Now}).ToArray()}).ToImmutableArray(),
            Sales=original.Sales.Select(s=>s with {LastCheckedAt=f.Now,
                Observations=s.Observations.Select(o=>o with {RetrievedAt=f.Now}).ToArray()}).ToImmutableArray() };
        var before=Observe(1,f.Now.AddHours(-1));
        var scope=BrokerSupervisedTrialPreparation.Prepare(baseline,import,risk,original.Sales.Take(2).ToImmutableArray(),
            "synthetic",calendar,f.Now);
        var started=f.Now.AddMinutes(1);f.Now=f.Now.AddMinutes(2);
        return(f,before,Observe(2,started),scope);
    }
    private sealed class NextSessionClock(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow()=>at; }
    [Fact]
    public async Task NextSessionUsesOriginalTradeDateAndUnchangedReferencesForAllNineNativeChecks()
    {
        var (f,before,after,scope)=NextSessionFacts();var calendar=BrokerTrialCarryForwardTests.Calendar();
        Assert.Equal(new DateOnly(2026,10,19),before.Baseline.TradingDate);
        Assert.Equal(new DateOnly(2026,10,20),scope.Session);
        Assert.Equal(before.Baseline.SessionReference,after.Risk.SessionReference);
        BrokerNativeTrialQualification.ValidateRecovery(before,after,scope,calendar,f.Now);
        var directory=Path.Combine(Path.GetTempPath(),"TraderVI-next-session-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"packet.dat");var store=new BrokerNativeTrialEvidenceFileStore(path,new HostProtector());
            store.Capture(before);
            Assert.NotNull(store.ReadCheckpoint(scope.Session)); Assert.Null(store.ReadCheckpoint(before.Baseline.TradingDate));
            var packet=store.Complete(after,scope,calendar,"synthetic",f.Now);
            Assert.Equal(9,packet.Qualifications.Length);Assert.All(packet.Qualifications,q=>Assert.True(q.FirstSessionTrialOnly));
            Assert.Equal(calendar.GetSessionBounds(scope.Session).CloseUtc,packet.ExpiresAt);
            var receipt=new BrokerImportReceipt(Guid.NewGuid(),before.Baseline.BindingId,Fixture.Hash,f.Now,
                new("TFSA",f.Binding.BrokerKey,Fixture.Hash,null,after.Account,[],[]));
            var evidence=await new BrokerNoTransferRiskEvidenceSource(scope,calendar,new NextSessionClock(f.Now))
                .ReadAsync(receipt,after.Risk,[],CancellationToken.None);
            Assert.Empty(evidence.Blocks);Assert.Empty(evidence.Continuations);
            Assert.Equal(BrokerTrialCarryForward.Contract,evidence.Coverage!.Contract);
            var projection=BrokerAccountProjector.Project(f.Binding,receipt,after.Risk,evidence.Coverage,f.Now);
            var review=BrokerFirstExecutionReviewPolicy.Prepare(after.Strategy,projection,packet,
                f.Strategy.Strategy.ImplementationIdentity,f.Now);
            Assert.True(review.CanApprove,string.Join(";",review.Blocks));
            Assert.Throws<InvalidOperationException>(()=>(review.Activation! with {NoTransferScope=scope with {TrialCarry=null}}).Validate(projection,f.Now));
            var expired=await new BrokerNoTransferRiskEvidenceSource(scope,calendar,
                new NextSessionClock(calendar.GetSessionBounds(scope.Session).CloseUtc)).ReadAsync(receipt,after.Risk,[],CancellationToken.None);
            Assert.Null(expired.Coverage);Assert.NotEmpty(expired.Blocks);
        }
        finally { Directory.Delete(directory,true); }
    }
    [Theory]
    [InlineData("carry missing")] [InlineData("carry changed")] [InlineData("reference reset")]
    [InlineData("cash changed")] [InlineData("extra order")] [InlineData("later date")]
    public void NextSessionCannotReplaceOriginalEvidenceOrRollAgain(string change)
    {
        var (f,before,after,scope)=NextSessionFacts();
        if(change=="carry missing")after=after with {Risk=after.Risk with {TrialCarry=null}};
        if(change=="carry changed")scope=scope with {TrialCarry=scope.TrialCarry! with {ReviewedBy="different"}};
        if(change=="reference reset")after=after with {Risk=after.Risk with {SessionReference=after.Risk.SessionReference with {Value=999}}};
        if(change=="cash changed")after=after with {Account=after.Account with {Cash=999,AccountValue=999}};
        if(change=="extra order")after=after with {Account=after.Account with {Orders=after.Account.Orders.Append(after.Account.Orders[0] with {ExternalId="extra"}).ToArray()}};
        if(change=="later date")scope=scope with {Session=scope.Session.AddDays(1)};
        Assert.Throws<InvalidOperationException>(()=>BrokerNativeTrialQualification.ValidateRecovery(before,after,scope,BrokerTrialCarryForwardTests.Calendar(),f.Now));
        Assert.Equal(0,f.Transport.Sent);Assert.Null(f.Store.State);
    }
}
