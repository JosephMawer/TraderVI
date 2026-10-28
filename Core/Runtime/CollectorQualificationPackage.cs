#nullable enable
using Core.Trader.Promotion;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Runtime;

public sealed record CollectorQualificationPacket(string SourceContract, TimeSpan DeadlineOffset,
    string ImplementationIdentity, DateTimeOffset QualifiedAt, string Reviewer,
    string ConfigurationPath, string ConfigurationSha256, string EvidencePath, string EvidenceSha256);
public sealed record CollectorQualificationChecks(bool NativeStorage, bool WriterOwnership, bool LegacyCompatibility,
    bool RestartRecovery, bool SourceFreshness, bool UniverseCapacity);

/// <summary>Reviewed evidence only; this class cannot produce qualification or enable execution.
/// No packet is included by default. The live study alone does not establish all required checks.</summary>
public static class CollectorQualificationPackage
{
    public const string FileName = "collector-qualification.json";
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static BrokerMinuteSourceQualification Read(string runtimeDirectory, string implementation, DateTimeOffset now)
    {
        var packet = ReadBounded<CollectorQualificationPacket>(Path.Combine(runtimeDirectory, FileName), 16384);
        if (string.IsNullOrWhiteSpace(packet.Reviewer) || packet.Reviewer.Length > 128 ||
            !Path.IsPathFullyQualified(packet.ConfigurationPath) || !Path.IsPathFullyQualified(packet.EvidencePath))
            throw new InvalidDataException("Review the collector qualification package and its exact evidence paths.");
        byte[] configuration = ReadVerifiedFile(packet.ConfigurationPath, packet.ConfigurationSha256);
        var checks = JsonSerializer.Deserialize<CollectorQualificationChecks>(ReadVerifiedFile(packet.EvidencePath, packet.EvidenceSha256), Options)
            ?? throw new InvalidDataException("Collector checks are missing.");
        if (!(checks.NativeStorage && checks.WriterOwnership && checks.LegacyCompatibility &&
            checks.RestartRecovery && checks.SourceFreshness && checks.UniverseCapacity))
            throw new InvalidDataException("Collector storage, ownership, compatibility, restart, freshness and full-universe capacity must all be qualified.");
        using var config = JsonDocument.Parse(configuration);
        var root = config.RootElement;
        if (root.GetProperty("SourceContract").GetString() != packet.SourceContract ||
            root.GetProperty("Limits").GetProperty("DeadlineOffset").Deserialize<TimeSpan>() != packet.DeadlineOffset ||
            !root.GetProperty("UseVerifiedRealSubscriptions").GetBoolean() ||
            root.GetProperty("LegacyIntradayConcurrency").GetInt32() is < 1 or > 8)
            throw new InvalidDataException("The collector configuration differs from the qualified source, deadline or ownership package.");
        var qualification = new BrokerMinuteSourceQualification(packet.SourceContract, packet.DeadlineOffset,
            packet.ImplementationIdentity, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(packet))), packet.QualifiedAt);
        qualification.Validate(now, implementation);
        return qualification;
    }
    private static T ReadBounded<T>(string path, long maximum)
    {
        if (!File.Exists(path)) throw new InvalidDataException("The reviewed collector qualification package is not installed. Finish native and live-source qualification before minute-strategy adoption.");
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Collector qualification evidence exceeds its bound.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Collector qualification evidence is empty.");
    }
    private static byte[] ReadVerifiedFile(string path, string hash)
    {
        if (!CompleteTradingStrategy.IsHash(hash) || !File.Exists(path) || new FileInfo(path).Length > 65536)
            throw new InvalidDataException("Collector qualification file is missing, oversized or unreviewed.");
        byte[] content = File.ReadAllBytes(path);
        if (content.Length > 65536 || Convert.ToHexString(SHA256.HashData(content)) != hash)
            throw new InvalidDataException("Collector qualification file changed after review.");
        return content;
    }
}
