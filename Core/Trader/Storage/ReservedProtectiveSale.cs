#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

// Only the creator of a committed reservation can enter this call. The caller retains the
// grant fence. Manual orders share the order gate and every transmission uses the native attempt journal.
internal sealed class ReservedProtectiveSale(IManualSellBroker broker, WealthsimpleTfsaAccount account,
    Func<string, CancellationToken, Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>>> findSecurity,
    ManualBuyFileJournal journal, SemaphoreSlim orderGate, ITsxSessionCalendar calendar, TimeProvider clock,
    Func<Guid, CancellationToken, Task<IManualBuySafetyLease>> protect)
{
    internal async Task SendAsync(TradingCurrentState state, TradingPositionCurrent position, TradingOrder order,
        string identity, CancellationToken ct)
    {
        if (!await orderGate.WaitAsync(0, ct)) throw new InvalidOperationException("Another order currently owns the account.");
        try
        {
            var revision = broker.SessionRevision;
            if (!broker.IsConnected || !state.Account.ExecutionEnabled || state.Account.Phase != "Active" ||
                !state.Settings.SellAllowed || state.Settings.PermissionId != order.PermissionId ||
                state.Account.BindingId != order.BindingId || order.Side != "SELL" ||
                order.PositionId != position.PositionId || order.DecisionId != position.ExitDecisionId ||
                order.Quantity != position.Quantity || order.Quantity != position.ReservedSellQuantity ||
                order.ReservedQuantity != order.Quantity || order.SecurityId != position.SecurityId || order.Symbol != position.Symbol ||
                !state.Orders.Contains(order) || !state.Positions.Contains(position) || position.ExitTriggeredUtc == null)
                throw new InvalidOperationException("The original protective decision and reserved shares disagree.");
            await using var safety = await protect(order.BindingId, ct);
            if (safety.BlockReason != null) throw new InvalidOperationException(safety.BlockReason);
            var instruments = await findSecurity(order.Symbol, ct);
            var security = instruments.Status == WealthsimpleReadStatus.Success
                ? instruments.Data?.SingleOrDefault(s => s.Id == order.SecurityId && s.SourceIdentity == identity) : null;
            if (security == null || !broker.IsConnected || revision != broker.SessionRevision)
                throw new InvalidOperationException("The approved account or broker security could not be verified.");
            var now = clock.GetUtcNow();
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
            if (!calendar.IsRegularSession(date)) throw new InvalidOperationException("The regular trading session is closed.");
            var bounds = calendar.GetSessionBounds(date);
            if (now < bounds.OpenUtc || now >= bounds.CloseUtc || state.Settings.ApprovedUtc > now.UtcDateTime || state.Settings.ExpiresUtc <= now.UtcDateTime)
                throw new InvalidOperationException("The regular trading session or sale permission has ended.");
            journal.RequireClearForRestartSell(identity, account.Id, order.SecurityId, position.PositionId, position.FirstEntryUtc, order.RequestId);
            var intent = new ManualSellIntent(order.RequestId, identity, account.Id, order.SecurityId, account.DisplayName,
                order.Symbol, security.Exchange, order.Quantity, state.Account.BrokerObservedUtc ?? position.FirstEntryUtc, null, now)
                { ProtectiveSale = new(order.DecisionId, position.PositionId, position.ExitTriggeredUtc.Value) };
            var record = new ManualSellRecord(intent, null, []);
            journal.SaveSell(record);
            WealthsimpleSubmissionStatus result;
            try { result = (await broker.SubmitManualSellAsync(new(account, security, order.Quantity, order.RequestId), ct)).Status; }
            catch (Exception error) when (error is System.IO.IOException or System.Net.Http.HttpRequestException or OperationCanceledException)
            { result = WealthsimpleSubmissionStatus.OutcomeUnknown; }
            journal.SaveSell(record with { Submission = result });
        }
        finally { orderGate.Release(); }
    }
}
