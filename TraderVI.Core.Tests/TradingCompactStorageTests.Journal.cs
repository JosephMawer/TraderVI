#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    private static readonly ManualBuyQuote JournalQuote = new(10m, 9m, Now, "OPEN", Now.AddHours(-1), Now.AddHours(1), Now);

    [Theory]
    [InlineData("original", true)]
    [InlineData("missing", true)]
    [InlineData("wrong account", false)]
    [InlineData("wrong security", false)]
    [InlineData("wrong quantity", false)]
    [InlineData("wrong side", false)]
    [InlineData("unreadable", false)]
    public async Task JournalBuyLookupPreservesTheReservationAndNeverReplacesTheRequest(string scenario, bool expectBrokerRead)
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        var order = InterruptedOrder;
        var intent = new ManualBuyIntent(order.RequestId, "identity", "account", order.SecurityId, "Fixture", order.Symbol,
            "XTSE", order.Quantity, new(order.Symbol, "Fixture", "Buy"), 10m, JournalQuote, 100m, Now);
        using (var writer = new ManualBuyFileJournal(folder.Path, protector))
        {
            // Decoy for the same holding. Looking up by symbol or most recent order would select it.
            writer.Save(new(intent with { RequestId = Guid.NewGuid(), CreatedAt = Now.AddMinutes(1) }, WealthsimpleSubmissionStatus.Acknowledged, []));
            switch (scenario)
            {
                case "wrong side":
                    writer.SaveSell(new(new(order.RequestId, "identity", "account", order.SecurityId, "Fixture", order.Symbol,
                        "XTSE", 1, Now, JournalQuote, Now), WealthsimpleSubmissionStatus.OutcomeUnknown, []));
                    break;
                case "missing": break;
                default:
                    intent = scenario switch { "wrong account" => intent with { AccountId = "another" },
                        "wrong security" => intent with { SecurityId = "another" }, "wrong quantity" => intent with { Quantity = 1 }, _ => intent };
                    writer.Save(new(intent, WealthsimpleSubmissionStatus.OutcomeUnknown, []));
                    break;
            }
        }
        if (scenario == "unreadable") File.WriteAllBytes(Path.Combine(folder.Path, order.RequestId.ToString("N") + ".order"), new byte[64]);
        File.WriteAllBytes(Path.Combine(folder.Path, Guid.NewGuid().ToString("N") + ".order"), new byte[64]);
        using var copy = new ManualBuyFileJournal(folder.Path, protector);
        var journal = new RestartBuyJournal(copy, Binding, "identity", "account");
        if (scenario == "original") Assert.Equal(order.RequestId, journal.FindOriginal(order)!.Intent.RequestId);
        if (scenario == "missing") Assert.Null(journal.FindOriginal(order));
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Orders = [order] });
        var lookups = new List<string>();
        var restart = new OfflineAccountRestart(db.Store, (_, id, _) =>
        {
            lookups.Add(id);
            throw new TimeoutException();
        }, (id, _) => Task.FromResult((id, BrokerBook(RecoveryAt))), (_, _) => throw new Exception("Unexpected send"),
            _ => throw new Exception("Unexpected sale"), SavedOpeningTerms, new RecoveryClock(), journal);
        for (int i = 0; i < 2; i++)
        {
            var pass = await restart.RunAsync(Binding);
            AssertAllowance(pass.Current, 0, 100);
            Assert.Equal(order.RequestId, Assert.Single(pass.Current.Orders).RequestId);
            Assert.Contains(pass.Notices, n => n.OriginalId == order.RequestId);
        }
        Assert.Equal(expectBrokerRead ? 2 : 0, lookups.Count);
        Assert.All(lookups, id => Assert.Equal("order-" + order.RequestId.ToString("D"), id));
    }

    [Fact]
    public void JournalScopeSurvivesReopenAndReadsOnlyRelevantSalePointers()
    {
        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        var original = Guid.NewGuid();
        var position = Guid.NewGuid();
        var manual = new ManualSellRecord(new(Guid.NewGuid(), "identity", "acct", "sec", "Test", "TEST", "XTSE",
            10, Now, JournalQuote, Now), WealthsimpleSubmissionStatus.OutcomeUnknown, []);
        var unrelated = manual with { Intent = manual.Intent with { RequestId = Guid.NewGuid(), SecurityId = "other" } };
        void Check(ManualBuyFileJournal j) => j.RequireClearForRestartSell("identity", "acct", "sec", position, Now, original);
        using (var writer = new ManualBuyFileJournal(dir.Path, protector))
        {
            writer.SaveSell(unrelated);
            writer.PrepareRestartSellScopes("identity", "acct", new ManualBuyFileJournal.RestartHolding("sec", position, Now));
            writer.SaveSell(manual);
            var path = Assert.Single(Directory.GetFiles(dir.Path, "*.scope"));
            var indexed = File.ReadAllBytes(path);
            for (int i = 0; i < 25; i++)
            {
                writer.SaveSell(manual);
                writer.SaveSell(unrelated with { Intent = unrelated.Intent with { RequestId = Guid.NewGuid() } });
            }
            Assert.Equal(indexed, File.ReadAllBytes(path));
        }
        // An unrelated unreadable envelope would fail a full directory scan.
        File.WriteAllBytes(Path.Combine(dir.Path, unrelated.Intent.RequestId.ToString("N") + ".sell"), [1,2,3]);
        using var reopened = new ManualBuyFileJournal(dir.Path, protector);
        Assert.Throws<InvalidOperationException>(() => Check(reopened));
        reopened.SaveSell(manual with { Submission = WealthsimpleSubmissionStatus.Rejected });
        Check(reopened);
        // Mimic loss after pointer publication, before its receipt is visible. Admission must fail closed.
        reopened.SaveSell(manual);
        File.Delete(Path.Combine(dir.Path, manual.Intent.RequestId.ToString("N") + ".sell"));
        Assert.Throws<InvalidOperationException>(() => Check(reopened));
        Assert.Throws<InvalidOperationException>(() => reopened.RequireClearForRestartSell("identity", "acct", "sec", Guid.NewGuid(), Now, original));
    }

    private sealed record CapturedPacket(string Manifest, string ContentHash, Guid RevisionId, Dictionary<string,string> Contents);

    [Fact]
    public void ReplacingWatchedHoldingsDropsOldPointersAndRefusesAnUnpreparedCycle()
    {
        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        using var journal = new ManualBuyFileJournal(dir.Path, new ManualMarketSellWorkflowTests.Protector());
        var oldCycle = new ManualBuyFileJournal.RestartHolding("sec", Guid.NewGuid(), Now);
        var nextCycle = oldCycle with { Position = Guid.NewGuid(), EntryUtc = Now.AddDays(1) };
        var original = Guid.NewGuid();
        void Check(ManualBuyFileJournal.RestartHolding h) =>
            journal.RequireClearForRestartSell("identity", "acct", h.Security, h.Position, h.EntryUtc, original);
        Assert.Throws<InvalidOperationException>(() => Check(oldCycle));
        journal.PrepareRestartSellScopes("identity", "acct", oldCycle);
        Check(oldCycle);
        journal.PrepareRestartSellScopes("identity", "acct", nextCycle);
        Assert.Throws<InvalidOperationException>(() => Check(oldCycle));
        Check(nextCycle);
        journal.PrepareRestartSellScopes("identity", "acct");
        Assert.Throws<InvalidOperationException>(() => Check(nextCycle));
        Assert.Single(Directory.GetFiles(dir.Path, "*.scope"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [CopiedJournalFact]
    public void IsolatedInstalledJournalMatchesSavedBuysAndScopesManualSales()
    {
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_JOURNAL_COPY")!, "N");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        var source = Path.Combine(root!.FullName, "artifacts", "restart-release", token.ToString("N"));
        var packet = JsonSerializer.Deserialize<CapturedPacket>(File.ReadAllText(Path.Combine(source, "packet.json")))!;
        Assert.True(EngineStrategySettings.Hash(packet.Manifest) == packet.ContentHash);
        var session = JsonSerializer.Deserialize<BrokerManagedSession>(EngineArchiveCodec.Restore(packet.Manifest, packet.Contents))!;
        Assert.True(session.RevisionId == packet.RevisionId);
        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        foreach (var ext in new[] { "*.order", "*.sell" })
            foreach (var path in Directory.EnumerateFiles(source, ext)) File.Copy(path, Path.Combine(dir.Path, Path.GetFileName(path)));
        var protector = new TraderVI.WPF.Services.WindowsManualBuyProtector();
        var account = session.Account.Import.Plan.Observation;
        var held = session.Account.Positions.Take(2).ToArray();
        Assert.True(held.Length == 2 && held[0].SecurityId != held[1].SecurityId);
        var saleRequest = Guid.NewGuid();
        void Check(ManualBuyFileJournal j, int i) => j.RequireClearForRestartSell(account.IdentityId, account.AccountId,
            held[i].SecurityId, held[i].PositionId, held[i].EntryCompletedAt.UtcDateTime, saleRequest);
        using (var journal = new ManualBuyFileJournal(dir.Path, protector))
        {
            journal.PrepareRestartSellScopes(account.IdentityId, account.AccountId, held.Select(h => new ManualBuyFileJournal.RestartHolding(h.SecurityId, h.PositionId, h.EntryCompletedAt.UtcDateTime)).ToArray());
            Check(journal, 0);
            Check(journal, 1);
            journal.SaveSell(new(new(Guid.NewGuid(), account.IdentityId, account.AccountId, held[0].SecurityId, "Fixture",
                held[0].Symbol, "XTSE", 1, Now, JournalQuote, Now), WealthsimpleSubmissionStatus.OutcomeUnknown, []));
        }
        using var restarted = new ManualBuyFileJournal(dir.Path, protector);
        var buys = session.Orders.Where(o => o.Side == "BUY").ToArray();
        Assert.NotEmpty(buys);
        var adapter = new RestartBuyJournal(restarted, session.BindingId, account.IdentityId, account.AccountId);
        foreach (var o in buys)
        {
            var receipt = adapter.FindOriginal(new TradingOrder { BindingId = session.BindingId, RequestId = o.RequestId,
                Side = o.Side, Symbol = o.Symbol, SecurityId = o.SecurityId, Quantity = o.Quantity });
            Assert.True(receipt != null && receipt.Intent.RequestId == o.RequestId);
        }
        Assert.Throws<InvalidOperationException>(() => Check(restarted, 0));
        Check(restarted, 1);
        output.WriteLine("All saved managed buys matched their original copied receipts. A simulated unresolved manual sale blocked only its own holding after reopening the copy.");
    }

    private sealed class CopiedJournalFactAttribute : FactAttribute
    {
        public CopiedJournalFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_JOURNAL_COPY"), "N", out _))
                Skip = "Requires an explicitly isolated encrypted journal copy and saved account capture.";
        }
    }
}
