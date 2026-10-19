#nullable enable
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.Promotion;

/// <summary>
/// Read-only consumer of an independently reviewed native qualification packet. No UI writer or
/// qualification producer is provided here. Protection and file hashes establish integrity only;
/// they cannot turn synthetic results or operator checkboxes into native evidence.
/// </summary>
public sealed class BrokerExecutionQualificationFileStore(string path, IManualBuyProtector protector)
{
    public BrokerExecutionQualificationBundle? Read(DateTimeOffset now)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The qualification packet exceeds its supported size.");
        var saved = JsonSerializer.Deserialize<BrokerExecutionQualificationBundle>(protector.Unprotect(File.ReadAllBytes(path)))
            ?? throw new InvalidDataException("The protected native qualification packet is unreadable.");
        saved.Validate(now);
        foreach (var proof in saved.Qualifications)
        {
            if (!Path.IsPathFullyQualified(proof.EvidenceReference) || !File.Exists(proof.EvidenceReference) ||
                new FileInfo(proof.EvidenceReference).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("A reviewed native evidence file is missing or unsupported; recover qualification before approval.");
            using var stream = File.OpenRead(proof.EvidenceReference);
            if (Convert.ToHexString(SHA256.HashData(stream)) != proof.EvidenceHash)
                throw new InvalidDataException("A reviewed native evidence file changed; repeat its independent qualification before approval.");
        }
        return saved;
    }
}
