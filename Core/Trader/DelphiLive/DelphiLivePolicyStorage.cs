#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveStoredPolicyIdentity(
    Guid PolicyVersionId, string PolicyDefinitionName, int PolicyDefinitionSchemaVersion,
    string EvaluatorVersion, string CollectorVersion, int CollectorSourceContractVersion,
    string DecisionDossierVersion, int DecisionDossierSchemaVersion, string QuoteFillVersion,
    string ShadowPortfolioVersion, string ResearchOutcomeVersion, string RankingDiagnosticVersion,
    string PromotionProtocolVersion);

/// <summary>Combines separately stored identity and numeric settings without supplying defaults.</summary>
public static class DelphiLivePolicyStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DelphiLivePolicyDefinition Read(
        DelphiLiveStoredPolicyIdentity identity, string settingsJson, byte[] expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(settingsJson);
        ArgumentNullException.ThrowIfNull(expectedSha256);
        if (expectedSha256.Length != 32)
            throw new ArgumentException("Stored policy settings require a SHA-256 digest.", nameof(expectedSha256));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(settingsJson)), expectedSha256))
            throw new InvalidOperationException("Stored Delphi Live policy settings hash does not match.");
        using var document = JsonDocument.Parse(settingsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Policy settings must be an object.");
        RejectDuplicateFields(document.RootElement);
        var merged = JsonSerializer.SerializeToNode(identity, Options)!.AsObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (merged.ContainsKey(property.Name))
                throw new JsonException("Numeric policy settings cannot override stored identity.");
            merged.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
        }
        return JsonSerializer.Deserialize<DelphiLivePolicyDefinition>(merged, Options)!.Validate();
    }

    private static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
                throw new JsonException("Duplicate policy settings are not supported.");
            foreach (var property in properties) RejectDuplicateFields(property.Value);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateFields(child);
    }
}
