#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("valid")][InlineData("revision")][InlineData("request")][InlineData("quantity")]
    [InlineData("cost")][InlineData("security")][InlineData("symbol")][InlineData("risk")]
    [InlineData("position")][InlineData("listing")][InlineData("stale")][InlineData("permission")]
    public async Task FilledBuyMaintenancePreservesOriginalOrRejectsMismatch(string scenario)
    {
        var (f, prior, projected, buy, db) = await NewMappedBuy();
        var book = (await db.Store.ReadAsync(prior.BindingId))!;
        var original = (await db.Store.ReadOrderAsync(prior.BindingId, buy.RequestId, default))!;
        var fact = buy.Latest! with { Symbol = buy.Symbol };
        var account = projected.Account;
        account = account with { Import = account.Import with { Plan = account.Import.Plan with {
            Observation = account.Import.Plan.Observation with {
                Cash = prior.Account.Import.Plan.Observation.Cash - original.AppliedNetValue,
                BuyingPower = prior.Account.Import.Plan.Observation.BuyingPower - original.AppliedNetValue } } } };
        Guid revision = prior.RevisionId, request = buy.RequestId;
        if (scenario == "revision") revision = Guid.NewGuid();
        if (scenario == "request") request = Guid.NewGuid();
        if (scenario == "quantity") original = original with { Quantity = original.Quantity + 1 };
        if (scenario == "cost") fact = fact with { NetValue = fact.NetValue + 1 };
        if (scenario == "security") fact = fact with { SecurityId = "other" };
        if (scenario == "symbol") fact = fact with { Symbol = "OTHER" };
        if (scenario == "risk") account = account with { Risk = account.Risk with { DailyLossHold = true } };
        if (scenario == "position") account = account with { Positions = [account.Positions.Single() with { PositionId = Guid.NewGuid() }] };
        if (scenario == "listing") account = account with { Import = account.Import with { Plan = account.Import.Plan with {
            Observation = account.Import.Plan.Observation with { Instruments = [] } } } };
        if (scenario == "stale") f.Now = f.Now.AddMinutes(1);
        if (scenario == "permission") book = book with { Settings = book.Settings with { PermissionId = Guid.NewGuid() } };
        var before = DelphiLiveLedgerJson.Serialize(await db.Store.ReadAsync(prior.BindingId));
        BrokerManagedSession Prepare() => SettledBuyTransition.Prepare(prior, book, original, fact, account, revision, request, f.Now);
        if (scenario != "valid")
        {
            Assert.ThrowsAny<Exception>(() => Prepare());
            Assert.Equal(before, DelphiLiveLedgerJson.Serialize(await db.Store.ReadAsync(prior.BindingId)));
            return;
        }
        var next = Prepare();
        BrokerManagedSessionRepository.ValidateNext(prior, next, SelectedSessionCalendar());
        var write = CompactManagedPublication.Prepare(prior, next, book);
        await db.Store.SaveAsync(write);
        Assert.True((await db.Store.SaveAsync(write)).AlreadyCommitted);
        var after = (await db.Store.ReadAsync(prior.BindingId))!;
        Assert.Equal(book.Account.CommittedPurchaseCost, after.Account.CommittedPurchaseCost);
        Assert.Equal(book.Positions.Single().FirstEntryUtc, after.Positions.Single().FirstEntryUtc);
        Assert.Equal(book.Positions.Single().CostBasis, after.Positions.Single().CostBasis);
        Assert.Equal(prior.EffectiveReview.RequestId, next.EffectiveReview.RequestId);
        Assert.Equal(prior.Memory, next.Memory);
        Assert.True(next.Orders.Single().Reconciled);
        Assert.Single(next.Protection);
        Assert.Equal(original with { Version = 0 }, (await db.Store.ReadOrderAsync(prior.BindingId, request, default))! with { Version = 0 });
    }
}
