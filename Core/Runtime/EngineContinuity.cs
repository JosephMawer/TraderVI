#nullable enable
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record EngineDatabaseIdentity(Guid Database, Guid Family, Guid RecoveryFork)
{
    public void Validate()
    {
        if (Database == Guid.Empty || Family == Guid.Empty || RecoveryFork == Guid.Empty)
            throw new InvalidDataException("Database recovery identity is unavailable.");
    }
}
public sealed record EngineContinuityStamp(int Version, EngineDatabaseIdentity Database, Guid RunId, long Sequence, bool Clean)
{
    public void Validate()
    {
        if (Database is null) throw new InvalidDataException("Missing database recovery identity.");
        Database.Validate();
        if (Version != 1 || RunId == Guid.Empty || Sequence < 0 || Sequence == long.MaxValue)
            throw new InvalidDataException("Unsupported engine continuity witness.");
    }
    [JsonIgnore] public string Json => JsonSerializer.Serialize(this);
    [JsonIgnore] public string Hash => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json)));
}
public sealed record EngineContinuityObservation(EngineDatabaseIdentity Database, EngineContinuityStamp? Stamp);
public interface IEngineContinuityStore
{
    Task<EngineContinuityObservation> ReadAsync(CancellationToken ct);
    Task WriteAsync(EngineContinuityStamp? expected, EngineContinuityStamp next, CancellationToken ct);
    Task HoldAsync(string key, string code, CancellationToken ct);
}
public interface IEngineContinuityFile
{
    EngineContinuityStamp? Read();
    void Write(EngineContinuityStamp value);
}

/// <summary>Protected local witness paired with SQL state. Dashboard lifetime never changes either stamp.</summary>
public sealed class EngineContinuityFile(string path, IManualBuyProtector protector) : IEngineContinuityFile
{
    public EngineContinuityStamp? Read()
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Engine continuity file is too large.");
        var bytes = protector.Unprotect(File.ReadAllBytes(path));
        try
        {
            var value = JsonSerializer.Deserialize<EngineContinuityStamp>(bytes) ?? throw new InvalidDataException("Missing engine continuity witness.");
            value.Validate(); return value;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Write(EngineContinuityStamp value)
    {
        value.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".pending";
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] encoded;
        try { encoded = protector.Protect(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(encoded); stream.Flush(true); }
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>A lost file, interrupted run, changed database fork or mismatched SQL/file pair requires explicit recovery.</summary>
public sealed class EngineContinuity(IEngineContinuityStore store, IEngineContinuityFile file)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private EngineContinuityStamp? current;
    private bool faulted;
    public async Task StartAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (current is not null) throw new InvalidOperationException("Engine continuity already started.");
            EngineContinuityStamp? local;
            try { local = file.Read(); }
            catch (Exception e) when (e is IOException or CryptographicException or JsonException or ArgumentException)
            { local = null; }
            var saved = await store.ReadAsync(ct).ConfigureAwait(false); saved.Database.Validate();
            Guid run = Guid.NewGuid();
            if (local is null || saved.Stamp is null || local != saved.Stamp || !local.Clean || local.Database != saved.Database)
                await store.HoldAsync("EngineContinuity/" + run.ToString("D"), "EngineRecoveryRequired", ct).ConfigureAwait(false);
            var next = new EngineContinuityStamp(1, saved.Database, run, checked((saved.Stamp?.Sequence ?? -1) + 1), false);
            await store.WriteAsync(saved.Stamp, next, ct).ConfigureAwait(false);
            file.Write(next); current = next;
        }
        finally { gate.Release(); }
    }
    public Task VerifyAsync(CancellationToken ct = default) => CheckAsync(false, false, ct);
    public Task TickAsync() => CheckAsync(true, false, CancellationToken.None);
    public Task CloseAsync() => CheckAsync(true, true, CancellationToken.None);
    private async Task CheckAsync(bool advance, bool clean, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (clean && (current is null || faulted)) return; // A failed start/restore must never acquire a clean-stop receipt.
            if (current is null || current.Clean || faulted) throw new InvalidOperationException("Restart the engine and verify recovery before continuing.");
            try
            {
                var saved = await store.ReadAsync(ct).ConfigureAwait(false);
                if (saved.Database != current.Database || saved.Stamp != current || file.Read() != current)
                    throw new InvalidDataException("Engine/database continuity changed.");
                if (!advance) return;
                var next = current with { Sequence = checked(current.Sequence + 1), Clean = clean };
                await store.WriteAsync(current, next, ct).ConfigureAwait(false);
                file.Write(next); current = next;
            }
            catch
            {
                faulted = true;
                // A failed write/timeout is uncertain too. If SQL is unavailable, in-memory admission still fails.
                try { await store.HoldAsync("EngineContinuity/" + current.RunId.ToString("D"), "EngineContinuityLost", CancellationToken.None).ConfigureAwait(false); }
                catch { }
                throw;
            }
        }
        finally { gate.Release(); }
    }
}
