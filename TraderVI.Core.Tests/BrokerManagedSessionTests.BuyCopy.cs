#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Dapper;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    // Opt-in, copied account only. No broker, authentication store, market client or host is constructed.
    [BuyCopyFact]
    public async Task FilledBuyCopyTransition()
    {
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_BUY_COPY")!, "N");
        string phase = Environment.GetEnvironmentVariable("TRADERVI_BUY_COPY_PHASE")!;
        Assert.Contains(phase, new[] { "handoff", "review", "reload" });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        string folder = Path.Combine(root!.FullName, "artifacts", "paper-rehearsal", token.ToString("N"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "copy.json")));
        Assert.Equal("restored", manifest.RootElement.GetProperty("Stage").GetString());
        Assert.Equal(token.ToString("N"), manifest.RootElement.GetProperty("Token").GetString());
        using var ownership = new EngineOwnershipLease(Path.Combine(folder, "buy-copy.lock"));
        var sql = new RestartRehearsalDatabase(token);
        await using var admin = await sql.OpenAsync();
        var binding = await admin.QuerySingleAsync<Guid>("SELECT BindingId FROM dbo.TradingAccountCurrent;");
        var database = await EngineContinuityRepository.IdentityAsync(admin, null!, default);
        var store = new SqlTradingStorageDatabase(sql.OpenAsync).CreateStore();
        var calendar = SelectedSessionCalendar();
        string checkpoints = Path.Combine(folder, "buy-copy-compact");
        var protector = new CompactReceiptProtector();
        var sessions = new BrokerManagedSessionRepository(sql.OpenAsync, calendar, async (c, tx, prior, next, ct) =>
            await CompactManagedWriter.PublishAsync(SqlTradingStorageDatabase.InTransaction(c, tx), prior!, next,
                database, checkpoints, protector, ct,
                (id, buy, filled, cancel) => CompactOpeningProtection.ReadAsync(c, tx, id, buy, filled, cancel)));
        BrokerManagedSession initial;
        await using (var lease = await sessions.AcquireAsync(binding, default)) initial = lease.Current!;
        var book = (await store.ReadAsync(binding))!;
        string baselineFile = Path.Combine(folder, "buy-copy-baseline.json");
        if (phase == "handoff")
        {
            if (!File.Exists(baselineFile))
            {
                var buy = Assert.Single(initial.Orders.Where(o => !o.Reconciled));
                Assert.Equal("BUY", buy.Side);
                var receipt = (await store.ReadOrderAsync(binding, buy.RequestId, default))!;
                Assert.Equal("Filled", receipt.Status);
                Assert.False(receipt.NeedsWork);
                Assert.Equal(receipt.Quantity, receipt.AppliedFilledQuantity);
                Assert.Equal(initial.Account.Positions.Length + 1, book.Positions.Length);
                Assert.Throws<InvalidOperationException>(() => BrokerImplementationReviewWorkflow.RequireSession(initial, calendar, initial.SavedAt));
                var originalSeal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(
                    Path.Combine(folder, "original-compact"), book.Account.Version), protector);
                Assert.True(originalSeal.Matches(binding, originalSeal.Database, book));
                Assert.Equal(initial.RevisionId, originalSeal.ManagedRevision);
                Assert.NotEqual(originalSeal.Database, database);
                Directory.CreateDirectory(checkpoints);
                // Restore changes database identity. This explicitly synthetic copy pairing never leaves this folder.
                (originalSeal with { Database = database }).Write(CompactAccountPreparation.Slot(checkpoints, book.Account.Version), protector);
                await File.WriteAllTextAsync(baselineFile, JsonSerializer.Serialize(new BuyCopyBaseline(initial, book, receipt)));
            }

            var latestId = await admin.QuerySingleAsync<Guid>("SELECT TOP(1) RequestId FROM dbo.BrokerAccountImport WHERE BindingId=@binding ORDER BY Sequence DESC;", new { binding });
            var import = (await BrokerAccountImportRepository.Receipt(admin, latestId))!;
            var now = import.Plan.Observation.CheckedAt;
            var clock = new BuyCopyClock(now);
            var evidence = await new BrokerOngoingRiskEvidenceSource(initial.OngoingManagement!, calendar,
                (_, _, _) => throw new InvalidOperationException("No market access in copy rehearsal."), clock,
                preserveRiskHistory: true).ReadAsync(import, initial.Account.Risk, initial.Orders, default);
            Assert.Empty(evidence.Blocks);
            Assert.Empty(evidence.Continuations);
            var linked = await admin.QuerySingleAsync<BuyCopyBinding>("SELECT BindingId,AccountLabel,BrokerKey,LegacyFingerprint,CreatedUtc,ReviewedBy FROM dbo.BrokerAccountImportBinding WHERE BindingId=@binding;", new { binding });
            var account = BrokerAccountProjector.Project(new(linked.BindingId, linked.AccountLabel, linked.BrokerKey,
                linked.LegacyFingerprint, new DateTimeOffset(DateTime.SpecifyKind(linked.CreatedUtc, DateTimeKind.Utc)), linked.ReviewedBy),
                import, initial.Account.Risk, evidence.Coverage, now);
            var source = new BuyCopySource(account, initial.Strategy.Strategy.ImplementationIdentity);
            var workflow = new BrokerManagedSessionWorkflow(sessions, source, new BuyCopyTransport(), clock);
            // Stop at the boundary after the real account/protection appends, before any strategy input or order.
            await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.TickAsync(binding));
            Assert.True(source.InputBoundaryReached);
        }
        var baseline = JsonSerializer.Deserialize<BuyCopyBaseline>(await File.ReadAllTextAsync(baselineFile))!;
        BrokerManagedSession saved;
        await using (var lease = await sessions.AcquireAsync(binding, default)) saved = lease.Current!;
        book = (await store.ReadAsync(binding))!;
        Assert.All(saved.Orders, o => Assert.True(o.Reconciled));
        Assert.Equal(baseline.Session.EffectiveReview.RequestId, saved.EffectiveReview.RequestId);
        Assert.Equal(BrokerAccountImport.Hash(baseline.Session.OngoingManagement), BrokerAccountImport.Hash(saved.OngoingManagement));
        Assert.Equal(BrokerAccountImport.Hash(baseline.Session.Account.Risk), BrokerAccountImport.Hash(saved.Account.Risk));
        Assert.Equal(baseline.Book.Account.CommittedPurchaseCost, book.Account.CommittedPurchaseCost);
        Assert.Equal(0, book.Account.ReservedPurchaseCost);
        Assert.Equal(baseline.Book.Positions.Length, saved.Account.Positions.Length);
        Assert.All(saved.Account.Positions, holding => Assert.Single(saved.Protection.Where(p => p.PositionId == holding.PositionId)));
        foreach (var before in baseline.Book.Positions)
        {
            var after = book.Positions.Single(p => p.PositionId == before.PositionId);
            Assert.Equal(before.SecurityId, after.SecurityId); Assert.Equal(before.Symbol, after.Symbol);
            Assert.Equal(before.Quantity, after.Quantity); Assert.Equal(before.CostBasis, after.CostBasis);
            Assert.Equal(before.FirstEntryUtc, after.FirstEntryUtc); Assert.Equal(before.LastEntryUtc, after.LastEntryUtc);
            Assert.Equal(before.EntryCount, after.EntryCount); Assert.Equal(before.AddOnCount, after.AddOnCount);
            Assert.Equal(before.ProtectionOwner, after.ProtectionOwner);
            Assert.True(before.ProtectionFloor is null || after.ProtectionFloor >= before.ProtectionFloor, "A saved protection floor decreased or disappeared.");
            Assert.True(before.HighestClose is null || after.HighestClose >= before.HighestClose, "A saved protection high decreased or disappeared.");
            Assert.Equal(before.ExitDecisionId, after.ExitDecisionId); Assert.Equal(before.ExitTriggeredUtc, after.ExitTriggeredUtc);
            Assert.True(book.Settings.OwnsProtection(after));
            BrokerMarketSymbols.RequirePosition(saved, after);
        }
        foreach (var protection in baseline.Session.Protection)
            Assert.Equal(BrokerAccountImport.Hash(protection), BrokerAccountImport.Hash(saved.Protection.Single(p => p.PositionId == protection.PositionId)));
        var original = (await store.ReadOrderAsync(binding, baseline.Receipt.RequestId, default))!;
        Assert.Equal(baseline.Receipt with { Version = 0 }, original with { Version = 0 });
        var fill = Assert.Single(saved.Account.Fills.Where(f => f.TradeId == BrokerManagedStrategy.StableId($"{binding:D}:{baseline.Session.Orders.Single(o => !o.Reconciled).ExternalId}")));
        Assert.Equal(original.AppliedFilledQuantity, fill.Quantity);
        Assert.Equal(baseline.Book.Positions.Single(p => !baseline.Session.Account.Positions.Any(h => h.PositionId == p.PositionId)).FirstEntryUtc, fill.FillUtc);
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(checkpoints, book.Account.Version), protector);
        Assert.True(seal.Matches(binding, database, book)); Assert.Equal(saved.RevisionId, seal.ManagedRevision);

        if (phase == "review")
        {
            // Recover a committed review whose test process lost its final receipt.
            if (saved.Event == "ImplementationReviewed")
                saved = await BrokerManagedSessionRepository.ReadRevisionAsync(admin, binding, saved.Revision - 1, default);
            BrokerImplementationReviewWorkflow.RequireSession(saved, calendar, saved.SavedAt);
            var now = saved.SavedAt;
            var fixture = await OngoingFixture();
            string implementation = TradingRecommendationStorage.ImplementationIdentity;
            // Only source freshness is synthetic: no publication or live approval is created.
            var current = fixture.Existing.Current with { PreparedUtc = now.UtcDateTime,
                Strategy = saved.Strategy with { Strategy = saved.Strategy.Strategy with { ImplementationIdentity = implementation } },
                Evidence = fixture.Existing.Current.Evidence with { PaperAccountId = saved.Strategy.PaperAccountId } };
            var proposal = BrokerImplementationReviewWorkflow.Prepare(saved, current, now, saved.Account, true,
                implementation, "Isolated copy simulation", calendar, now, true, saved.ReusesSaleProceeds);
            if (initial.Event == "ImplementationReviewed")
            {
                var originalReview = initial.ImplementationReview! with { ApprovedAt = default };
                var differences = typeof(BrokerImplementationReview).GetProperties().Where(p =>
                    !Equals(p.GetValue(originalReview), p.GetValue(proposal.Review))).Select(p => p.Name).ToArray();
                Assert.All(differences, name => Assert.Equal("PreviousSessionHash", name));
                Assert.Equal(initial.Strategy.Fingerprint, proposal.Strategy.Fingerprint);
                await File.WriteAllTextAsync(Path.Combine(folder, "buy-review-reconstruction.json"), JsonSerializer.Serialize(new {
                    DifferingFields = differences, StrategyMatches = true
                }));
                // A retry is the original saved request, not a newly prepared approval.
                proposal = proposal with { Review = originalReview, Strategy = initial.Strategy };
            }
            var grants = new HostGrants { Value = seal.Assignment.Grant with { Enabled = true } };
            var review = new BrokerImplementationReviewWorkflow(sessions, new BuyCopySource(saved.Account, implementation), grants, calendar, new BuyCopyClock(now));
            await Assert.ThrowsAsync<InvalidOperationException>(() => review.ApproveAsync(proposal));
            grants.Value = grants.Value with { Enabled = false };
            var reviewed = await review.ApproveAsync(proposal);
            Assert.Equal(reviewed.RevisionId, (await review.ApproveAsync(proposal)).RevisionId);
            var updated = (await store.ReadAsync(binding))!;
            Assert.Equal(RestartAccountRehearsal.Positions(saved, current: book.Positions).OrderBy(p => p.PositionId).ToArray(),
                updated.Positions.OrderBy(p => p.PositionId).Select(p => p with { Version = 0 }).ToArray());
            Assert.Equal(book.Settings.FinancialPermissionId, updated.Settings.FinancialPermissionId);
            Assert.Equal(implementation, updated.Settings.ImplementationIdentity);
            var reviewedSeal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(checkpoints, updated.Account.Version), protector);
            Assert.False(reviewedSeal.Assignment.Grant.Enabled);
            Assert.False(grants.Value.Enabled);
            Assert.Equal(reviewed.RevisionId, reviewedSeal.ManagedRevision);
            Assert.True(reviewedSeal.Matches(binding, database, updated));
        }
        if (phase == "reload")
        {
            Assert.Equal("ImplementationReviewed", saved.Event);
            Assert.False(seal.Assignment.Grant.Enabled);
            Assert.Equal(TradingRecommendationStorage.ImplementationIdentity, book.Settings.ImplementationIdentity);
            var previous = await BrokerManagedSessionRepository.ReadArchiveRevisionAsync(admin, binding, saved.Revision - 1, default);
            var current = await BrokerManagedSessionRepository.ReadArchiveRevisionAsync(admin, binding, saved.Revision, default);
            var historical = await BrokerManagedSessionRepository.HistoricalEvidenceAsync(admin, previous, current, saved.Revision, default);
            BrokerManagedSessionRepository.ValidateNext(previous.State, current.State, calendar, historical);
        }
        await File.WriteAllTextAsync(Path.Combine(folder, $"buy-copy-{phase}-passed.json"), JsonSerializer.Serialize(new {
            Phase = phase, CompletedUtc = DateTimeOffset.UtcNow, Positions = book.Positions.Length,
            OriginalFillPreserved = true, PriorProtectionPreserved = true, FinancialPermissionPreserved = true,
            BrokerClients = 0, OrderSubmissions = 0, SyntheticRestoredIdentity = true, SyntheticReviewSource = phase != "handoff"
        }));
    }

    private sealed record BuyCopyBaseline(BrokerManagedSession Session, TradingCurrentState Book, TradingOrder Receipt);
    private sealed record BuyCopyBinding(Guid BindingId, string AccountLabel, string BrokerKey, string LegacyFingerprint, DateTime CreatedUtc, string ReviewedBy);
    private sealed class BuyCopyClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class BuyCopyTransport : IBrokerManagedOrderTransport
    {
        public Task<IBrokerManagedOrderPreflight> PrepareAsync(BrokerStrategyOrder order, BrokerAccountProjection account, CancellationToken ct) =>
            throw new InvalidOperationException("Order transport is forbidden in the copy rehearsal.");
    }
    private sealed class BuyCopySource(BrokerAccountProjection account, string implementation) : IBrokerManagedAccountSource
    {
        public string ImplementationIdentity => implementation;
        internal bool InputBoundaryReached;
        public Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct) => Task.FromResult(account);
        public Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection _, CancellationToken ct) => throw new InvalidOperationException("No broker access.");
        public Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct)
        { InputBoundaryReached = true; throw new OperationCanceledException("Copy rehearsal stops before evaluation."); }
        public Task PreserveRiskHoldsAsync(BrokerAccountProjection _, TradingRecommendationMemory memory, CancellationToken ct) => throw new InvalidOperationException("No risk mutation.");
        public Task<string> ResolveSecurityAsync(string symbol, CancellationToken ct) => throw new InvalidOperationException("No security search.");
    }
    private sealed class BuyCopyFactAttribute : FactAttribute
    {
        public BuyCopyFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRADERVI_BUY_COPY"))) Skip = "Requires explicitly authorized isolated filled-buy copy.";
        }
    }
}
