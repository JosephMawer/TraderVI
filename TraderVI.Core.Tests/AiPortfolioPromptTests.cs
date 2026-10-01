#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Oracle;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioPromptTests
{
    [Fact]
    public void SmallPacketRetainsExactOriginalWireText()
    {
        const string text = "{\"evidence\":[],\"candidates\":[]}";
        Assert.Equal(text, AiPortfolioPrompt.Create(text));
    }

    [Fact]
    public void CompactViewPreservesEligibilityRulesDatesAndCurrentSignalGapsWithoutMutatingAuditPacket()
    {
        var source = Packet();
        string original = source.ToJsonString();
        var prompt = JsonNode.Parse(AiPortfolioPrompt.Create(original))!;
        var view = Expand(prompt["packet"], prompt["shared"]!);
        Assert.Equal(original, source.ToJsonString());
        Assert.True(JsonNode.DeepEquals(source["candidates"], view!["candidates"]));
        Assert.Equal(source["expires_utc"]!.GetValue<string>(), view["expires_utc"]!.GetValue<string>());
        var evidence = view["evidence"]![0]!;
        Assert.Equal("choice:live-signal:TEST", evidence["id"]!.GetValue<string>());
        Assert.Equal("2026-10-01T15:05:00Z", evidence["observed_utc"]!.GetValue<string>());
        var d = evidence["description"]!;
        var row = d["row"]!;
        Assert.False(row[3]!.GetValue<bool>());
        Assert.True(row[2]!.GetValue<bool>());
        Assert.Equal("MissingPair", row[9]!["Observation"]!.GetValue<string>());
        Assert.Equal(12.34567890123456789m, row[8]![0]!.GetValue<decimal>());
        Assert.Null(row[8]![2]); // Missing volume support must never become zero.
        Assert.True(row[1]!.GetValue<bool>());
        Assert.Equal("2026-10-01T15:00:30Z", row[11]!.GetValue<string>());
        Assert.False(row[12]!.GetValue<bool>());
        Assert.False(row[10]![0]![2]!.GetValue<bool>());
        Assert.Equal("Low", row[10]![0]![4]!["State"]!.GetValue<string>());
        Assert.Contains("older raw measurements", prompt["reading_guide"]!.GetValue<string>());
        Assert.True(AiPortfolioPrompt.Create(original).Length < original.Length);
    }

    [Fact]
    public void UnknownCheckpointFieldsStayVisibleRatherThanSilentlyOmitted()
    {
        var source = Packet(extraField: true);
        var prompt = JsonNode.Parse(AiPortfolioPrompt.Create(source.ToJsonString()))!;
        var view = Expand(prompt["packet"], prompt["shared"]!);
        Assert.Equal("new missing-data condition", view!["evidence"]![0]!["description"]!["Checkpoints"]![0]!["FutureField"]!.GetValue<string>());
    }

    [Fact]
    public void EmptySignalHistoryRemainsExplicitlyEmpty()
    {
        var source = Packet(empty: true);
        var prompt = JsonNode.Parse(AiPortfolioPrompt.Create(source.ToJsonString()))!;
        var view = Expand(prompt["packet"], prompt["shared"]!);
        var row = view!["evidence"]![0]!["description"]!["row"]!;
        Assert.Null(row[5]);
        Assert.All(row[8]!.AsArray(), value => Assert.Null(value));
        Assert.Empty(row[10]!.AsArray());
    }

    [Fact]
    public void RemovesOnlyTechnicalSourceIdsWhileKeepingAccountIdentityAndDecisionVersions()
    {
        var source = Packet();
        source["evidence"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "strategy:choice", ["observed_utc"] = "2026-10-01T15:05:00Z",
            ["description"] = JsonSerializer.Serialize(new
            {
                PaperAccountId = "account", UnavailableReasons = new[] { "Blocked" },
                Strategy = new { VersionId = "internal-version", PolicyVersionId = "internal-policy",
                    MaximumHoldings = 3, HardLossFraction = 0.05m }
            })
        });
        var prompt = JsonNode.Parse(AiPortfolioPrompt.Create(source.ToJsonString()))!;
        var view = Expand(prompt["packet"], prompt["shared"]!);
        var strategy = view!["evidence"]![1]!["description"]!;
        Assert.Equal("account", strategy["PaperAccountId"]!.GetValue<string>());
        Assert.Equal("Blocked", strategy["UnavailableReasons"]![0]!.GetValue<string>());
        Assert.Null(strategy["Strategy"]!["PolicyVersionId"]);
        Assert.Equal(0.05m, strategy["Strategy"]!["HardLossFraction"]!.GetValue<decimal>());
        Assert.True(JsonNode.DeepEquals(source["candidates"], view["candidates"]));
    }

    [Fact]
    public void OnlyRecognizedClosedPositionsAreSummarizedAndAllRiskFieldsStay()
    {
        var source = Packet();
        source["evidence"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "managed-account", ["observed_utc"] = "2026-10-01T15:05:00Z",
            ["description"] = JsonSerializer.Serialize(new
            {
                Contract = global::Core.Trader.Risk.SystemShadowAiPortfolioRiskMonitor.ValuationContract,
                ExitTrigger = 98.123m, Phase = "Held", Sha256 = "saved-full-book-hash",
                SavedBook = new { CashVerifiedUtc = "2026-10-01T15:05:00Z", Positions = new[]
                { new { Status = "Open", Stop = 10.12m }, new { Status = "Closed", Stop = 11m } } }
            })
        });
        var prompt = JsonNode.Parse(AiPortfolioPrompt.Create(source.ToJsonString()))!;
        var view = Expand(prompt["packet"], prompt["shared"]!);
        var account = view!["evidence"]![1]!["description"]!;
        Assert.Equal(98.123m, account["ExitTrigger"]!.GetValue<decimal>());
        Assert.Equal("saved-full-book-hash", account["Sha256"]!.GetValue<string>());
        Assert.Single(account["SavedBook"]!["Positions"]!.AsArray());
        Assert.Equal(10.12m, account["SavedBook"]!["Positions"]![0]!["Stop"]!.GetValue<decimal>());
        Assert.Equal(1, account["SavedBook"]!["ClosedPositionCount"]!.GetValue<int>());
    }

    private static JsonObject Packet(bool extraField = false, bool empty = false)
    {
        var check = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            EvaluationId = "test-evaluation", BarEndUtc = "2026-10-01T15:00:00Z", EvaluatedUtc = "2026-10-01T15:00:30Z",
            ContinuityEpoch = 3, DailySetup = new { GateEvidence = "Failed: source rule" },
            ObservationIsValid = false, FamiliesMature = false, CurrentStockObservationId = (string?)null,
            Confidence = new { State = "Low", AllowsNewRisk = false }, Momentum = new { State = 5 },
            FamilyJudgments = new[] { new { Family = 0, State = 2, ReasonCode = "Missing" } },
            Persistence = new { Diagnostic = "detail" }, PriceMovement = new { Diagnostic = "detail" },
            VolumeSupport = new { Diagnostic = "detail" }, PriceStructure = new { Diagnostic = "detail" },
            RawValues = new { Close = 12.34567890123456789m, DirectionalVolumeBalance20 = (decimal?)null },
            DerivedFacts = new { Observation = "MissingPair" }
        }))!;
        if (extraField) check["FutureField"] = "new missing-data condition";
        var signal = new JsonObject
        {
            ["Symbol"] = "TEST", ["DailyCandidateId"] = "candidate", ["HasSourceConflict"] = true,
            ["Current"] = false, ["PaperAccountConfirmation"] = null, ["Scope"] = "Paper only",
            ["Checkpoints"] = empty ? new JsonArray() : new JsonArray(check)
        };
        var rules = new JsonArray(Enumerable.Range(0, 30).Select(i => (JsonNode)new JsonObject
        { ["id"] = "rule-" + i, ["is_hard_boundary"] = true, ["observation"] = new string('x', 2200) }).ToArray());
        return new JsonObject
        {
            ["expires_utc"] = "2026-10-01T15:10:00Z",
            ["candidates"] = new JsonArray(new JsonObject { ["id"] = "choice", ["eligible"] = false, ["rules"] = rules }),
            ["evidence"] = new JsonArray(new JsonObject
            { ["id"] = "choice:live-signal:TEST", ["observed_utc"] = "2026-10-01T15:05:00Z", ["description"] = signal.ToJsonString() })
        };
    }

    private static JsonNode? Expand(JsonNode? value, JsonNode shared)
    {
        if (value is JsonObject obj)
        {
            if (obj.Count == 1 && obj["shared_ref"] is { } reference) return Expand(shared[reference.GetValue<string>()], shared);
            if (obj.Count == 1 && obj["table"] is { } table)
            {
                var fields = table["columns"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
                return new JsonArray(table["rows"]!.AsArray().Select(row => (JsonNode)new JsonObject(fields.Select((f, i) =>
                    System.Collections.Generic.KeyValuePair.Create(f, Expand(row![i], shared))))).ToArray());
            }
            return new JsonObject(obj.Select(p => System.Collections.Generic.KeyValuePair.Create(p.Key, Expand(p.Value, shared))));
        }
        return value is JsonArray array ? new JsonArray(array.Select(v => Expand(v, shared)).ToArray()) : value?.DeepClone();
    }
}
