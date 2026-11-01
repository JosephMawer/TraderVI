#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Dapper;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>One-time comparison fixture for a settled, ongoing assignment. Only a marked LocalDB is accepted.</summary>
public static class RestartAccountRehearsal
{
    public static async Task StageAsync(RestartRehearsalDatabase rehearsal, BrokerManagedSession saved,
        BrokerExecutionHostGrant grant, string installedBuild, string manifestHash, CancellationToken ct = default)
    {
        // Do not guess which permission owned an old pending request, or invent a manual-fill attribution.
        if (saved.OngoingManagement?.ApprovedAt is not { } approved || saved.EffectiveReview.NoTransferScope is not null ||
            !saved.Blocks.IsEmpty || !saved.Account.NewRiskBlocks.IsEmpty || saved.TrialOrderReviewRequired ||
            saved.Orders.Any(o => !o.Reconciled || o.Latest is not { IsTerminal: true }))
            throw new InvalidOperationException("Saved assignment requires a mapping outside the settled rehearsal.");
        grant.Validate();
        if (grant.ImplementationIdentity != installedBuild) throw new InvalidOperationException("Installed grant build mismatch.");
        BrokerExecutionHost.RequireMatchingAssignment(grant, saved, installedBuild, DateTimeOffset.UtcNow);
        var review = saved.EffectiveReview;
        var risk = saved.Account.Risk;
        var observation = saved.Account.Import.Plan.Observation;
        var account = new TradingAccountCurrent
        {
            BindingId = saved.BindingId, AssignmentId = saved.SessionId, PermissionId = review.RequestId,
            BaselineId = review.BaselineId, StrategyId = saved.Strategy.VersionId, Phase = saved.Phase.ToString(),
            ExecutionEnabled = grant.Enabled, NewBuysStopped = saved.NewBuysStopped, TradingDate = risk.TradingDate.ToDateTime(TimeOnly.MinValue),
            RiskEvidenceId = risk.RevisionId ?? risk.BaselineId, RiskThroughUtc = risk.Through.UtcDateTime,
            DailyLossHold = risk.DailyLossHold || saved.Memory.DailyLossHold,
            CapitalReviewRequired = risk.CapitalReviewRequired || saved.Memory.CapitalReviewRequired,
            HighestClosingValue = saved.Account.Account.HighestClosingValue, SessionReference = risk.SessionReference.Value,
            DrawdownReference = risk.DrawdownReference.Value, ObservedCash = observation.Cash,
            ObservedBuyingPower = observation.BuyingPower, BrokerObservedUtc = observation.SnapshotAt.UtcDateTime,
            CommittedPurchaseCost = saved.CommittedPurchaseCost
        };
        var settings = new TradingApprovedSettings
        {
            BindingId = saved.BindingId, AssignmentId = saved.SessionId, PermissionId = review.RequestId, BaselineId = review.BaselineId,
            StrategyId = account.StrategyId, SourceId = saved.Strategy.PaperAccountId, ImplementationIdentity = installedBuild,
            ApprovedUtc = approved.UtcDateTime, PurchaseCeiling = review.PurchaseCeiling, ScopeCode = BrokerOngoingManagement.Contract,
            // Rehearsal never grants either action. Source permission and host switch are compared separately.
            BuyAllowed = false, SellAllowed = false, ConfigurationJson = JsonSerializer.Serialize(new
                { StrategyFingerprint = saved.Strategy.Fingerprint, grant.RestoreAfterRestart })
        };
        var positions = saved.Account.Positions.Select(h =>
        {
            var p = saved.Protection.Single(x => x.PositionId == h.PositionId);
            var live = p.State.LiveProtection ?? throw new InvalidOperationException("Unsupported protection family.");
            if (live.PositionId != h.PositionId || ((p.State.ExitReason != null || live.Progressive?.ExitTriggered == true) && p.FirstExit == null))
                throw new InvalidOperationException("Protection identity or original exit receipt requires review.");
            var entries = saved.Account.Fills.Where(f => f.PositionId == h.PositionId && f.Side == "BUY").OrderBy(f => f.FillUtc).ToArray();
            return new TradingPositionCurrent
            {
                BindingId = saved.BindingId, PositionId = h.PositionId, SecurityId = h.SecurityId, Symbol = h.Symbol, Quantity = h.Quantity,
                CostBasis = h.CostBasis, IsOpen = true, FirstEntryUtc = h.EntryCompletedAt.UtcDateTime,
                LastEntryUtc = entries.Select(f => f.FillUtc).Append(h.EntryCompletedAt.UtcDateTime).Max(),
                EntryCount = entries.Length, AddOnCount = entries.Count(f => f.RecommendationKind == "AddOn"),
                OriginalTarget = entries.FirstOrDefault()?.PositionTarget, ProtectionOwner = p.Owner,
                ProtectionFloor = new[] { p.CarriedFloor, live.FloorPrice, live.Progressive?.StopPrice }.Max(),
                HighestClose = live.Progressive?.HighestClose ?? live.HighestCompletedFiveMinuteClose,
                ProtectionStage = live.Stage.ToString(), LastProtectionUtc = p.ThroughUtc ?? p.EstablishedAt.UtcDateTime,
                ExitDecisionId = p.FirstExit?.DecisionId, ExitReason = p.FirstExit?.Reason, ExitTriggeredUtc = p.FirstExit?.TriggeredUtc,
                ProtectionJson = live.Progressive is null ? "{}" : JsonSerializer.Serialize(live.Progressive)
            };
        }).ToImmutableArray();
        var marker = new TradingCheckpoint { BindingId = saved.BindingId, Slot = 0, OperationId = saved.RevisionId,
            OperationHash = manifestHash, OperationKind = "SavedAccountComparison", SavedUtc = saved.SavedAt.UtcDateTime };
        var symbols = CompactDelphiAssignment.CurrentSymbols(saved);
        var state = new TradingCurrentState(account, settings, positions, symbols, [], [], [marker]);
        TradingCurrentStore.ValidateCurrent(state, saved.BindingId);
        await using var admin = await rehearsal.OpenAsync(ct);
        var plan = saved.Account.Import.Plan;
        await admin.ExecuteAsync(new CommandDefinition("""
            IF NOT EXISTS(SELECT 1 FROM dbo.BrokerAccountImportBinding WHERE BindingId=@BindingId)
            INSERT dbo.BrokerAccountImportBinding VALUES (@BindingId,N'TFSA',@BrokerKey,@LegacyFingerprint,@SavedAt,N'Rehearsal copy');
            """, new { saved.BindingId, plan.BrokerKey, plan.LegacyFingerprint, SavedAt = saved.SavedAt.UtcDateTime }, cancellationToken: ct));
        ITradingStorageDatabase db = new SqlTradingStorageDatabase(rehearsal.OpenAsync);
        await using var tx = await db.BeginAsync(saved.BindingId, true, ct);
        await tx.PutAsync(settings, true, ct);
        await tx.PutAsync(account, true, ct);
        foreach (var p in positions) await tx.PutAsync(p, true, ct);
        foreach (var symbol in symbols) await tx.PutAsync(symbol, true, ct);
        await tx.PutAsync(marker, true, ct);
        await tx.CommitAsync(ct);
    }
}
