#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.WSTrade.Accounts;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [Fact]
    public async Task RecoveredDelphiBuyKeepsOpeningPolicyAndCountsOneEntryAcrossTwoFills()
    {
        var db = new Database();
        var order = InterruptedOrder;
        await db.Store.SaveAsync(Initial() with { Orders = [order], Symbols = [new() { Symbol = order.Symbol,
            BindingId = Binding, SessionId = Strategy, StrategyId = Strategy, IsRelevant = true, TradingDate = Now.Date }] });
        var fact = Cancellation with { Status = "PARTIALLY_FILLED" };
        var recovery = new InterruptedBuyRecovery(db.Store, (_,_,_) => Task.FromResult<BrokerOrderFact?>(fact),
            (s,o) => new(s.BindingId, s.PermissionId, o.RequestId, s.StrategyId.ToString("D"), "None", null, 100m) { Progressive = true });
        await recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt);
        await recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt);
        fact = fact with { Status = "FILLED", FilledQuantity = 10, NetValue = 96, GrossValue = 96 };
        await recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt);
        var state = (await db.Store.ReadAsync(Binding))!;
        var held = Assert.Single(state.Positions);
        Assert.Equal(100m, held.OriginalTarget);
        Assert.Equal(9.6m, JsonSerializer.Deserialize<ProgressiveProfitProtectionState>(held.ProtectionJson)!.AverageCost);
        Assert.Equal(1, Assert.Single(state.Symbols).EntriesToday);
        Assert.Equal(96m, state.Account.CommittedPurchaseCost);
        Assert.Empty(state.Orders);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [CandidateProcessFact]
    public async Task PrepareCompactCandidateProcessBoundary()
    {
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_CANDIDATE_REHEARSAL")!, "N");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        string folder = Path.Combine(root!.FullName, "artifacts", "restart-release", token.ToString("N"));
        var db = new RestartRehearsalDatabase(token);
        var store = new SqlTradingStorageDatabase(db.OpenAsync).CreateStore();
        var protector = new TraderVI.WPF.Services.WindowsManualBuyProtector();
        using var journal = new ManualBuyFileJournal(folder, protector);
        var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(folder,"synthetic-grant.dat"), protector);
        var file = new EngineContinuityFile(Path.Combine(folder, "witness.dat"), protector);
        string source = Path.Combine(folder, "synthetic-session.json");
        string build = "CoreSha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
            Path.Combine(root.FullName, "artifacts","compact-candidate","bin","TraderVI.Engine","release","Core.dll"))));
        await PrepareNormalStartupAsync(db, store, grants, journal, file, source, build);
        var saved = JsonSerializer.Deserialize<BrokerManagedSession>(File.ReadAllText(source))!;
        var state = (await store.ReadAsync(saved.BindingId))!;
        var assignment = CompactDelphiAssignment.Capture(saved, grants.Read()!, state, build, saved.SavedAt);
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("CREATE TABLE dbo.RehearsalHold(Code nvarchar(90) NOT NULL)");
        var database = await EngineContinuityRepository.IdentityAsync(c, null!, default);
        var marker = CompactDelphiCheckpoint.From(token, database, assignment, state);
        marker.Write(Path.Combine(folder, $"compact-{state.Account.Version % 2}.dat"), protector);
        var observed = saved.Account.Import.Plan.Observation;
        var date = saved.EffectiveReview.Session;
        File.WriteAllText(Path.Combine(folder, "session-calendar.json"), JsonSerializer.Serialize(
            new global::Core.Trader.DelphiLive.ReviewedTsxCalendarDocument("fixture", "synthetic regular session", date, date, [date])));
        File.WriteAllText(Path.Combine(folder,"broker-copy.json"),JsonSerializer.Serialize(new {
            Binding = saved.BindingId, Account = observed.AccountId, At = saved.SavedAt, observed.Cash, observed.BuyingPower,
            Holdings = saved.Account.Positions.Select(p => new { Security = p.SecurityId, p.Symbol, p.Quantity }) }));
        File.Delete(source); // No session history is available to the restarted executable.
        Assert.NotEmpty(state.Symbols);
        Assert.Single(assignment.Opening);
    }

    private sealed class CandidateProcessFactAttribute : FactAttribute
    {
        public CandidateProcessFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_CANDIDATE_REHEARSAL"), "N", out _))
                Skip = "Requires the isolated candidate binary and a fresh token-marked LocalDB.";
        }
    }
}
