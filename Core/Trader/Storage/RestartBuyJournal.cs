#nullable enable
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.Storage;

// A local intent can corroborate the request. Only a broker observation can book a fill or release cash.
internal sealed class RestartBuyJournal(ManualBuyFileJournal journal, Guid binding, string identity, string account)
{
    internal ManualBuyRecord? FindOriginal(TradingOrder order)
    {
        try
        {
            if (order.BindingId != binding || order.Side != "BUY" || journal.FindSell(order.RequestId) != null)
                throw new InvalidOperationException("The original buy does not match the journal account or side.");
            var buy = journal.FindBuy(order.RequestId);
            // A reservation may precede the journal write. Continue the broker lookup with its original ID.
            if (buy == null) return null;
            var intent = buy.Intent;
            if (intent.IdentityId != identity || intent.AccountId != account || intent.SecurityId != order.SecurityId ||
                intent.Symbol != order.Symbol || intent.Quantity != order.Quantity)
                throw new InvalidOperationException("The original buy and journal receipt disagree. Keep its reservation.");
            return buy;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or CryptographicException or
            UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException("The original buy's journal receipt is unreadable. Keep its reservation.", error);
        }
    }
}
