#nullable enable
using Core.Runtime;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CollectorQualificationPackageTests
{
    [Fact]
    public void ExactReviewedPackageBindsBuildSourceDeadlineAndEvidence()
    {
        using var f = new Fixture();
        var q = CollectorQualificationPackage.Read(f.Root, "reviewed-build", f.Now);
        Assert.Equal(TmxCollectorSource.Contract, q.SourceContract);
        Assert.Equal(TimeSpan.FromSeconds(45), q.DeadlineOffset);
        Assert.Equal(q, CollectorQualificationPackage.Read(f.Root, "reviewed-build", f.Now.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => CollectorQualificationPackage.Read(f.Root, "another-build", f.Now));
    }

    [Theory]
    [InlineData("missing")][InlineData("config-tampered")][InlineData("evidence-tampered")]
    [InlineData("source")][InlineData("deadline")][InlineData("subscriptions")][InlineData("relay")]
    [InlineData("storage")][InlineData("ownership")][InlineData("compatibility")]
    [InlineData("restart")][InlineData("freshness")][InlineData("capacity")]
    public void IncompleteChangedOrUnqualifiedPackagesCannotAuthorizeAdoption(string fault)
    {
        using var f = new Fixture();
        switch (fault)
        {
            case "missing": File.Delete(f.PacketPath); break;
            case "config-tampered": File.AppendAllText(f.ConfigPath, " "); break;
            case "evidence-tampered": File.AppendAllText(f.EvidencePath, " "); break;
            case "source": f.WriteConfig("other-source"); f.WritePacket(); break;
            case "deadline": f.WriteConfig(deadline: TimeSpan.FromSeconds(30)); f.WritePacket(); break;
            case "subscriptions": f.WriteConfig(subscriptions: false); f.WritePacket(); break;
            case "relay": f.WriteConfig(relay: 0); f.WritePacket(); break;
            default:
                File.WriteAllText(f.EvidencePath, JsonSerializer.Serialize(new CollectorQualificationChecks(
                    fault != "storage", fault != "ownership", fault != "compatibility", fault != "restart",
                    fault != "freshness", fault != "capacity")));
                f.WritePacket(); break;
        }
        Assert.Throws<InvalidDataException>(() => CollectorQualificationPackage.Read(f.Root, "reviewed-build", f.Now));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "TraderVI_Qualification_Test_" + Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Root, "config.json");
        public string EvidencePath => Path.Combine(Root, "checks.json");
        public string PacketPath => Path.Combine(Root, CollectorQualificationPackage.FileName);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 14, 0, 0, TimeSpan.Zero);
        public Fixture()
        {
            Directory.CreateDirectory(Root); WriteConfig();
            File.WriteAllText(EvidencePath, JsonSerializer.Serialize(new CollectorQualificationChecks(true, true, true, true, true, true)));
            WritePacket();
        }
        public void WriteConfig(string source = TmxCollectorSource.Contract, TimeSpan? deadline = null, bool subscriptions = true, int relay = 2) =>
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new { SourceContract = source,
                Limits = new { DeadlineOffset = deadline ?? TimeSpan.FromSeconds(45) },
                UseVerifiedRealSubscriptions = subscriptions, LegacyIntradayConcurrency = relay }));
        public void WritePacket() => File.WriteAllText(PacketPath, JsonSerializer.Serialize(new CollectorQualificationPacket(
            TmxCollectorSource.Contract, TimeSpan.FromSeconds(45), "reviewed-build", Now.AddMinutes(-1), "test reviewer",
            ConfigPath, Hash(ConfigPath), EvidencePath, Hash(EvidencePath))));
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
