#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Runtime;

/// <summary>Private pipe contracts only. Never use these options for saved financial history or hashes.</summary>
internal static class EngineRpcJson
{
    internal sealed record AccountReference(Guid Handle, string Id, string DisplayName);
    private static readonly ConditionalWeakTable<WealthsimpleTfsaAccount, AccountReference> references = new();
    private static readonly object referenceGate = new();

    internal static AccountReference Reference(WealthsimpleTfsaAccount account)
    {
        lock (referenceGate)
            return references.GetValue(account, a => new(Guid.NewGuid(), a.Id, a.DisplayName));
    }

    internal static void SetReference(WealthsimpleTfsaAccount account, AccountReference reference)
    {
        lock (referenceGate) { references.Remove(account); references.Add(account, reference); }
    }

    internal static WealthsimpleTfsaAccount? SelectedAccount(object? payload) => payload switch
    {
        WealthsimpleTfsaAccount account => account,
        EngineInstrumentRequest request => request.Account,
        EnginePrepareBuy request => request.Account,
        EngineReconcile request => request.Account,
        EngineSell request => request.Selection.Account,
        _ => null
    };

    internal static JsonSerializerOptions Options(WealthsimpleTfsaAccount? selected = null,
        Func<AccountReference, WealthsimpleTfsaAccount>? resolve = null) => new()
    {
        Converters = { new AccountConverter(selected, resolve), new SecurityConverter(), new DailyConverter(),
            new HoldingConverter(), new PendingConverter() }
    };

    private sealed class AccountConverter(WealthsimpleTfsaAccount? selected,
        Func<AccountReference, WealthsimpleTfsaAccount>? resolve) : JsonConverter<WealthsimpleTfsaAccount>
    {
        private readonly Dictionary<Guid, WealthsimpleTfsaAccount> seen = [];
        public override WealthsimpleTfsaAccount Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<AccountReference>(ref reader, options) ?? throw new JsonException("Missing account reference.");
            if (wire.Handle == Guid.Empty || string.IsNullOrWhiteSpace(wire.Id) || string.IsNullOrWhiteSpace(wire.DisplayName))
                throw new JsonException("Incomplete engine account reference.");
            if (resolve is not null) return resolve(wire);
            if (selected is not null)
            {
                if (wire != Reference(selected)) throw new JsonException("The engine returned another account selection.");
                return selected;
            }
            if (seen.TryGetValue(wire.Handle, out var existing))
            {
                if (Reference(existing) != wire) throw new JsonException("Conflicting engine account references.");
                return existing;
            }
            var account = new WealthsimpleTfsaAccount(wire.Id, wire.DisplayName);
            SetReference(account, wire); seen.Add(wire.Handle, account); return account;
        }
        public override void Write(Utf8JsonWriter writer, WealthsimpleTfsaAccount value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, Reference(value), options);
    }

    private sealed record SecurityWire(string Id, string Symbol, string Exchange);
    private sealed class SecurityConverter : JsonConverter<WealthsimpleCanadianSecurity>
    {
        public override WealthsimpleCanadianSecurity Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<SecurityWire>(ref reader, options) ?? throw new JsonException("Missing security.");
            if (string.IsNullOrWhiteSpace(wire.Id)) throw new JsonException("Missing instrument identity.");
            // Display evidence has no native-client submission provenance. Only server-owned reviews can submit.
            return new(wire.Id, wire.Symbol, wire.Exchange);
        }
        public override void Write(Utf8JsonWriter writer, WealthsimpleCanadianSecurity value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new SecurityWire(value.Id, value.Symbol, value.Exchange), options);
    }

    private sealed record DailyWire(WealthsimpleTfsaAccount Account, DateTimeOffset RetrievedAt, DateTimeOffset? AsOf,
        DateTimeOffset? ReferenceAt, WealthsimpleMoney? Amount, decimal? Rate);
    private sealed class DailyConverter : JsonConverter<WealthsimpleDailyPerformance>
    {
        public override WealthsimpleDailyPerformance Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<DailyWire>(ref reader, options) ?? throw new JsonException("Missing daily performance.");
            return new(wire.Account, wire.RetrievedAt, wire.AsOf, wire.ReferenceAt, wire.Amount, wire.Rate);
        }
        public override void Write(Utf8JsonWriter writer, WealthsimpleDailyPerformance value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new DailyWire(value.Account, value.RetrievedAt, value.AsOf, value.ReferenceAt, value.Amount, value.Rate), options);
    }

    private sealed record HoldingWire(string Symbol, string Name, string SecurityType, decimal Quantity,
        WealthsimpleMoney? Value, WealthsimpleMoney? BookValue, WealthsimpleMoney? UnrealizedGain, string? SecurityId);
    private sealed class HoldingConverter : JsonConverter<WealthsimpleHolding>
    {
        public override WealthsimpleHolding Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<HoldingWire>(ref reader, options) ?? throw new JsonException("Missing holding.");
            return new(wire.Symbol, wire.Name, wire.SecurityType, wire.Quantity, wire.Value, wire.BookValue, wire.UnrealizedGain)
                { SecurityId = wire.SecurityId };
        }
        public override void Write(Utf8JsonWriter writer, WealthsimpleHolding value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new HoldingWire(value.Symbol, value.Name, value.SecurityType, value.Quantity,
                value.Value, value.BookValue, value.UnrealizedGain, value.SecurityId), options);
    }

    private sealed record PendingWire(string Symbol, string Side, string Status, string ExecutionType, decimal Quantity,
        WealthsimpleMoney? LimitPrice, DateTimeOffset CreatedAt, string OrderId, string SecurityId);
    private sealed class PendingConverter : JsonConverter<WealthsimplePendingOrder>
    {
        public override WealthsimplePendingOrder Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<PendingWire>(ref reader, options) ?? throw new JsonException("Missing pending order.");
            return new(wire.Symbol, wire.Side, wire.Status, wire.ExecutionType, wire.Quantity, wire.LimitPrice, wire.CreatedAt)
                { OrderId = wire.OrderId, SecurityId = wire.SecurityId };
        }
        public override void Write(Utf8JsonWriter writer, WealthsimplePendingOrder value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new PendingWire(value.Symbol, value.Side, value.Status, value.ExecutionType,
                value.Quantity, value.LimitPrice, value.CreatedAt, value.OrderId, value.SecurityId), options);
    }
}

/// <summary>Only handles issued by this engine for its current authenticated catalogue can select an account.</summary>
internal sealed class EngineAccountCatalogue
{
    private readonly Dictionary<Guid, WealthsimpleTfsaAccount> accounts = [];
    private long revision = -1;
    internal void Publish(IReadOnlyList<WealthsimpleTfsaAccount> current, long sessionRevision)
    {
        foreach (var account in current)
            if (revision != sessionRevision || !accounts.ContainsKey(EngineRpcJson.Reference(account).Handle))
                EngineRpcJson.SetReference(account, new(Guid.NewGuid(), account.Id, account.DisplayName));
        accounts.Clear(); revision = sessionRevision;
        foreach (var account in current) accounts.Add(EngineRpcJson.Reference(account).Handle, account);
    }
    internal WealthsimpleTfsaAccount Resolve(EngineRpcJson.AccountReference reference, long sessionRevision, bool connected)
    {
        if (!connected || revision != sessionRevision || !accounts.TryGetValue(reference.Handle, out var account) ||
            reference != EngineRpcJson.Reference(account))
            throw new InvalidOperationException("The engine account selection expired. Refresh the connected account.");
        return account;
    }
}
