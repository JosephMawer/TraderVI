#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private sealed record MappedBuy(Fixture Fixture, BrokerManagedSession Prior, BrokerManagedSession Next,
        BrokerStrategyOrder Buy, TradingCompactStorageTests.Database Database);

    private static async Task<MappedBuy> NewMappedBuy(bool lostReply = false)
    {
        var x = await OngoingFixture();
        var approved = await x.Workflow().ApproveAsync(x.Review);
        var f = x.F;
        f.Now = new DateTimeOffset(f.Now.UtcDateTime.Date.AddHours(14).AddMinutes(2));
        var buy = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-ABC", "ABC",
            "BUY", 2, 100, "Initial", 200, f.Now.AddMinutes(-3), WealthsimpleSubmissionStatus.Acknowledged);
        var prior = approved with { ActivatedAt = buy.CreatedAt, Phase = BrokerManagedPhase.Active,
            SavedAt = buy.CreatedAt, Orders = [buy], Protection = [], Event = "OrderTransmissionObserved",
            ImplementationReview = new(Guid.NewGuid(), approved.RevisionId, Fixture.Hash, "earlier-build",
                f.ImplementationIdentity, approved.SavedAt, approved.SavedAt, "Offline fixture")
                { RestartPolicy = BrokerImplementationReview.ScopedRestart, PermissionRequestId = approved.EffectiveReview.RequestId } };
        Assert.Empty(prior.Account.Positions);
        var account = RestartAccountRehearsal.Account(prior, true) with { CommittedPurchaseCost = 0 };
        var settings = new TradingApprovedSettings { BindingId = prior.BindingId, AssignmentId = prior.SessionId,
            StrategyId = prior.Strategy.VersionId, PermissionId = prior.EffectiveReview.RequestId,
            BaselineId = prior.EffectiveReview.BaselineId, SourceId = prior.Strategy.PaperAccountId,
            ImplementationIdentity = prior.Strategy.Strategy.ImplementationIdentity, ScopeCode = "Offline mapping fixture",
            ApprovedUtc = approved.SavedAt.UtcDateTime, PurchaseCeiling = prior.EffectiveReview.PurchaseCeiling, SellAllowed = true };
        var receipt = new TradingOrder { BindingId = prior.BindingId, PermissionId = settings.PermissionId,
            RequestId = buy.RequestId, DecisionId = buy.DecisionId, Symbol = buy.Symbol, SecurityId = buy.SecurityId,
            Side = "BUY", Quantity = buy.Quantity, ReferencePrice = buy.ReferencePrice, CreatedUtc = buy.CreatedAt.UtcDateTime,
            Status = "Submitted" };
        var db = new TradingCompactStorageTests.Database();
        await db.Store.SaveAsync(new(Guid.NewGuid(), "OriginalBuy", buy.CreatedAt.UtcDateTime, -1, account)
            { Settings = [settings], Orders = [receipt], Symbols = CompactDelphiAssignment.CurrentSymbols(prior) });
        // Native original-ID reads retain the intent symbol. The complete import uses the listing name.
        var fill = new BrokerOrderFact(buy.ExternalId, buy.SecurityId, buy.Symbol, "BUY", "FILLED", "CAD", "EQUITY",
            2, 2, 200, 200, 0, 0, buy.CreatedAt, f.Now.AddMinutes(-2));
        var recovery = new InterruptedBuyRecovery(db.Store, (_, original, _) =>
        {
            Assert.Equal(buy.ExternalId, original);
            return Task.FromResult<BrokerOrderFact?>(fill);
        }, (_, order) => new(prior.BindingId, settings.PermissionId, order.RequestId,
            prior.Strategy.VersionId.ToString("D"), "None", null, buy.PositionTarget)
            { Progressive = prior.Strategy.Strategy.ReadLivePolicy().UseProgressiveProfitProtection });
        db.LoseCommitReply = lostReply;
        if (lostReply) await Assert.ThrowsAsync<TimeoutException>(() => recovery.RecoverAsync(prior.BindingId, buy.RequestId, f.Now));
        else await recovery.RecoverAsync(prior.BindingId, buy.RequestId, f.Now);
        Assert.Null(await recovery.RecoverAsync(prior.BindingId, buy.RequestId, f.Now));
        f.Risk = prior.Account.Risk;
        f.Facts.Clear();
        f.Facts.AddRange(prior.Account.Import.Plan.Observation.Orders);
        f.Facts.Add(fill with { Symbol = "ABC.TO" });
        var projected = f.Project();
        projected = projected with { Import = projected.Import with { Plan = projected.Import.Plan with {
            Observation = projected.Import.Plan.Observation with {
                Instruments = [new(buy.SecurityId, "ABC.TO", "XTSE", "CAD", "EQUITY")] } } } };
        buy = buy with { Latest = f.Facts.Last(), Reconciled = true };
        projected = projected.WithManagedFillAttribution(prior.BindingId, [buy]);
        var next = prior with { Account = projected, Orders = [buy], Revision = prior.Revision + 1,
            PreviousRevisionId = prior.RevisionId, RevisionId = Guid.NewGuid(), SavedAt = f.Now, Event = "AccountSynchronized" };
        return new(f, prior, next, buy, db);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task NewBuyReconcilesThroughAllConsumerWiringAndProtectionWithoutAliasOrReplay(bool lostReply)
    {
        var (f, prior, next, buy, db) = await NewMappedBuy(lostReply);
        var book = (await db.Store.ReadAsync(prior.BindingId))!;
        var original = Assert.Single(book.Positions);
        Assert.Equal("ABC", original.Symbol);
        Assert.Equal("ABC.TO", Assert.Single(next.Account.Positions).Symbol);
        // Prove this fixture reaches the original failure, with the immutable-cycle guard intact.
        var unadapted = Assert.Single(RestartAccountRehearsal.Positions(next, true));
        Assert.Throws<InvalidOperationException>(() => TradingStorageValidation.ValidateTransition(original, unadapted));
        var publication = CompactManagedPublication.Prepare(prior, next, book);
        db.LoseCommitReply = lostReply;
        if (lostReply) await Assert.ThrowsAsync<TimeoutException>(() => db.Store.SaveAsync(publication));
        else await db.Store.SaveAsync(publication);
        Assert.True((await db.Store.SaveAsync(publication)).AlreadyCommitted);
        book = (await db.Store.ReadAsync(prior.BindingId))!;
        var held = Assert.Single(book.Positions);
        Assert.Equal(original.PositionId, held.PositionId);
        Assert.Equal(original.Symbol, held.Symbol);
        Assert.Equal(original.FirstEntryUtc, held.FirstEntryUtc);
        Assert.Equal(original.ProtectionOwner, held.ProtectionOwner);
        Assert.Equal(original.CostBasis, held.CostBasis);
        Assert.Equal(200m, book.Account.CommittedPurchaseCost);
        Assert.Equal(0m, book.Account.ReservedPurchaseCost);
        Assert.Empty(book.Orders);
        Assert.True(book.Settings.OwnsProtection(held));
        Assert.Equal("ABC", Assert.Single(book.Symbols).Symbol);
        Assert.Equal(1, book.Symbols.Single().EntriesToday);
        var protection = BrokerLiveProtection.Transfer(next.Strategy, next.Account.Positions.Single(), null,
            next.Account.Positions.Single().EntryCompletedAt);
        next = next with { Protection = [protection] };
        next = DelphiLiveLedgerJson.Deserialize<BrokerManagedSession>(DelphiLiveLedgerJson.Serialize(next));
        string saved = DelphiLiveLedgerJson.Serialize(next);
        var observation = EngineRealObservation.From(next);
        var tracked = new DelphiLiveObservedHolding("ABC.TO", "Real", BrokerTradeProjector.TrackedPositionId(
            next.Account.Import.Plan.BrokerKey, buy.ExternalId), false);
        int headReads = 0;
        var resolver = new HoldingMarketSymbolResolver(_ => Task.FromResult<IReadOnlyList<EngineRealObservation>>([observation]),
            (expected, _) => { headReads++; expected.RequireMatches(next); return Task.FromResult(BrokerMarketSymbols.ForTrackedHoldings(next)); });
        var calendar = (ReviewedTsxSessionCalendar)SelectedSessionCalendar();
        var market = new IndependentMarketService(calendar, resolver);
        var consumers = new[] { market.HoldingSource,
            new IndependentPaperService(calendar, () => null, market, resolver).HoldingSource,
            new IndependentResearchService(calendar, () => null, market, resolver).HoldingSource,
            new DelphiLiveDesktopService(null, calendar, resolver).HoldingSource! };
        foreach (var consumer in consumers)
        {
            var resolved = await consumer.ResolveMarketSymbolsAsync([tracked, .. observation.Holdings], ["ABC"], []);
            Assert.All(resolved, h => Assert.Equal("ABC", h.Symbol));
            Assert.Equal(tracked with { Symbol = "ABC" }, resolved[0]);
            Assert.All(resolved, h => Assert.False(h.DelphiLiveMayAct));
        }
        Assert.Equal(4, headReads);
        Assert.Equal(saved, DelphiLiveLedgerJson.Serialize(next));
        var input = MappingInputAt(f.Now.UtcDateTime);
        var holding = new ReportedTradingHolding(held.PositionId, "ABC.TO", 2, 200, input.TradingDate);
        input = input with { CurrentHoldings = [holding], BrokerMarketSymbols = BrokerMarketSymbols.From(next.Account, next.Orders),
            Quotes = [new("ABC.TO", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 105, 104, 106, "TmxQuoteFieldsV1")] };
        var evaluated = TradingRecommendationEngine.Evaluate(input);
        Assert.DoesNotContain("NetAssetValueUnavailable", evaluated.EntryRiskBlocks);
        Assert.DoesNotContain(evaluated.Recommendations, r => r.Action is "Buy" or "Add");
        Assert.Equal(held.PositionId, evaluated.Memory.Symbols["ABC.TO"].PositionId);
        Assert.Equal("ABC", BrokerMarketSymbols.Market(input, "ABC.TO"));
        var exit = TradingRecommendationEngine.Evaluate(input with { PreviousMemory = evaluated.Memory,
            Quotes = [new("ABC.TO", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 94, 94, 95, "TmxQuoteFieldsV1")] });
        Assert.Equal("Sell", Assert.Single(exit.Recommendations).Action);
        Assert.Equal(held.PositionId, Assert.Single(exit.Recommendations).PositionId);
        var triggered = next with { Protection = [protection with {
            FirstExit = new(Guid.NewGuid(), next.EffectiveReview.RequestId, "RiskExit", f.Now.UtcDateTime) }] };
        var latched = RestartSellAdmission.CopyTrigger(triggered, book, held);
        Assert.NotNull(latched.ExitDecisionId);
        Assert.Equal(held.Symbol, BrokerManagedAccountSource.PositionOrderSymbol(triggered, held.PositionId, book));
        Assert.Equal(held.ProtectionOwner, latched.ProtectionOwner);
        Assert.Equal(0, f.Transport.Sent);
    }

    private static TradingRecommendationInput MappingInputAt(DateTime now)
    {
        var input = TradingRecommendationTests.LiveInput();
        var delta = now - input.EvaluatedUtc;
        var date = DateOnly.FromDateTime(now);
        DelphiLiveFiveMinuteSeries Shift(DelphiLiveFiveMinuteSeries s) => new(s.Symbol, date,
            s.SessionOpenUtc + delta, s.OperationalContinuityStartUtc + delta, s.Bars.Select(b => new DelphiLiveFiveMinuteBar(
                b.ObservationId, b.Symbol, date, b.StartUtc + delta, b.EndUtc + delta, b.Open, b.High, b.Low, b.Close,
                b.Volume, b.ReceivedUtc + delta, b.Provider, b.SourceContractVersion, b.Disposition)));
        return input with { EvaluatedUtc = now, TradingDate = date, SessionOpenUtc = input.SessionOpenUtc + delta,
            SessionCloseUtc = input.SessionCloseUtc + delta,
            Account = input.Account! with { TradingDate = date, ReportedUtc = now },
            LiveInputs = input.LiveInputs.Select(o => o with { Input = o.Input with {
                Stock = Shift(o.Input.Stock), Xiu = Shift(o.Input.Xiu), BarEndUtc = o.Input.BarEndUtc + delta,
                EvaluatedUtc = o.Input.EvaluatedUtc + delta, PreviousState = DelphiLiveEvaluationState.Initial(true) } }).ToImmutableArray(),
            Bars = input.Bars.ToImmutableDictionary(p => p.Key, p => p.Value.Select(b => b with {
                StartUtc = b.StartUtc + delta, ReceivedUtc = b.ReceivedUtc + delta }).ToImmutableArray()) };
    }

    [Theory]
    [InlineData("security")][InlineData("position")][InlineData("time")][InlineData("symbol")]
    [InlineData("listing")][InlineData("exchange")][InlineData("duplicate")][InlineData("unmanaged")]
    public async Task BuyHandoffRejectsChangedProofWithoutChangingSavedBook(string fault)
    {
        var (_, prior, next, _, db) = await NewMappedBuy();
        var book = (await db.Store.ReadAsync(prior.BindingId))!;
        string before = DelphiLiveLedgerJson.Serialize(book);
        var holding = next.Account.Positions.Single();
        if (fault == "security") holding = holding with { SecurityId = "other" };
        if (fault == "position") holding = holding with { PositionId = Guid.NewGuid() };
        if (fault == "time") holding = holding with { EntryCompletedAt = holding.EntryCompletedAt.AddSeconds(1) };
        if (fault == "symbol") holding = holding with { Symbol = "OTHER.TO" };
        next = next with { Account = next.Account with { Positions = [holding] } };
        var observation = next.Account.Import.Plan.Observation;
        if (fault == "listing") observation = observation with { Instruments = [] };
        if (fault == "exchange") observation = observation with { Instruments = [observation.Instruments!.Single() with { PrimaryMic = "XTSX" }] };
        if (fault == "duplicate") observation = observation with { Instruments = [observation.Instruments!.Single(), observation.Instruments!.Single()] };
        next = next with { Account = next.Account with { Import = next.Account.Import with {
            Plan = next.Account.Import.Plan with { Observation = observation } } } };
        if (fault == "unmanaged") next = next with { Orders = [] };
        await Assert.ThrowsAnyAsync<Exception>(async () => await db.Store.SaveAsync(CompactManagedPublication.Prepare(prior, next, book)));
        Assert.Equal(before, DelphiLiveLedgerJson.Serialize(await db.Store.ReadAsync(prior.BindingId)));
    }

    private sealed class NoBuySafety : IBrokerManagedBuySafetySource
    {
        public Task<IBrokerManagedBuySafetyLease> AcquireManagedAsync(CancellationToken ct) =>
            throw new InvalidOperationException("This protection rehearsal must never acquire buy authority.");
    }

    public static IEnumerable<object[]> ConsumerMappingFailures() =>
        from consumer in new[] { "Market", "Paper", "Research", "Desktop" }
        from fault in new[] { "missing-head", "changed-head", "wrong-position", "wrong-security", "wrong-exchange", "unmanaged", "alias-conflict" }
        select new object[] { consumer, fault };

    [Theory]
    [MemberData(nameof(ConsumerMappingFailures))]
    public async Task EveryConstructedConsumerRejectsMissingOrConflictingMapping(string consumer, string fault)
    {
        var (_, _, next, buy, _) = await NewMappedBuy();
        var observation = EngineRealObservation.From(next);
        var tracked = new DelphiLiveObservedHolding("ABC.TO", "Real", BrokerTradeProjector.TrackedPositionId(
            next.Account.Import.Plan.BrokerKey, buy.ExternalId), false);
        var readState = next;
        if (fault == "changed-head") readState = next with { Revision = next.Revision + 1 };
        if (fault == "wrong-position") tracked = tracked with { OwnerRecordId = Guid.NewGuid() };
        if (fault == "unmanaged") readState = next with { Orders = [] };
        if (fault is "wrong-security" or "wrong-exchange")
        {
            var listing = next.Account.Import.Plan.Observation.Instruments!.Single();
            listing = fault == "wrong-security" ? listing with { SecurityId = "other" } : listing with { PrimaryMic = "XTSX" };
            readState = next with { Account = next.Account with { Import = next.Account.Import with {
                Plan = next.Account.Import.Plan with { Observation = next.Account.Import.Plan.Observation with { Instruments = [listing] } } } } };
        }
        var resolver = new HoldingMarketSymbolResolver(_ => Task.FromResult<IReadOnlyList<EngineRealObservation>>(
            fault == "missing-head" ? [] : [observation]), (expected, _) => {
                expected.RequireMatches(readState); return Task.FromResult(BrokerMarketSymbols.ForTrackedHoldings(readState)); });
        var calendar = (ReviewedTsxSessionCalendar)SelectedSessionCalendar();
        var market = new IndependentMarketService(calendar, resolver);
        var source = consumer switch {
            "Market" => market.HoldingSource,
            "Paper" => new IndependentPaperService(calendar, () => null, market, resolver).HoldingSource,
            "Research" => new IndependentResearchService(calendar, () => null, market, resolver).HoldingSource,
            _ => new DelphiLiveDesktopService(null, calendar, resolver).HoldingSource!
        };
        await Assert.ThrowsAnyAsync<Exception>(() => source.ResolveMarketSymbolsAsync([tracked], ["ABC"],
            fault == "alias-conflict" ? [new("Broker", "ABC.TO", "ABC"), new("Broker", "ABC.TO", "OTHER")] : []));
    }

    [Fact]
    public async Task OrdinaryWorkflowPublishesNewBuyProtectsItAndPreflightsOriginalCycleSale()
    {
        var (f, prior, reconciled, buy, db) = await NewMappedBuy();
        f.Store.State = prior;
        f.TransformAccount = _ => reconciled.Account;
        f.Strategy = prior.Strategy;
        f.PositionOrderSymbolRead = async (session, id) => BrokerManagedAccountSource.PositionOrderSymbol(session, id,
            (await db.Store.ReadAsync(session.BindingId))!);
        var observation = reconciled.Account.Import.Plan.Observation;
        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        var protector = new HostProtector();
        var identity = new EngineDatabaseIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var grant = new BrokerExecutionHostGrant(prior.BindingId, prior.SessionId, prior.Strategy.Fingerprint,
            f.ImplementationIdentity, f.Now, "Offline fixture", true);
        var assignment = new CompactDelphiAssignment(grant, prior.EffectiveReview.RequestId, prior.Strategy.VersionId,
            prior.Strategy.PaperAccountId, prior.Account.Risk.BaselineId, observation.IdentityId, observation.AccountId, []);
        var initialBook = (await db.Store.ReadAsync(prior.BindingId))!;
        (CompactDelphiCheckpoint.From(prior.BindingId, identity, assignment, initialBook) with { ManagedRevision = prior.RevisionId })
            .Write(CompactAccountPreparation.Slot(dir.Path, initialBook.Account.Version), protector);
        var events = new List<string>();
        f.Store.Publish = (previous, next, ct) => CompactManagedWriter.PublishAsync(db.Store, previous, next,
            identity, dir.Path, protector, ct);
        f.Store.AfterAppend = events.Add;
        decimal bid = 100m;
        f.TransformInput = input => input with {
            BrokerMarketSymbols = BrokerMarketSymbols.From(reconciled.Account, reconciled.Orders),
            BrokerHoldingSecurityIds = ImmutableDictionary<string, string>.Empty.Add("ABC.TO", buy.SecurityId),
            LiveInputs = MappingInputAt(f.Now.UtcDateTime).LiveInputs,
            Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("ABC.TO", [
                new(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-7), f.Now.UtcDateTime.AddMinutes(-1), 5, 100, 100, 100, 100)]) };
        var brokerAccount = new WealthsimpleTfsaAccount(observation.AccountId, "Offline fixture");
        var security = new WealthsimpleCanadianSecurity(buy.SecurityId, "ABC", "XTSE") { SourceIdentity = observation.IdentityId };
        ManualSellContext Context() => new(brokerAccount, security,
            new(brokerAccount, f.Now, new(200, "CAD"), new(observation.Cash!.Value, "CAD"), new(observation.BuyingPower!.Value, "CAD"), null, null, null, null, null,
                [new WealthsimpleHolding("ABC.TO", "Offline stock", "EQUITY", 2, new(200, "CAD"), new(200, "CAD"), null) { SecurityId = buy.SecurityId }]),
            new(brokerAccount, f.Now, []), new(bid + .01m, bid, f.Now, "OPEN", f.Now.AddHours(-1), f.Now.AddHours(3), f.Now));
        var sellBroker = new ManualMarketSellWorkflowTests.Broker(Context());
        var buyBroker = new ManualMarketBuyWorkflowTests.FakeBroker(new(brokerAccount, security,
            sellBroker.Context.Snapshot, sellBroker.Context.PendingOrders, sellBroker.Context.Quote));
        var journal = new ManualMarketSellWorkflowTests.Journal();
        var transport = new BrokerManagedOrderTransport(buyBroker, sellBroker, journal, new NoBuySafety(), brokerAccount,
            new SemaphoreSlim(1, 1), new HostClock(f));
        f.QuoteRead = input => [BrokerHoldingQuote.Validate(input, brokerAccount, "ABC.TO", f.Now.UtcDateTime,
            f.Now.UtcDateTime, Context())];
        var workflow = new BrokerManagedSessionWorkflow(f.Store, f, transport, new HostClock(f));
        var protectedState = await workflow.TickAsync(prior.BindingId);
        Assert.Contains("AccountSynchronized", events);
        Assert.Contains("FilledPositionsProtected", events);
        Assert.Contains("LiveProtectionPrepared", events);
        Assert.Contains("StrategyEvaluated", events);
        Assert.All(protectedState.Orders, o => Assert.True(o.Reconciled));
        Assert.Equal(0, sellBroker.Submissions);
        Assert.Equal(0, buyBroker.Submissions);
        var savedProtection = Assert.Single(protectedState.Protection);
        var book = (await db.Store.ReadAsync(prior.BindingId))!;
        Assert.Equal(savedProtection.PositionId, Assert.Single(book.Positions).PositionId);
        Assert.Equal("ABC", book.Positions.Single().Symbol);
        Assert.NotNull(book.Positions.Single().LastProtectionUtc);
        Assert.Equal("ABC", Assert.Single(book.Symbols).Symbol);
        Assert.Equal(1, book.Symbols.Single().EntriesToday);
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir.Path, book.Account.Version), protector);
        Assert.True(seal.Matches(prior.BindingId, identity, book));
        Assert.Equal(protectedState.RevisionId, seal.ManagedRevision);

        f.Store.Restart();
        bid = 94m;
        sellBroker.Context = Context();
        var sold = await workflow.TickAsync(prior.BindingId);
        var sale = Assert.Single(sold.Orders.Where(o => o.Side == "SELL"));
        Assert.Equal("ABC", sale.Symbol);
        Assert.Equal(savedProtection.PositionId, sale.PositionId);
        Assert.Equal(buy.SecurityId, sale.SecurityId);
        Assert.Equal(2, sale.Quantity);
        Assert.Equal(1, sellBroker.Submissions); // Synthetic client only; never a live broker.
        Assert.Equal(0, buyBroker.Submissions);
        var intent = Assert.Single(journal.ReadSells()).Intent;
        Assert.Equal(sale.RequestId, intent.RequestId);
        Assert.Equal(sale.Symbol, intent.Symbol);
        book = (await db.Store.ReadAsync(prior.BindingId))!;
        Assert.Equal(sale.RequestId, Assert.Single(book.Orders).RequestId);
        Assert.Equal("ABC", book.Positions.Single().Symbol);
        Assert.NotNull(book.Positions.Single().ExitDecisionId);
        Assert.Equal(2, book.Positions.Single().ReservedSellQuantity);
        Assert.Equal(200m, book.Account.CommittedPurchaseCost);
        Assert.Equal(initialBook.Settings, book.Settings);

        // Close and reconcile through the same compact/journal boundaries, then repeat after reload.
        f.Now = f.Now.AddSeconds(15);
        var filled = new BrokerOrderFact(sale.ExternalId, sale.SecurityId, sale.Symbol, "SELL", "FILLED", "CAD", "EQUITY",
            2, 2, 188, 188, 0, 0, sale.CreatedAt, f.Now);
        var closing = OfflineAccountRestart.PrepareSale(book, book.Positions.Single(), book.Orders.Single(), filled, f.Now, null);
        var committing = db.Store.WithCheckpointWriter(updated =>
            (CompactDelphiCheckpoint.From(prior.BindingId, identity, assignment, updated) with { ManagedRevision = sold.RevisionId })
                .Write(CompactAccountPreparation.Slot(dir.Path, updated.Account.Version), protector));
        await committing.SaveAsync(closing);
        book = (await db.Store.ReadAsync(prior.BindingId))!;
        Assert.Empty(book.Positions);
        Assert.Empty(book.Orders);
        Assert.Equal(filled.LastFilledAt!.Value.UtcDateTime, book.Symbols.Single().LastExitUtc);
        sellBroker.State = new("FILLED", 2, 2, new(94, "CAD"), new(188, "CAD"), new(188, "CAD"), new(0, "CAD"),
            new(0, "CAD"), sale.CreatedAt, f.Now, null, "REGULAR", f.Now);
        sellBroker.Inventory = new(0, true, f.Now) { HasPendingOnSecurity = false };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            f.Store.Restart();
            await using var lease = await f.Store.AcquireAsync(prior.BindingId, default);
            await CompactManagedSaleHandoff.ReconcileAsync(lease, new(book, []), db.Store,
                request => journal.ReadSells().SingleOrDefault(r => r.Intent.RequestId == request),
                (_, _) => Task.FromResult<BrokerOrderFact?>(filled), new ManualMarketSellWorkflow(sellBroker, journal, new HostClock(f)),
                observation.IdentityId, observation.AccountId, () => { }, new HostClock(f), default);
        }
        Assert.True(f.Store.State!.Orders.Single(o => o.RequestId == sale.RequestId).Reconciled);
        Assert.Empty(f.Store.State.Account.Positions);
        Assert.Equal(1, sellBroker.Submissions);
        Assert.Single(journal.ReadSells().Single().Observations);
        Assert.Equal(188m, (await db.Store.ReadOrderAsync(prior.BindingId, sale.RequestId, default))!.AppliedNetValue);
    }
}
