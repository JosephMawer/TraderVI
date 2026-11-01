#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Core.Trader.Storage;

public static class TradingStorageValidation
{
    public const int BenchmarkPayloadBudget = 512 * 1024; // Measurement target, never a trading gate.
    public static int PayloadBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value).Length;

    public static void Validate(TradingStorageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.BindingId == Guid.Empty || row.Version < 0) Fail("Missing account or invalid version.");
        var table = TradingTable.For(row.GetType());
        foreach (var column in table.Columns)
        {
            var value = row.GetType().GetProperty(column.Name)!.GetValue(row);
            if (value is null) { if (!column.Nullable) Fail("Missing " + column.Name); continue; }
            if (value is Guid id && id == Guid.Empty) Fail("Empty " + column.Name);
            if (value is DateTime date)
            {
                if (date == default || (column.SqlType == "date" ? date.TimeOfDay != TimeSpan.Zero : date.Kind != DateTimeKind.Utc))
                    Fail("Invalid date/UTC boundary: " + column.Name);
            }
            if (value is string text)
            {
                int start = column.SqlType.IndexOf('(');
                int limit = int.Parse(column.SqlType[(start + 1)..^1]);
                if (string.IsNullOrWhiteSpace(text) || text.Length > limit) Fail("Invalid bounded text: " + column.Name);
                if (column.Name.EndsWith("Json", StringComparison.Ordinal)) ValidateFlatJson(text);
                if (column.Name == "Symbol" && text != text.Trim().ToUpperInvariant()) Fail("Symbol must be canonical uppercase.");
            }
            if (value is decimal amount && (decimal.Abs(amount) >= 10_000_000_000_000m || decimal.Round(amount, 6) != amount))
                Fail("Value cannot be stored without precision loss: " + column.Name);
        }
        switch (row)
        {
            case TradingAccountCurrent a:
                if (a.Phase is not ("Transition" or "Active" or "Paused" or "NeedsAttention") ||
                    a.CommittedPurchaseCost < 0 || a.ReservedPurchaseCost < 0) Fail("Invalid current account.");
                break;
            case TradingApprovedSettings a:
                if (a.PurchaseCeiling < 0 || a.ExpiresUtc < a.ApprovedUtc) Fail("Invalid approval.");
                break;
            case TradingPositionCurrent p:
                if (p.Quantity < 0 || p.ReservedSellQuantity < 0 || p.ReservedSellQuantity > p.Quantity ||
                    p.IsOpen != (p.Quantity > 0) || p.CostBasis < 0 || p.EntryCount < 0 || p.AddOnCount < 0 ||
                    p.LastEntryUtc < p.FirstEntryUtc ||
                    (p.ExitDecisionId.HasValue != p.ExitTriggeredUtc.HasValue) ||
                    (p.ExitDecisionId.HasValue != (p.ExitReason is not null))) Fail("Invalid current position.");
                break;
            case TradingSymbolCurrent s:
                if (s.EntriesToday < 0 || s.SignalExpiresUtc < s.ConfirmationStartedUtc) Fail("Invalid symbol progress.");
                break;
            case TradingOrder o:
                if (o.Side is not ("BUY" or "SELL") || o.Quantity <= 0 || o.ReferencePrice < 0 ||
                    o.Status is not ("Prepared" or "Submitted" or "Unknown" or "Filled" or "Rejected" or "Cancelled") ||
                    o.LatestFilledQuantity < 0 || o.LatestFilledQuantity > o.Quantity || o.AppliedFilledQuantity < 0 ||
                    o.AppliedFilledQuantity > o.LatestFilledQuantity || o.AppliedNetValue < 0 ||
                    o.LatestNetValue < o.AppliedNetValue || o.ReservedQuantity < 0 || o.ReservedQuantity > o.Quantity ||
                    o.ReservedCash < 0 || (o.Status == "Filled" && o.LatestFilledQuantity != o.Quantity) ||
                    (o.Status == "Rejected" && o.LatestFilledQuantity != 0) ||
                    (!o.NeedsWork && (!IsTerminal(o.Status) || o.AppliedFilledQuantity != o.LatestFilledQuantity ||
                        o.AppliedNetValue != o.LatestNetValue || o.ReservedQuantity != 0 || o.ReservedCash != 0)))
                    Fail("Invalid order accounting/reservation state.");
                break;
            case TradingCheckpoint c:
                if (c.Slot is not (0 or 1) || c.Slot != c.Version % 2 || c.OperationHash.Length != 64 ||
                    c.OperationHash.Any(ch => !char.IsAsciiHexDigitUpper(ch))) Fail("Invalid checkpoint marker.");
                break;
        }
    }

    internal static bool Same(TradingStorageRow a, TradingStorageRow b) =>
        Equals(a with { Version = 0 }, b with { Version = 0 });

    internal static void ValidateTransition(TradingStorageRow before, TradingStorageRow after)
    {
        if (before.BindingId != after.BindingId || before.GetType() != after.GetType() ||
            !Equals(TradingTable.For(before.GetType()).Key(before), TradingTable.For(after.GetType()).Key(after)))
            Fail("Row identity changed.");
        if (before is TradingApprovedSettings && !Same(before, after)) Fail("An approval is immutable; use a new approval identity.");
        if (before is TradingPositionCurrent p && after is TradingPositionCurrent next &&
            (p.SecurityId != next.SecurityId || p.Symbol != next.Symbol || p.FirstEntryUtc != next.FirstEntryUtc))
            Fail("Position cycle identity changed.");
        if (before is TradingPositionCurrent { IsOpen: false } && after is TradingPositionCurrent { IsOpen: true })
            Fail("A closed position cycle cannot be reopened by a repeated fill.");
        if (before is TradingOrder old && after is TradingOrder updated)
        {
            var permitted = old with
            {
                Version = updated.Version, BrokerOrderId = updated.BrokerOrderId, Status = updated.Status,
                LatestFilledQuantity = updated.LatestFilledQuantity, AppliedFilledQuantity = updated.AppliedFilledQuantity,
                LatestNetValue = updated.LatestNetValue, AppliedNetValue = updated.AppliedNetValue,
                ReservedQuantity = updated.ReservedQuantity, ReservedCash = updated.ReservedCash,
                NeedsWork = updated.NeedsWork, LastCheckedUtc = updated.LastCheckedUtc
            };
            if (permitted != updated || (old.BrokerOrderId is not null && old.BrokerOrderId != updated.BrokerOrderId) ||
                updated.LatestFilledQuantity < old.LatestFilledQuantity || updated.AppliedFilledQuantity < old.AppliedFilledQuantity ||
                updated.LatestNetValue < old.LatestNetValue || updated.AppliedNetValue < old.AppliedNetValue ||
                (IsTerminal(old.Status) && old.Status != updated.Status) || (!old.NeedsWork && !Same(old, updated)))
                Fail("Original order changed or cumulative evidence regressed; reconcile the conflict explicitly.");
        }
    }

    public static void ValidateFlatJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 2 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) Fail("Configuration must be a flat object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (names.Count >= 64 || property.Name.Length is 0 or > 64 || !names.Add(property.Name) ||
                property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object ||
                (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length > 256))
                Fail("Only bounded scalar configuration is allowed; history and nested snapshots are prohibited.");
        }
    }
    internal static bool IsTerminal(string status) => status is "Filled" or "Rejected" or "Cancelled";
    private static void Fail(string message) => throw new InvalidOperationException(message);
}

/// <summary>Prepare a cumulative fill delta. Persist its order, position and account effects together.</summary>
public sealed record TradingFillApplication(TradingOrder Order, int QuantityDelta, decimal NetValueDelta)
{
    public static TradingFillApplication Prepare(TradingOrder prior, int cumulativeQuantity, decimal cumulativeNetValue)
    {
        TradingStorageValidation.Validate(prior);
        if (cumulativeQuantity < prior.LatestFilledQuantity || cumulativeNetValue < prior.LatestNetValue)
            throw new InvalidOperationException("Broker cumulative facts regressed.");
        var next = prior with { LatestFilledQuantity = cumulativeQuantity, LatestNetValue = cumulativeNetValue,
            AppliedFilledQuantity = cumulativeQuantity, AppliedNetValue = cumulativeNetValue };
        TradingStorageValidation.Validate(next);
        TradingStorageValidation.ValidateTransition(prior, next);
        return new(next, cumulativeQuantity - prior.AppliedFilledQuantity, cumulativeNetValue - prior.AppliedNetValue);
    }
}
