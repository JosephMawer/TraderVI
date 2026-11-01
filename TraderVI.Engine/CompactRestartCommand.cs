using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

// Deliberately constructed before the host lease, broker client, schedules and execution journal.
internal static class CompactRestartCommand
{
    private sealed record Packet(string Manifest, string ContentHash, Guid RevisionId, Dictionary<string,string> Contents);

    public static async Task<int> RunAsync(string mode, Guid token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var db = new RestartRehearsalDatabase(token);
            string folder = Path.GetFullPath(Path.Combine("artifacts", "restart-release", token.ToString("N")));
            var grant = new BrokerExecutionHostGrantFileStore(Path.Combine(folder, "grant.dat"),
                new WindowsManualBuyProtector()).Read() ?? throw new InvalidDataException("Missing copied host grant.");
            if (mode == "--stage-compact-rehearsal")
            {
                var (saved, hash) = ReadSource(folder);
                string installedBuild = "CoreSha256:" + Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine("TraderVI.WPF", "bin", "Release", "net10.0-windows", "Core.dll"))));
                await RestartAccountRehearsal.StageAsync(db, saved, grant, installedBuild, hash);
                Console.WriteLine("Settled account staged with execution prohibited.");
                return 0;
            }
            var readClock = Stopwatch.StartNew();
            var state = await new SqlTradingStorageDatabase(db.OpenAsync).CreateStore().ReadAsync(grant.BindingId)
                ?? throw new InvalidDataException("Missing staged account.");
            double compactReadMs = readClock.Elapsed.TotalMilliseconds;
            var config = JsonSerializer.Deserialize<JsonElement>(state.Settings.ConfigurationJson);
            if (state.Settings.ScopeCode != BrokerOngoingManagement.Contract || state.Settings.BuyAllowed || state.Settings.SellAllowed ||
                state.Account.AssignmentId != grant.SessionId || state.Account.ExecutionEnabled != grant.Enabled ||
                state.Settings.ImplementationIdentity != grant.ImplementationIdentity ||
                config.GetProperty("StrategyFingerprint").GetString() != grant.StrategyFingerprint ||
                config.GetProperty("RestoreAfterRestart").GetBoolean() != grant.RestoreAfterRestart)
                throw new InvalidDataException("Staged permission differs from copied grant.");
            bool candidateMatchesGrant = grant.ImplementationIdentity == BrokerManagedSessionComposition.ImplementationIdentity;
            double startupMs = clock.Elapsed.TotalMilliseconds;
            if (mode == "--compare-compact-rehearsal")
            {
                var (saved, hash) = ReadSource(folder); // Outside the timed restart.
                var marker = state.Checkpoints.Single();
                var review = saved.EffectiveReview;
                bool permission = state.Settings.PermissionId == review.RequestId && state.Settings.SourceId == saved.Strategy.PaperAccountId &&
                    state.Settings.StrategyId == saved.Strategy.VersionId && state.Settings.BaselineId == review.BaselineId &&
                    state.Settings.PurchaseCeiling == review.PurchaseCeiling && state.Account.NewBuysStopped == saved.NewBuysStopped;
                bool positions = state.Positions.Length == saved.Account.Positions.Length && saved.Account.Positions.All(h =>
                    state.Positions.Any(p => p.PositionId == h.PositionId && p.SecurityId == h.SecurityId && p.Symbol == h.Symbol &&
                        p.Quantity == h.Quantity && p.CostBasis == h.CostBasis));
                bool protection = state.Positions.All(p =>
                {
                    var source = saved.Protection.Single(s => s.PositionId == p.PositionId);
                    var live = source.State.LiveProtection!;
                    return p.ProtectionOwner == source.Owner && p.ProtectionStage == live.Stage.ToString() &&
                        p.ExitDecisionId == source.FirstExit?.DecisionId && p.ExitReason == source.FirstExit?.Reason &&
                        p.ExitTriggeredUtc == source.FirstExit?.TriggeredUtc &&
                        p.ProtectionFloor == new[] { source.CarriedFloor, live.FloorPrice, live.Progressive?.StopPrice }.Max() &&
                        (live.Progressive == null ? p.ProtectionJson == "{}" :
                            JsonSerializer.Deserialize<Core.Trader.ProgressiveProfitProtectionState>(p.ProtectionJson) == live.Progressive);
                });
                var observation = saved.Account.Import.Plan.Observation;
                bool cash = state.Account.ObservedCash == observation.Cash && state.Account.ObservedBuyingPower == observation.BuyingPower &&
                    state.Account.BrokerObservedUtc == observation.SnapshotAt.UtcDateTime &&
                    state.Account.ReservedPurchaseCost == 0 && state.Account.CommittedPurchaseCost == saved.CommittedPurchaseCost;
                var sourceRisk = saved.Account.Risk;
                bool risk = state.Account.SessionReference == sourceRisk.SessionReference.Value &&
                    state.Account.DrawdownReference == sourceRisk.DrawdownReference.Value &&
                    state.Account.DailyLossHold == (sourceRisk.DailyLossHold || saved.Memory.DailyLossHold) &&
                    state.Account.CapitalReviewRequired == (sourceRisk.CapitalReviewRequired || saved.Memory.CapitalReviewRequired) &&
                    state.Account.RiskThroughUtc == sourceRisk.Through.UtcDateTime;
                bool unfinished = state.Orders.Select(o => o.RequestId).ToHashSet()
                    .SetEquals(saved.Orders.Where(o => !o.Reconciled).Select(o => o.RequestId));
                Console.WriteLine(JsonSerializer.Serialize(new { permission, positions, protection, cash, risk, unfinished }));
                if (!permission || !positions || !protection || !cash || !risk || !unfinished || marker.OperationHash != hash ||
                    marker.OperationId != saved.RevisionId) return 2;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { startupMs, compactReadMs, candidateMatchesGrant, dispatchEnabled = false }));
            return 0;
        }
        catch (Exception error)
        {
            // Validation messages may contain identifiers. Emit only the error category and safe field name.
            string? field = error.Message.StartsWith("Value cannot be stored without precision loss: ", StringComparison.Ordinal)
                ? error.Message.Split(':')[1].Trim() : null;
            Console.Error.WriteLine(JsonSerializer.Serialize(new { rehearsalFailed = error.GetType().Name, field }));
            return 1;
        }
    }

    private static (BrokerManagedSession Session, string Hash) ReadSource(string folder)
    {
        var packet = JsonSerializer.Deserialize<Packet>(File.ReadAllText(Path.Combine(folder, "packet.json")))
            ?? throw new InvalidDataException("Missing capture.");
        if (EngineStrategySettings.Hash(packet.Manifest) != packet.ContentHash)
            throw new InvalidDataException("Captured manifest checksum failed.");
        var saved = JsonSerializer.Deserialize<BrokerManagedSession>(EngineArchiveCodec.Restore(packet.Manifest, packet.Contents),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing saved account.");
        if (saved.RevisionId != packet.RevisionId) throw new InvalidDataException("Captured revision mismatch.");
        saved.Strategy.Validate();
        return (saved, packet.ContentHash);
    }
}
