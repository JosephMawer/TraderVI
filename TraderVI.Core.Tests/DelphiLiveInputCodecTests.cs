using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveInputCodecTests
{
    [Fact]
    public void RoundTripPreservesEvidenceTimesStateAndDecision()
    {
        var input = Input(); var package = DelphiLiveInputCodec.Pack(input);
        var restored = DelphiLiveInputCodec.Read(package.InputJson, package.Contents);
        Assert.Equal(DelphiLiveLedgerJson.Serialize(input), DelphiLiveLedgerJson.Serialize(restored));
        Assert.Equal(DelphiLiveLedgerJson.Serialize(DelphiLiveEvaluationEngine.Evaluate(input)),
            DelphiLiveLedgerJson.Serialize(DelphiLiveEvaluationEngine.Evaluate(restored)));
    }
    [Fact]
    public void RepeatedCheckpointsAndPoliciesShareOriginalBars()
    {
        var input = Input(); var first = DelphiLiveInputCodec.Pack(input);
        var next = DelphiLiveInputCodec.Pack(input with { EvaluationId = Guid.NewGuid(), EvaluatedUtc = input.EvaluatedUtc.AddSeconds(1) });
        Assert.Equal(first.Contents.Keys.Order(), next.Contents.Keys.Order());
        Assert.NotEqual(first.InputJson, next.InputJson);
        Assert.True(next.InputJson.Length < DelphiLiveLedgerJson.Serialize(input).Length);
    }
    [Fact]
    public void CorruptedOrMissingContentCannotBeUsed()
    {
        var package = DelphiLiveInputCodec.Pack(Input());
        var contents = new Dictionary<string, string>(package.Contents);
        string key = contents.Keys.First(); contents.Remove(key);
        Assert.Throws<InvalidDataException>(() => DelphiLiveInputCodec.Read(package.InputJson, contents));
        contents[key] = "{}";
        Assert.Throws<InvalidDataException>(() => DelphiLiveInputCodec.Read(package.InputJson, contents));
    }
    [Fact]
    public void ReplacingValidReferenceOrTimestampFailsManifestCheck()
    {
        var package = DelphiLiveInputCodec.Pack(Input()); var root = JsonNode.Parse(package.InputJson)!;
        root["evaluatedUtc"] = DateTime.UtcNow;
        Assert.Throws<InvalidDataException>(() => DelphiLiveInputCodec.Read(root.ToJsonString(), package.Contents));
        root = JsonNode.Parse(package.InputJson)!;
        root["stock"]!["bars"]![0] = root["xiu"]!["bars"]![0]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => DelphiLiveInputCodec.Read(root.ToJsonString(), package.Contents));
    }
    [Fact]
    public void LegacyInputsRemainReadableWithoutSharedStorage()
    {
        var input = Input(); string json = DelphiLiveLedgerJson.Serialize(input);
        Assert.Empty(DelphiLiveInputCodec.References(json));
        Assert.Equal(json, DelphiLiveLedgerJson.Serialize(DelphiLiveInputCodec.Read(json, new Dictionary<string, string>())));
    }
    internal static DelphiLiveEvaluationInput Input()
    {
        var date = new DateOnly(2026, 10, 21); var open = new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc);
        DelphiLiveFiveMinuteSeries Series(string symbol) => new(symbol, date, open, open,
            Enumerable.Range(0, 20).Select(i => new DelphiLiveFiveMinuteBar(Guid.NewGuid(), symbol, date,
                open.AddMinutes(i * 5), open.AddMinutes((i + 1) * 5), 100, 101, 99, 100, 100,
                open.AddMinutes((i + 1) * 5 + 2), "TMX", 1, DelphiLiveEvidenceDisposition.OperationalOnTime)));
        DelphiLiveTrueRangeRulerMeasurement Ruler(int n) => new(n, date.AddDays(-1), DelphiLiveScalarMeasurement.Available(0.04m));
        return new() { EvaluationId = Guid.NewGuid(), SessionId = Guid.NewGuid(), BarEndUtc = open.AddMinutes(100),
            EvaluatedUtc = open.AddMinutes(102), Stock = Series("ABC"), Xiu = Series("XIU"),
            VolatilityRulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)), PreviousState = DelphiLiveEvaluationState.Initial(true),
            Policy = DelphiLivePolicyDefinition.Version1, PreviousStockSessionClose = 100, PreviousXiuSessionClose = 100,
            MedianFullDayVolume20 = 10000, ExactPairPersistedOnTime = true };
    }
}
