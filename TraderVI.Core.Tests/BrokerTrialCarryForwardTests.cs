#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerTrialCarryForwardTests
{
    internal static readonly DateTimeOffset OriginalAt = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 14, 0, 0, TimeSpan.Zero);
    internal static ITsxSessionCalendar Calendar() => new ReviewedTsxSessionCalendar(new("fixture", "offline",
        new(2026,10,19), new(2026,10,21), [new(2026,10,19),new(2026,10,20),new(2026,10,21)]));
    internal static (BrokerAccountRiskBaseline Baseline, BrokerImportReceipt Import, BrokerAccountRiskState Risk,
        BrokerAccountReconciliation Current) Facts()
    {
        var orders = new[] {
            new BrokerOrderFact("original-buy","security","FIX","BUY","FILLED","CAD","EQUITY",1,1,10,10,0,0,OriginalAt.AddHours(-2),OriginalAt.AddHours(-2)),
            new BrokerOrderFact("original-sale","security","FIX","SELL","FILLED","CAD","EQUITY",1,1,10,10,0,0,OriginalAt.AddHours(-1),OriginalAt.AddHours(-1)) };
        var anchor = new BrokerAccountReconciliation("fixture", "account", OriginalAt, OriginalAt, 1000,1000,1000,orders,[],[],[]);
        var binding=Guid.NewGuid(); var id=Guid.NewGuid(); var reference=new BrokerRiskReference(1000,
            BrokerRiskReferenceSource.SetupObservation,Guid.NewGuid(),OriginalAt);
        var baseline=new BrokerAccountRiskBaseline(Guid.NewGuid(),binding,"TFSA",id,OriginalAt,
            new(2026,10,19),reference,reference,null,null,TradingRecommendationMemory.Empty) { ObservationAt=OriginalAt };
        var import=new BrokerImportReceipt(id,binding,new string('A',64),OriginalAt,
            new("TFSA",BrokerAccountImport.Key(anchor),new string('A',64),null,anchor,[],[]));
        return (baseline,import,BrokerAccountRiskContinuation.Start(baseline),anchor with { SnapshotAt=Now,CheckedAt=Now });
    }
    [Fact]
    public void CarryPreservesEveryOriginalReferenceAndDoesNotInventClosingHistory()
    {
        var (baseline,import,risk,current)=Facts();
        var request=BrokerTrialCarryForward.Prepare(baseline,import,risk,current,"fixture",true,Calendar(),Now);
        var state=BrokerAccountRiskContinuation.Apply(risk,request,Calendar(),Now);
        Assert.Equal(new DateOnly(2026,10,20),state.TradingDate);
        Assert.Equal(risk.SessionReference,state.SessionReference); Assert.Equal(risk.DrawdownReference,state.DrawdownReference);
        Assert.Null(state.LastCompletedSession); Assert.Equal(current.SnapshotAt,state.Through);
        Assert.Equal(risk.DailyLossHold,state.DailyLossHold); Assert.Equal(risk.CapitalReviewRequired,state.CapitalReviewRequired);
        Assert.Equal(BrokerTrialCarryForward.RequestId(baseline.BaselineId),state.RevisionId);
        var reopened=JsonSerializer.Deserialize<BrokerRiskContinuationRequest>(JsonSerializer.Serialize(request))!;
        Assert.Equal(request.RequestHash,reopened.RequestHash);
        Assert.Equal(state.TrialCarry!.Fingerprint,reopened.TrialCarry!.Fingerprint);
        Assert.Throws<InvalidOperationException>(()=>BrokerAccountRiskContinuation.Apply(state,request with {RequestId=Guid.NewGuid(),PreviousRevisionId=state.RevisionId},Calendar(),Now));
    }
    [Theory]
    [InlineData("no confirmation")] [InlineData("cash")] [InlineData("account value")]
    [InlineData("foreign account")] [InlineData("holding")] [InlineData("pending")]
    [InlineData("added order")] [InlineData("missing order")] [InlineData("changed fill")]
    [InlineData("daily hold")] [InlineData("capital hold")] [InlineData("advanced state")]
    [InlineData("changed reference")] [InlineData("stale")] [InlineData("wrong import")]
    public void ChangedFinancialEvidenceCannotBecomeACarry(string change)
    {
        var (baseline,import,risk,current)=Facts(); bool confirmed=true;
        if(change=="no confirmation")confirmed=false;
        if(change=="cash")current=current with { Cash=1001,AccountValue=1001 };
        if(change=="account value")current=current with { AccountValue=1001 };
        if(change=="foreign account")current=current with { AccountId="other" };
        if(change=="holding")current=current with { Holdings=[new("security","FIX",1,1,10,10,Now,0)] };
        if(change=="pending")current=current with { Orders=current.Orders.Select(o=>o with {Status="NEW"}).ToArray() };
        if(change=="added order")current=current with { Orders=current.Orders.Append(current.Orders[0] with {ExternalId="extra"}).ToArray() };
        if(change=="missing order")current=current with { Orders=[] };
        if(change=="changed fill")current=current with { Orders=current.Orders.Select(o=>o with {NetValue=11}).ToArray() };
        if(change=="daily hold")risk=risk with { DailyLossHold=true };
        if(change=="capital hold")risk=risk with { CapitalReviewRequired=true };
        if(change=="advanced state")risk=risk with { RevisionId=Guid.NewGuid() };
        if(change=="changed reference")baseline=baseline with { SessionReference=baseline.SessionReference with {Value=999} };
        if(change=="stale")current=current with { SnapshotAt=Now.AddSeconds(-31),CheckedAt=Now.AddSeconds(-31) };
        if(change=="wrong import")import=import with {RequestId=Guid.NewGuid()};
        Assert.Throws<InvalidOperationException>(()=>BrokerTrialCarryForward.Prepare(baseline,import,risk,current,"fixture",confirmed,Calendar(),Now));
    }
    [Fact]
    public void CarryHasOneReviewedDateAndCannotSupplyOrdinaryBoundaryOrTransferAuthority()
    {
        var (baseline,import,risk,current)=Facts();
        var request=BrokerTrialCarryForward.Prepare(baseline,import,risk,current,"fixture",true,Calendar(),Now);
        foreach(var at in new[]{Now.AddDays(-1),Now.AddDays(1),new DateTimeOffset(2026,10,20,20,0,0,TimeSpan.Zero)})
            Assert.Throws<InvalidOperationException>(()=>BrokerTrialCarryForward.Prepare(baseline,import,risk,
                current with {SnapshotAt=at,CheckedAt=at},"fixture",true,Calendar(),at));
        var state=BrokerAccountRiskContinuation.Apply(risk,request,Calendar(),Now);
        // An ordinary event cannot silently convert this exception into production history.
        Assert.Throws<InvalidOperationException>(()=>BrokerAccountRiskContinuation.Apply(state,
            new(Guid.NewGuid(),state.BaselineId,state.RevisionId,BrokerRiskContinuationKind.Closing,
                new(Guid.NewGuid(),state.BindingId,state.TradingDate,BrokerRiskBoundaryKind.Closing,
                    BrokerAccountSessionValuation.SessionClosing,Now,Now,1000,new string('A',64)),
                new(Guid.NewGuid(),state.BindingId,state.Through,Now,Now,new string('A',64)),null),Calendar(),Now));
    }
    [Fact]
    public void CarriedReferencesCannotBeProjectedAsProductionRiskPermission()
    {
        var (baseline,import,risk,current)=Facts();
        risk=BrokerAccountRiskContinuation.Apply(risk,BrokerTrialCarryForward.Prepare(baseline,import,risk,current,"fixture",true,Calendar(),Now),Calendar(),Now);
        var binding=new BrokerImportBinding(baseline.BindingId,"TFSA",BrokerAccountImport.Key(current),new string('A',64),OriginalAt,"fixture");
        var saved=import with {Plan=import.Plan with {Observation=current}};
        var projection=BrokerAccountProjector.Project(binding,saved,risk,
            new(baseline.BindingId,risk.Through,Now,Now,"Unrelated production source",new string('A',64)),Now);
        Assert.Contains(projection.NewRiskBlocks,b=>b.Contains("cannot qualify production"));
    }
}
