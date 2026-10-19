#nullable enable
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Text.Json;

namespace Core.Trader.Promotion;

/// <summary>Protected operator host switch. File recovery never creates assignment or order authority.</summary>
public sealed class BrokerExecutionHostGrantFileStore(string path, IManualBuyProtector protector) : IBrokerExecutionHostGrantStore
{
    public BrokerExecutionHostGrant? Read()
    {
        if (!File.Exists(path)) return null;
        var saved = JsonSerializer.Deserialize<BrokerExecutionHostGrant>(protector.Unprotect(File.ReadAllBytes(path)))
            ?? throw new InvalidDataException("The execution-host permission is unreadable.");
        saved.Validate(); return saved;
    }
    public void Write(BrokerExecutionHostGrant? expected, BrokerExecutionHostGrant next)
    {
        next.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Read() != expected) throw new InvalidOperationException("Execution-host permission changed. Read its saved mode before another action.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(next));
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, overwrite: true);
    }
}
