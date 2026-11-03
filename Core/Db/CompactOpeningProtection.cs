#nullable enable
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

// A build changes the strategy version ID even when the operator retains the same
// financial assignment. Recover a missing per-position owner only from its original buy.
internal static class CompactOpeningProtection
{
    internal delegate Task<BrokerManagedSession?> ReadIntent(Guid binding, BrokerStrategyOrder buy,
        DateTimeOffset filledAt, CancellationToken ct);

    internal static async Task<BrokerManagedSession?> ReadAsync(SqlConnection connection, SqlTransaction? transaction,
        Guid binding, BrokerStrategyOrder buy, DateTimeOffset filledAt, CancellationToken ct)
    {
        var revisions = (await connection.QueryAsync<long>(new CommandDefinition("""
            SELECT TOP (2) Revision FROM dbo.BrokerManagedSessionRevision
            WHERE BindingId=@binding AND SavedAt>=@created AND SavedAt<=@filledAt
              AND JSON_VALUE(SnapshotJson,'$.Event')='OrderIntentReserved' ORDER BY Revision;
            """, new { binding, created = buy.CreatedAt, filledAt }, transaction, cancellationToken: ct))).ToArray();
        return revisions.Length == 1
            ? await BrokerManagedSessionRepository.ReadRevisionAsync(connection, binding, revisions[0], ct, transaction)
            : null;
    }

    internal static async Task<TradingApprovedSettings> RestoreAsync(BrokerManagedSession saved, TradingCurrentState book,
        ReadIntent? read, CancellationToken ct)
    {
        var settings = book.Settings;
        if (read == null) return settings;
        var config = JsonNode.Parse(settings.ConfigurationJson)!.AsObject();
        bool changed = false;
        foreach (var position in book.Positions.Where(p => !settings.OwnsProtection(p)))
        {
            var held = saved.Account.Positions.Single(p => p.PositionId == position.PositionId);
            var protection = saved.Protection.Single(p => p.PositionId == held.PositionId);
            var buy = saved.Orders.SingleOrDefault(o => o.ExternalId == held.FirstBuyOrderId);
            if (held.LegacyPositionId != null || buy is not { Side: "BUY", Kind: "Initial" or "Reentry",
                PositionId: null, Reconciled: true, Latest: { Status: "FILLED" or "POSTED" } fact } ||
                fact.Side != "BUY" || fact.ExternalId != buy.ExternalId || fact.SecurityId != held.SecurityId ||
                buy.SecurityId != held.SecurityId || fact.LastFilledAt != held.EntryCompletedAt ||
                fact.FilledQuantity != buy.Quantity || fact.SubmittedQuantity != buy.Quantity ||
                held.PositionId != BrokerManagedStrategy.StableId($"{saved.BindingId:D}:{held.SecurityId}:{held.FirstBuyOrderId}") ||
                position.SecurityId != held.SecurityId || position.FirstEntryUtc != held.EntryCompletedAt.UtcDateTime ||
                protection.Owner != position.ProtectionOwner || protection.EstablishedAt != held.EntryCompletedAt ||
                protection.State.LiveProtection?.PositionId != held.PositionId)
                continue;
            var fillId = BrokerManagedStrategy.StableId($"{saved.BindingId:D}:{buy.ExternalId}");
            if (!saved.Account.Fills.Any(f => f.TradeId == fillId && f.PositionId == held.PositionId &&
                f.Side == "BUY" && f.Quantity == buy.Quantity && f.FillUtc == held.EntryCompletedAt.UtcDateTime))
                continue;

            var intent = await read(saved.BindingId, buy, held.EntryCompletedAt, ct);
            if (intent == null || intent.Event != "OrderIntentReserved" || intent.BindingId != saved.BindingId ||
                intent.SessionId != saved.SessionId || intent.Revision >= saved.Revision ||
                intent.EffectiveReview.RequestId != settings.FinancialPermissionId ||
                intent.EffectiveReview.BaselineId != settings.BaselineId ||
                intent.SavedAt < buy.CreatedAt || intent.SavedAt > held.EntryCompletedAt ||
                intent.Strategy.VersionId.ToString("D") != protection.Owner ||
                intent.Orders.LastOrDefault() != (buy with { Latest = null, Submission = null, Reconciled = false }))
                continue;
            BrokerOctober21Continuation.RequireSameDefinition(intent.Strategy, saved.Strategy);
            var key = "Held_" + held.PositionId.ToString("N");
            if (config.ContainsKey(key)) throw new InvalidOperationException("Conflicting saved protection owner.");
            config[key] = protection.Owner;
            changed = true;
        }
        return changed ? settings with { ConfigurationJson = config.ToJsonString() } : settings;
    }
}
