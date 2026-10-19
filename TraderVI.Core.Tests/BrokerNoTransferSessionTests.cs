#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerNoTransferSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Binding = Guid.NewGuid(), Baseline = Guid.NewGuid();
    private static BrokerAccountReconciliation Anchor() => new("fixture", "account", Now, Now, 320, 320, 320, [], [], [], []);
    private static BrokerNoTransferSessionScope Scope() => new(Guid.NewGuid(), Binding, Baseline, new(2026,9,23), Now, "operator fixture", Anchor());
    private static BrokerAccountRiskState State() => new(Baseline, Binding, "TFSA", null, new(2026,9,23),
        new(320,BrokerRiskReferenceSource.SetupObservation,Guid.NewGuid(),Now),
        new(320,BrokerRiskReferenceSource.SetupObservation,Guid.NewGuid(),Now),false,false,null,Now);
    private static BrokerImportReceipt Receipt(BrokerAccountReconciliation value) => new(Guid.NewGuid(),Binding,new string('A',64),Now,
        new("TFSA",BrokerAccountImport.Key(value),new string('A',64),null,value,[],[]));
    private static ITsxSessionCalendar Calendar() => new ReviewedTsxSessionCalendar(new(
        "fixture", "offline", new(2026,9,23), new(2026,9,23), [new(2026,9,23)]));
    private sealed class Clock(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow()=>value; }

    [Fact]
    public async Task CashOnlyScopeHasExplicitConditionalCoverageAndCannotAdvanceReferences()
    {
        var source = new BrokerNoTransferRiskEvidenceSource(Scope(),Calendar(),new Clock(Now));
        var result = await source.ReadAsync(Receipt(Anchor()),State(),[],CancellationToken.None);
        Assert.Empty(result.Blocks); Assert.Empty(result.Continuations);
        Assert.Equal(BrokerNoTransferSessionScope.Contract,result.Coverage!.Contract);
        Assert.Equal(Now,result.Coverage.From); Assert.Equal(Now,result.Coverage.Through);
    }

    [Theory]
    [InlineData(319)] [InlineData(321)]
    public async Task AnyUnexplainedCashChangeStopsNewBuys(decimal cash)
    {
        var result=await new BrokerNoTransferRiskEvidenceSource(Scope(),Calendar(),new Clock(Now))
            .ReadAsync(Receipt(Anchor() with {Cash=cash}),State(),[],CancellationToken.None);
        Assert.Null(result.Coverage); Assert.Contains(result.Blocks,b=>b.Contains("Cash no longer equals"));
    }

    [Fact]
    public async Task OnlyOwnedConfirmedNetCostsCanExplainCash()
    {
        var request=Guid.NewGuid(); var order=new BrokerStrategyOrder(request,Guid.NewGuid(),null,"security","FIX","BUY",10,10,"Initial",null,Now);
        var fact=new BrokerOrderFact(order.ExternalId,"security","FIX","BUY","FILLED","CAD","EQUITY",10,10,100,100.25m,.25m,0,Now.AddSeconds(-10),Now);
        var observed=Anchor() with {Cash=219.75m,Orders=[fact]};
        var source=new BrokerNoTransferRiskEvidenceSource(Scope(),Calendar(),new Clock(Now));
        Assert.Empty((await source.ReadAsync(Receipt(observed),State(),[order],CancellationToken.None)).Blocks);
        Assert.Null((await source.ReadAsync(Receipt(observed),State(),[],CancellationToken.None)).Coverage);
        Assert.Null((await source.ReadAsync(Receipt(observed),State(),[order with {Quantity=11}],CancellationToken.None)).Coverage);
    }

    [Fact]
    public async Task ScopeExpiresWithoutReleasingHoldsOrClaimingANewDay()
    {
        var state=State() with {DailyLossHold=true,CapitalReviewRequired=true};
        var result=await new BrokerNoTransferRiskEvidenceSource(Scope(),Calendar(),new Clock(Now.AddDays(1)))
            .ReadAsync(Receipt(Anchor()),state,[],CancellationToken.None);
        Assert.Null(result.Coverage); Assert.Empty(result.Continuations); Assert.NotEmpty(result.Blocks);
        Assert.True(state.DailyLossHold); Assert.True(state.CapitalReviewRequired);
    }

    [Fact]
    public void ScopeCannotBeCreatedFromUnsoldHoldingsOrInventedAccountValue()
    {
        Assert.Throws<InvalidOperationException>(()=>(Scope() with {CashOnlyAnchor=Anchor() with {AccountValue=321}}).Validate(Calendar()));
        Assert.Throws<InvalidOperationException>(()=>(Scope() with {CashOnlyAnchor=Anchor() with {
            Holdings=[new("security","FIX",10,10,100,100,Now,0)]}}).Validate(Calendar()));
    }

    [Fact]
    public async Task ConflictingOrderIdentitiesStopCoverageWithoutSelectingAnArbitraryRecord()
    {
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security", "FIX", "BUY", 1, 10, "Initial", null, Now);
        var result = await new BrokerNoTransferRiskEvidenceSource(Scope(), Calendar(), new Clock(Now))
            .ReadAsync(Receipt(Anchor()), State(), [order, order], CancellationToken.None);
        Assert.Null(result.Coverage); Assert.NotEmpty(result.Blocks);
        Assert.Throws<InvalidOperationException>(() => (Scope() with { ApprovedAt = Now.AddSeconds(-1) }).Validate(Calendar()));
    }
}
