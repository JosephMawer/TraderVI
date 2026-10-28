#nullable enable
using Core.TMX;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Runtime;

/// <summary>Optional immutable package file; absence retains the installed legacy transport contract.</summary>
public static class CollectorIntradayRouting
{
    public const string FileName = "collector-routing.json";
    public static bool Read(string runtimeDirectory)
    {
        string path = Path.Combine(runtimeDirectory, FileName);
        if (!File.Exists(path)) return false;
        if (new FileInfo(path).Length > 1024) throw new InvalidDataException("Collector routing file exceeds its bound.");
        var configuration = JsonSerializer.Deserialize<Routing>(File.ReadAllText(path),
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        if (configuration?.Contract != "IndependentIntradayCollectorRoutingV1" || configuration.SourceContract != TmxCollectorSource.Contract)
            throw new InvalidDataException("Collector routing contract is not supported; no direct-source fallback is permitted.");
        return true;
    }
    public static void ApplyIfConfigured(string runtimeDirectory)
    {
        if (Read(runtimeDirectory)) TmxClient.RequireIndependentIntradayCollector();
    }
    private sealed record Routing(string Contract, string SourceContract);
}
