#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Trader.DelphiLive;

namespace Core.Oracle;

/// <summary>Model-facing view only. The complete packet remains the validation and saved audit record.</summary>
public static class AiPortfolioPrompt
{
    public const string Format = "portfolio-review-view-v1";
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] Metrics = ["Close", "PersistenceScore", "DirectionalVolumeBalance20",
        "RawReturn20", "ExcessReturn20", "RawReturn60", "ExcessReturn60", "PreviousCloseReturn",
        "SessionVwapDistanceUnits", "PriorRangeHighDistanceUnits", "FullDayVolumeFraction20"];

    public static string Create(string frozenPacketJson)
    {
        // Small packets keep their original wire shape. This is a formatting threshold,
        // never a substitute for the provider's exact 32k token admission check.
        if (frozenPacketJson.Length <= 60_000) return frozenPacketJson;
        var packet = JsonNode.Parse(frozenPacketJson)!.AsObject();
        foreach (var evidence in packet["evidence"]!.AsArray())
        {
            string id = evidence!["id"]!.GetValue<string>();
            var description = ReadJson(evidence["description"]!.GetValue<string>());
            if (id.StartsWith("strategy:", StringComparison.Ordinal) || id.EndsWith(":live-source", StringComparison.Ordinal) ||
                id.EndsWith(":shadow-source", StringComparison.Ordinal) || id.EndsWith(":shadow-signals", StringComparison.Ordinal))
                RemoveAuditIdentifiers(description);
            if (description is JsonObject body && id.Contains(":live-signal:", StringComparison.Ordinal))
                description = Live(body);
            else if (description is JsonObject account && id == "managed-account" &&
                account["Contract"]?.GetValue<string>() == Core.Trader.Risk.SystemShadowAiPortfolioRiskMonitor.ValuationContract &&
                account["SavedBook"] is JsonObject book && book["Positions"] is JsonArray positions)
            {
                // Closed ledger rows support validation/reconciliation, not current spendable holdings.
                // Only recognize the saved Shadow contract; unfamiliar books remain complete.
                if (positions.All(p => p?["Status"]?.GetValue<string>() is "Open" or "Closed"))
                {
                    book["ClosedPositionCount"] = positions.Count(p => p!["Status"]!.GetValue<string>() == "Closed");
                    book["Positions"] = new JsonArray(positions.Where(p => p!["Status"]!.GetValue<string>() == "Open")
                        .Select(p => p!.DeepClone()).ToArray());
                }
            }
            evidence["description"] = description;
        }
        // Repeated definitions and explanatory text have one copy, with explicit references.
        // No account-specific values are merged unless their complete JSON is identical.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Count(packet);
        var shared = new JsonObject();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var view = Pack(packet);
        return new JsonObject
        {
            ["format"] = Format,
            ["reading_guide"] = "packet is the review view. An object with only shared_ref means the identical value in shared. " +
                "A table object represents an array of records: each row follows its columns. " +
                "Live descriptions use columns and row; history uses history_columns and rows. Measurements follow measurement_columns. " +
                "Live family rows contain Family, State, ReasonCode. Source JSON strings are shown as structured data. " +
                "All candidates, eligibility, rules and evidence IDs remain. Live detail is a current summary, not the full calculation history: " +
                "selected current measurements and recent validity/confidence/momentum are shown; detailed window calculations, " +
                "older raw measurements, detailed daily gate traces, vote totals, lifecycle bookkeeping, technical source IDs/hashes " +
                "and closed managed-position rows remain in the saved full packet. Candidate IDs/versions, account identities, rule IDs " +
                "and evidence IDs used for the decision are unchanged; source identity checks already ran before rendering. " +
                "Null remains missing, not zero. FullDayVolumeFraction20 is a non-voting full-day fraction, not same-clock volume pace. " +
                "Do not infer omitted measurements or treat paper confirmation as managed-account permission.",
            ["live_enum_codes"] = new JsonObject
            {
                ["Confidence.State"] = EnumCodes<DelphiLiveDataConfidenceState>(),
                ["Momentum.State"] = EnumCodes<DelphiLiveMomentumState>(),
                ["Momentum.StrongTier"] = EnumCodes<DelphiLiveStrongTier>(),
                ["Momentum.NeutralDetail"] = EnumCodes<DelphiLiveNeutralDetail>(),
                ["Family"] = EnumCodes<DelphiLiveSignalFamily>(),
                ["Family.State"] = EnumCodes<DelphiLiveFamilyState>(),
                ["Lifecycle.State"] = EnumCodes<DelphiLiveRecommendationState>(),
                ["SourceLenses.Lens"] = EnumCodes<DelphiLiveSourceLens>()
            },
            ["shared"] = shared, ["packet"] = view
        }.ToJsonString(Json);

        void Count(JsonNode? node)
        {
            if (node is null) return;
            string key = node.ToJsonString(Json);
            if (key.Length >= 48) counts[key] = counts.GetValueOrDefault(key) + 1;
            foreach (var child in Children(node)) Count(child);
        }
        JsonNode? Pack(JsonNode? node)
        {
            if (node is null) return null;
            string key = node.ToJsonString(Json);
            if (counts.GetValueOrDefault(key) > 1)
            {
                if (!names.TryGetValue(key, out string? name))
                {
                    var value = Copy(node);
                    name = "s" + names.Count;
                    names.Add(key, name);
                    shared.Add(name, value);
                }
                return new JsonObject { ["shared_ref"] = name };
            }
            return Copy(node);
        }
        JsonNode Copy(JsonNode node) => node switch
        {
            JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Pack(p.Value)))),
            JsonArray array => TableOrArray(array),
            _ => node.DeepClone()
        };
        JsonNode TableOrArray(JsonArray array)
        {
            if (array.Count >= 2 && array[0] is JsonObject first)
            {
                var fields = first.Select(p => p.Key).ToArray();
                if (fields.Length >= 2 && array.All(n => n is JsonObject obj && obj.Select(p => p.Key).SequenceEqual(fields)))
                    return new JsonObject { ["table"] = new JsonObject
                    {
                        ["columns"] = Strings(fields),
                        ["rows"] = new JsonArray(array.Select(n => (JsonNode)new JsonArray(fields.Select(f => Pack(n![f])).ToArray())).ToArray())
                    } };
            }
            return new JsonArray(array.Select(Pack).ToArray());
        }
    }

    private static JsonNode? ReadJson(string text)
    {
        try
        {
            var value = JsonNode.Parse(text);
            // Plain scalar evidence remains text. Do not interpret arbitrary prose as a number/bool.
            if (value is JsonObject or JsonArray) return Normalize(value);
        }
        catch (JsonException) { }
        return JsonValue.Create(text);
    }

    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject obj when obj.ContainsKey("shared_ref") || obj.ContainsKey("table") => throw new JsonException("Reserved prompt marker in evidence."),
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Normalize(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Normalize).ToArray()),
        JsonValue val when val.TryGetValue<string>(out var text) && (text.StartsWith('{') || text.StartsWith('[')) => ReadJson(text),
        _ => node?.DeepClone()
    };

    private static IEnumerable<JsonNode?> Children(JsonNode node) => node switch
    {
        JsonObject obj => obj.Select(p => p.Value), JsonArray array => array, _ => []
    };

    private static readonly HashSet<string> AuditIdentifiers = new(StringComparer.Ordinal)
    {
        "CandidateTrackingId", "CalibrationCandidateId", "CandidateId", "RunId", "DailyStrategyId", "SessionId",
        "SourceRevisionId", "AccountSourceRevisionId", "GenerationId", "PolicyId", "PolicyVersionId", "BatchId", "PairId",
        "DailyStrategyVersionId", "LivePolicyVersionId", "ShadowStrategyVersionId", "ModelId", "VersionId", "StrategyId",
        "ArtifactSha256", "ModelSetSha256", "Fingerprint", "SettingsHash", "ShadowSettingsHash"
    };
    private static void RemoveAuditIdentifiers(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in obj.Select(p => p.Key).Where(AuditIdentifiers.Contains).ToArray()) obj.Remove(name);
            foreach (var child in obj.Select(p => p.Value)) RemoveAuditIdentifiers(child);
        }
        else if (node is JsonArray array) foreach (var child in array) RemoveAuditIdentifiers(child);
    }

    private static JsonNode Live(JsonObject signal)
    {
        // Unknown shapes stay complete instead of silently dropping new adapter fields.
        string[] expected = ["Symbol", "DailyCandidateId", "HasSourceConflict", "Current", "PaperAccountConfirmation", "Scope", "Checkpoints"];
        string[] checkpointFields = ["EvaluationId", "BarEndUtc", "EvaluatedUtc", "ContinuityEpoch", "DailySetup",
            "ObservationIsValid", "FamiliesMature", "CurrentStockObservationId", "Confidence", "Momentum",
            "FamilyJudgments", "Persistence", "PriceMovement", "VolumeSupport", "PriceStructure", "RawValues", "DerivedFacts"];
        if (signal.Count != expected.Length || signal.Any(p => !expected.Contains(p.Key)) || signal["Checkpoints"] is not JsonArray checkpoints ||
            checkpoints.Any(c => c is not JsonObject)) return signal;
        if (checkpoints.Any(c => c!.AsObject().Count != checkpointFields.Length ||
            c.AsObject().Any(p => !checkpointFields.Contains(p.Key)))) return signal;
        var latest = checkpoints.LastOrDefault();
        var history = new JsonArray(checkpoints.Select(c => (JsonNode)new JsonArray(
            Clone(c, "BarEndUtc"), Clone(c, "ContinuityEpoch"), Clone(c, "ObservationIsValid"),
            Clone(c, "FamiliesMature"), Clone(c, "Confidence"), Clone(c?["Momentum"], "State"))).ToArray());
        var families = new JsonArray((latest?["FamilyJudgments"]?.AsArray() ?? []).Select(f => (JsonNode)new JsonArray(
            Clone(f, "Family"), Clone(f, "State"), Clone(f, "ReasonCode"))).ToArray());
        var measurements = new JsonArray(Metrics.Select(k => Clone(latest?["RawValues"], k)).ToArray());
        var facts = Pick(latest?["DerivedFacts"], "Observation", "MarketJudgment", "LifecycleReason",
            "OperationalContinuityStartUtc", "FrozenRulerSourceThrough", "SessionThesis", "FullDayVolumeFraction20Role");
        var daily = latest?["DailySetup"];
        // Daily identities and complete gate traces remain in the full packet. The view retains
        // each lens's rank/admission; unchanged candidate rules still govern overrides.
        var dailySummary = daily is null ? null : new JsonObject
        {
            ["CommonDelphiComposite"] = Clone(daily, "CommonDelphiComposite"),
            ["SourceLenses"] = daily["SourceLenses"] is JsonArray lenses
                ? new JsonArray(lenses.Select(l => (JsonNode?)Pick(l, "Lens", "Rank", "RankingKey", "IsEligible", "IsPublished")).ToArray())
                : Clone(daily, "SourceLenses")
        };
        var account = signal["PaperAccountConfirmation"];
        var accountSummary = account is null ? null : new JsonObject
        {
            ["ContinuityEpoch"] = Clone(account, "ContinuityEpoch"),
            ["Lifecycle"] = Pick(account["Lifecycle"], "State", "ReasonCode", "ConsecutiveStrongObservations",
                "ConsecutiveStrongWeakeningObservations", "LastScheduledBarEndUtc")
        };
        return new JsonObject
        {
            ["columns"] = Strings("Symbol", "HasDailyCandidate", "HasSourceConflict", "Current", "PaperAccountConfirmation",
                "LatestDailyRanks", "LatestMomentum", "LatestFamilies", "LatestMeasurements", "LatestDataFacts", "History",
                "LatestEvaluatedUtc", "HasLatestStockObservation"),
            ["measurement_columns"] = Strings(Metrics),
            ["history_columns"] = Strings("BarEndUtc", "ContinuityEpoch", "ObservationIsValid",
                "FamiliesMature", "Confidence", "MomentumState"),
            ["row"] = new JsonArray(Clone(signal, "Symbol"), JsonValue.Create(signal["DailyCandidateId"] is not null), Clone(signal, "HasSourceConflict"),
                Clone(signal, "Current"), accountSummary, dailySummary,
                Pick(latest?["Momentum"], "State", "StrongTier", "NeutralDetail"), families, measurements, facts, history,
                Clone(latest, "EvaluatedUtc"), latest is null ? null : JsonValue.Create(latest["CurrentStockObservationId"] is not null))
        };
    }
    private static JsonNode? Clone(JsonNode? node, string name) => node?[name]?.DeepClone();
    private static JsonObject? Pick(JsonNode? node, params string[] names) => node is null ? null :
        new JsonObject(names.Select(n => KeyValuePair.Create(n, Clone(node, n))));
    private static JsonArray Strings(params string[] names) => new(names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
    private static JsonObject EnumCodes<T>() where T : struct, Enum => new(Enum.GetValues<T>().Select(v =>
        KeyValuePair.Create(Convert.ToInt32(v).ToString(System.Globalization.CultureInfo.InvariantCulture), (JsonNode?)JsonValue.Create(v.ToString()))));
}
