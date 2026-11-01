#nullable enable
using Core.Runtime;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.Storage;

// Only the isolated candidate writes this receipt. It seals the already validated SQL rows and assignment.
internal sealed record CompactDelphiCheckpoint(Guid Rehearsal, EngineDatabaseIdentity Database, CompactDelphiAssignment Assignment,
    long Version, Guid Operation, string Digest)
{
    internal bool Matches(Guid token, EngineDatabaseIdentity database, TradingCurrentState state) =>
        Rehearsal == token && Database == database && state.Account.BindingId == Assignment.Grant.BindingId &&
        state.Account.Version == Version && state.Checkpoints.Any(c => c.Version == Version && c.OperationId == Operation) &&
        CompactDelphiAssignment.StateDigest(state) == Digest;

    internal static CompactDelphiCheckpoint From(Guid token, EngineDatabaseIdentity database, CompactDelphiAssignment assignment,
        TradingCurrentState state) => new(token, database, assignment, state.Account.Version,
            state.Checkpoints.Single(c => c.Version == state.Account.Version).OperationId, CompactDelphiAssignment.StateDigest(state));

    internal static CompactDelphiCheckpoint Read(string path, IManualBuyProtector protector)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Oversized compact checkpoint.");
        byte[] clear = protector.Unprotect(File.ReadAllBytes(path));
        try { return JsonSerializer.Deserialize<CompactDelphiCheckpoint>(clear) ?? throw new InvalidDataException("No compact checkpoint."); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal void Write(string path, IManualBuyProtector protector)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            var data = protector.Protect(clear);
            if (data.Length > 1024 * 1024) throw new InvalidDataException("Oversized compact checkpoint.");
            using (var file = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(data);
                file.Flush(true);
            }
            File.Move(path + ".pending", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
