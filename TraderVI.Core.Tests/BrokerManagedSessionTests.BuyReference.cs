#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task OngoingBuyReferenceSurvivesRejectionAndRestartWithoutChasingOrDuplicateOrders()
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        var f = x.F; f.Strategy = saved.Strategy; f.Risk = saved.Account.Risk;
        f.Now = new(2026,10,23,14,1,0,TimeSpan.Zero);
        f.TransformAccount = account =>
        {
            var observation = saved.Account.Import.Plan.Observation with { SnapshotAt = f.Now, CheckedAt = f.Now };
            var import = account.Import with { Plan = account.Import.Plan with { Observation = observation } };
            return BrokerAccountProjector.Project(f.Binding, import, f.Risk, f.Coverage(), f.Now);
        };
        OfferContinuationSignal(x.Existing.Original.Existing);
        f.Transport.ReadReference = () => new(105.02m,105m,f.Now,"OPEN",new DateTimeOffset(f.Now.UtcDateTime.Date.AddHours(13.5),TimeSpan.Zero),new DateTimeOffset(f.Now.UtcDateTime.Date.AddHours(20),TimeSpan.Zero),f.Now);
        f.Transport.BeforePrepare = () => throw new InvalidOperationException("Synthetic rejected attempt");
        var error = await Record.ExceptionAsync(() => f.Workflow().TickAsync(f.Binding.BindingId));
        Assert.True(error is InvalidOperationException, $"{f.Store.State!.Event}: {f.Store.State.LatestDecision?.Summary}; {string.Join(';', f.Store.State.Blocks)}");
        Assert.True(f.Store.State!.BuyReferences is not null, error?.ToString());
        var reference = Assert.Single(f.Store.State!.BuyReferences!);
        Assert.Empty(f.Store.State.Orders); Assert.Equal(1, f.Transport.ReferenceReads);
        f.Store.Restart(); f.Now = f.Now.AddSeconds(15);
        f.Transport.ReadReference = () => throw new InvalidOperationException("Must not replace the fixed ask");
        f.Transport.BeforePrepare = null;
        var accepted = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(accepted.Orders.Length == 1, $"{accepted.Event}: {accepted.LatestDecision?.Summary}; {string.Join(';', accepted.Blocks)}");
        var order = Assert.Single(accepted.Orders);
        Assert.Equal(reference, order.BuyReference); Assert.Equal(reference.MaximumAsk, order.ReferencePrice);
        Assert.True(order.Quantity * order.ReferencePrice <= order.PositionTarget);
        Assert.True(order.Quantity * order.ReferencePrice <= accepted.EffectiveReview.PurchaseCeiling);
        Assert.Equal(1, f.Transport.ReferenceReads); Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart(); await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent);
        Assert.Throws<InvalidOperationException>(() => BrokerBuyReference.ValidateAppend(accepted,
            accepted with { BuyReferences = [reference with { Quote = reference.Quote with { Ask = 110 } }] }));
    }
}
