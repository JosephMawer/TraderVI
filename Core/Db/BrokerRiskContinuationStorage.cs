#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

/// <summary>Stores selected-session evidence once without changing domain hashes or older receipts.</summary>
internal static class BrokerRiskContinuationStorage
{
    internal const int MaximumBytes = 256 * 1024;
    private const string Format = "SelectedSessionRiskReceiptV1";
    private sealed record Stored(string StorageFormat, BrokerRiskContinuationRequest Request,
        BrokerAccountRiskState State, DateTimeOffset SavedAt, string StateHash);

    internal static string Serialize(BrokerRiskContinuationReceipt receipt)
    {
        string json;
        if (receipt.Request.Kind == BrokerRiskContinuationKind.ReviewedSessionContinuation)
        {
            // Request already contains the exact original carry, legacy permissions and selected review.
            // Only omit the identical copies from State; reject any disagreement before writing.
            var stored = new Stored(Format, receipt.Request, BrokerSessionContinuation.Anchor(receipt.State),
                receipt.SavedAt, BrokerAccountImport.Hash(receipt.State));
            var restored = Restore(stored);
            if (BrokerAccountImport.Hash(restored) != BrokerAccountImport.Hash(receipt))
                throw new InvalidOperationException("Account risk evidence cannot be reconstructed exactly; no event was saved.");
            json = Json(stored);
        }
        else json = Json(receipt); // Preserve the representation of all previous event contracts.
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidOperationException("Account risk event exceeds the supported size; no event was saved.");
        return json;
    }

    internal static BrokerRiskContinuationReceipt Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(nameof(Stored.StorageFormat), out _))
            return Read<BrokerRiskContinuationReceipt>(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes ||
            document.RootElement.EnumerateObject().Count() != 5 ||
            document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 5)
            throw new InvalidOperationException("Stored account risk event has an invalid representation.");
        return Restore(Read<Stored>(json));
    }

    private static BrokerRiskContinuationReceipt Restore(Stored stored)
    {
        if (stored.StorageFormat != Format || stored.Request is null || stored.State is null ||
            stored.Request.Kind != BrokerRiskContinuationKind.ReviewedSessionContinuation ||
            stored.Request.SessionContinuation is not { } selected ||
            stored.State.TrialCarry is not null || stored.State.Continuation is not null ||
            stored.State.October22 is not null || stored.State.SessionContinuation is not null ||
            !CompleteTradingStrategy.IsHash(stored.StateHash))
            throw new InvalidOperationException("Stored account risk event has an invalid representation.");
        BrokerAccountRiskContinuation.Validate(stored.Request);
        var state = stored.State with
        {
            TrialCarry = selected.OriginScope.TrialCarry,
            Continuation = selected.OriginScope.Continuation,
            October22 = selected.OriginScope.October22,
            SessionContinuation = selected
        };
        if (BrokerAccountImport.Hash(state) != stored.StateHash)
            throw new InvalidOperationException("Stored account risk evidence does not match its exact state hash.");
        return new(stored.Request, state, stored.SavedAt);
    }
}
