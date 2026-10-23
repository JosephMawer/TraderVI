#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static async Task<BrokerRiskContinuationReceipt> SelectedRiskReceipt(int historicalRoundTrips = 15)
    {
        var x = await SelectedSessionFixture();
        var selected = x.Continuation;
        var legacy = selected.OriginScope.Continuation!;
        var carry = legacy.PreviousScope.TrialCarry!;
        var original = carry.OriginalAnchor;
        var history = Enumerable.Range(0, historicalRoundTrips).SelectMany(i => new[]
        {
            x.F.Fact("older-buy-" + i, "HISTORY" + i, "BUY", 1, 10, original.SnapshotAt.AddDays(-2).AddMinutes(i)),
            x.F.Fact("older-sale-" + i, "HISTORY" + i, "SELL", 1, 10, original.SnapshotAt.AddDays(-1).AddMinutes(i))
        }).ToArray();
        BrokerAccountReconciliation WithHistory(BrokerAccountReconciliation account) => account with
            { Orders = history.Concat(account.Orders).ToArray() };
        carry = carry with { OriginalAnchor = WithHistory(original), ReviewedAccount = WithHistory(carry.ReviewedAccount) };
        legacy = legacy with { PreviousScope = legacy.PreviousScope with
            { CashOnlyAnchor = carry.OriginalAnchor, TrialCarry = carry } };
        selected = selected with { OriginScope = selected.OriginScope with
            { CashOnlyAnchor = carry.OriginalAnchor, TrialCarry = carry, Continuation = legacy } };
        selected.Validate(SelectedSessionCalendar());
        return new(selected.RiskRequest(), selected.ExpectedRisk(SelectedSessionCalendar()), x.F.Now.AddTicks(12345));
    }

    private static BrokerAccountRiskContinuationRepository.Row RiskStorageRow(BrokerRiskContinuationReceipt receipt, string json) =>
        new(receipt.Request.RequestId, receipt.Request.BaselineId, receipt.Request.PreviousRevisionId,
            receipt.State.TradingDate.ToDateTime(TimeOnly.MinValue), receipt.SavedAt.UtcDateTime,
            receipt.Request.RequestHash, EngineStrategySettings.Hash(json), json);

    [Theory]
    [InlineData(15, false)]
    [InlineData(18, true)]
    public async Task SelectedRiskStorageFitsExistingLimitWithoutLosingHistoricalEvidence(int roundTrips, bool exceedsInlineLimit)
    {
        var receipt = await SelectedRiskReceipt(roundTrips);
        // Reproduce the rejection with realistic older completed trades, not only four fixture orders.
        Assert.Equal(exceedsInlineLimit, JsonSerializer.SerializeToUtf8Bytes(receipt).Length > 256 * 1024);
        var json = BrokerRiskContinuationStorage.Serialize(receipt);
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, 256 * 1024);
        var reopened = BrokerAccountRiskContinuationRepository.Decode(RiskStorageRow(receipt, json))!;
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(reopened));
        Assert.Equal(receipt.Request.RequestHash, reopened.Request.RequestHash);
        Assert.Equal(BrokerAccountImport.Hash(receipt.State), BrokerAccountImport.Hash(reopened.State));
        Assert.Equal(receipt.State.Through, reopened.State.Through);
        Assert.Equal(receipt.State.LastCompletedSession, reopened.State.LastCompletedSession);
        Assert.Equal(receipt.State.SessionReference, reopened.State.SessionReference);
        Assert.Equal(receipt.State.DrawdownReference, reopened.State.DrawdownReference);
        Assert.Equal(roundTrips * 2, reopened.State.TrialCarry!.OriginalAnchor.Orders.Count(o => o.ExternalId.StartsWith("older-")));
        Assert.Equal(receipt.State.Continuation!.Fingerprint, reopened.State.Continuation!.Fingerprint);
        Assert.Equal(receipt.State.SessionContinuation!.Fingerprint, reopened.State.SessionContinuation!.Fingerprint);
    }

    [Fact]
    public async Task SelectedRiskStorageKeepsLegacyBytesAndReadsBothRepresentations()
    {
        var x = ContinuedFixture();
        var legacy = new BrokerRiskContinuationReceipt(x.Continuation.RiskRequest(),
            x.Continuation.Apply(x.Continuation.PreviousRisk, BrokerTrialCarryForwardTests.Calendar()), x.F.Now);
        Assert.Equal(JsonSerializer.Serialize(legacy), BrokerRiskContinuationStorage.Serialize(legacy));
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(
            BrokerAccountRiskContinuationRepository.Decode(RiskStorageRow(legacy, JsonSerializer.Serialize(legacy)))));
        var selected = await SelectedRiskReceipt(0);
        var inline = JsonSerializer.Serialize(selected);
        Assert.Equal(inline, JsonSerializer.Serialize(BrokerAccountRiskContinuationRepository.Decode(RiskStorageRow(selected, inline))));
    }

    [Theory]
    [InlineData("version")][InlineData("state hash")][InlineData("risk reference")]
    [InlineData("hold")][InlineData("through")][InlineData("request")]
    [InlineData("duplicate evidence")][InlineData("unknown field")][InlineData("missing state hash")]
    public async Task SelectedRiskStorageRejectsCorruptionEvenWithARecomputedStorageHash(string change)
    {
        var receipt = await SelectedRiskReceipt(0);
        var root = JsonNode.Parse(BrokerRiskContinuationStorage.Serialize(receipt))!;
        switch (change)
        {
            case "version": root["StorageFormat"] = "unknown"; break;
            case "state hash": root["StateHash"] = new string('B', 64); break;
            case "risk reference": root["State"]!["SessionReference"]!["Value"] = 1m; break;
            case "hold": root["State"]!["DailyLossHold"] = true; break;
            case "through": root["State"]!["Through"] = receipt.State.Through.AddDays(1); break;
            case "request": root["Request"]!["SessionContinuation"]!["ConfirmedBy"] = "changed"; break;
            case "duplicate evidence": root["State"]!["SessionContinuation"] = root["Request"]!["SessionContinuation"]!.DeepClone(); break;
            case "unknown field": root["Unexpected"] = true; break;
            case "missing state hash": root.AsObject().Remove("StateHash"); break;
        }
        var json = root.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => BrokerAccountRiskContinuationRepository.Decode(RiskStorageRow(receipt, json)));
    }

    [Fact]
    public async Task SelectedRiskStorageRejectsDifferentStateEvidenceAndStillEnforces256KiB()
    {
        var receipt = await SelectedRiskReceipt();
        var changed = receipt with { State = receipt.State with { TrialCarry = null } };
        Assert.Throws<InvalidOperationException>(() => BrokerRiskContinuationStorage.Serialize(changed));
        var tooLarge = await SelectedRiskReceipt(100);
        var error = Assert.Throws<InvalidOperationException>(() => BrokerRiskContinuationStorage.Serialize(tooLarge));
        Assert.Contains("exceeds the supported size", error.Message);
    }
}
