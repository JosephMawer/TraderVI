#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Oracle.Llm;

public sealed record AiCallRecord(
    Guid CallId, DateTimeOffset StartedUtc, string MonthUtc, AiSpendPolicy Policy,
    string RequestJson, string RequestSha256, int CountedInputTokens, int OutputLimit,
    decimal ReservedCad, string? ResponseJson = null, int? InputTokens = null,
    int? OutputTokens = null, decimal? ChargedCad = null, string Outcome = "Reserved",
    DateTimeOffset? SettledUtc = null);

/// <summary>
/// Private local audit and shared spend ledger. One owner per directory; unknown calls keep their
/// full reservation across restart and month boundaries. Initialization is explicit, never recovery.
/// No API keys belong in this journal. The host must secure the directory and preserve its identity.
/// </summary>
public sealed class AiCallJournal : IDisposable
{
    private readonly string path;
    private readonly FileStream lease;
    private readonly object sync = new();
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record State(int Version, List<AiCallRecord> Calls, bool Blocked = false);

    public AiCallJournal(string directory, bool initializeNew = false)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute AI journal directory is required.");
        if (initializeNew) Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "ai-calls.json");
        string leasePath = Path.Combine(directory, "ai-calls.lock");
        if (initializeNew && File.Exists(leasePath))
            throw new InvalidOperationException("AI journal directory was already initialized; reviewed recovery is required.");
        // The lock file also marks previous initialization even when the data file is lost.
        lease = new FileStream(leasePath, initializeNew ? FileMode.CreateNew : FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (initializeNew)
            {
                // A pending creation is also evidence of previous use. Never erase it to reset spending.
                if (File.Exists(path) || File.Exists(path + ".pending"))
                    throw new InvalidOperationException("AI journal already exists; open it without initialization.");
                Save(new State(1, []));
            }
            _ = Read();
        }
        catch { lease.Dispose(); throw; }
    }

    public IReadOnlyList<AiCallRecord> ReadAll()
    {
        lock (sync) return Read().Calls.ToArray();
    }

    public void CheckNewCall(Guid callId, AiSpendPolicy policy, DateTimeOffset now)
    {
        lock (sync) Check(Read(), callId, policy, now);
    }

    public void Reserve(Guid callId, AiSpendPolicy policy, DateTimeOffset now,
        string requestJson, int inputTokens, int outputLimit)
    {
        lock (sync)
        {
            var state = Read();
            Check(state, callId, policy, now);
            if (string.IsNullOrWhiteSpace(requestJson) || requestJson.Length > 2_000_000 ||
                inputTokens <= 0 || inputTokens > policy.MaxInputTokens ||
                outputLimit <= 0 || outputLimit > policy.MaxOutputTokens)
                throw new InvalidOperationException("AI request exceeds its reviewed bounds.");
            decimal reserve = policy.CostCad(inputTokens, outputLimit);
            string month = Month(now);
            decimal committed = state.Calls.Where(c => c.MonthUtc == month || c.ChargedCad is null ||
                    (c.SettledUtc.HasValue && Month(c.SettledUtc.Value) == month))
                .Sum(c => c.ChargedCad ?? c.ReservedCad);
            // A lower ceiling remains binding for the rest of its month; settings cannot refill it.
            decimal ceiling = state.Calls.Where(c => c.MonthUtc == month)
                .Select(c => c.Policy.MonthlyLimitCad).Append(policy.MonthlyLimitCad).Min();
            if (committed + reserve > ceiling)
                throw new LlmBudgetExceededException("The shared monthly AI budget cannot cover this request.");
            state.Calls.Add(new(callId, now, month, policy, requestJson, Hash(requestJson),
                inputTokens, outputLimit, reserve));
            Save(state);
        }
    }

    public void RecordResponse(Guid callId, string responseJson)
    {
        lock (sync)
        {
            var state = Read();
            int index = Find(state, callId);
            if (responseJson.Length > 2_000_000 || state.Calls[index].ResponseJson != null)
                throw new InvalidOperationException("AI response cannot replace existing evidence.");
            state.Calls[index] = state.Calls[index] with { ResponseJson = responseJson };
            Save(state);
        }
    }

    public void Block()
    {
        lock (sync) Save(Read() with { Blocked = true });
    }

    public void Settle(Guid callId, int inputTokens, int outputTokens, string outcome, DateTimeOffset now)
    {
        lock (sync)
        {
            var state = Read();
            int index = Find(state, callId);
            var call = state.Calls[index];
            if (call.ResponseJson is null || call.ChargedCad != null || inputTokens <= 0 || outputTokens < 0 || now < call.StartedUtc)
                throw new InvalidOperationException("AI usage cannot settle without valid original response evidence.");
            decimal charged = call.Policy.CostCad(inputTokens, outputTokens);
            bool overrun = inputTokens > call.CountedInputTokens || outputTokens > call.OutputLimit || charged > call.ReservedCad;
            state.Calls[index] = call with
            {
                InputTokens = inputTokens, OutputTokens = outputTokens,
                ChargedCad = charged, Outcome = overrun ? "UsageOverrun" : outcome, SettledUtc = now
            };
            Save(state with { Blocked = state.Blocked || overrun });
            if (overrun) throw new InvalidOperationException("AI usage exceeded its reservation; further calls are blocked.");
        }
    }

    private static string Month(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static int Find(State state, Guid id)
    {
        int index = state.Calls.FindIndex(c => c.CallId == id);
        return index >= 0 ? index : throw new InvalidOperationException("Unknown AI call reservation.");
    }

    private static void Check(State state, Guid callId, AiSpendPolicy policy, DateTimeOffset now)
    {
        policy.Validate(now);
        if (state.Blocked || callId == Guid.Empty || state.Calls.Any(c => c.CallId == callId) ||
            state.Calls.Any(c => c.StartedUtc > now || c.SettledUtc > now))
            throw new InvalidOperationException("AI journal is blocked, clock moved backward or call identity was already used.");
    }

    private State Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(path) || File.Exists(path + ".pending") || new FileInfo(path).Length > 256_000_000)
            throw new InvalidDataException("AI journal is missing or needs maintenance; spending is blocked.");
        State state;
        try { state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(path), Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidDataException("AI journal is invalid; spending is blocked."); }
        if (state.Version != 1 || state.Calls == null || state.Calls.Any(c => c is null) ||
            state.Calls.Select(c => c.CallId).Distinct().Count() != state.Calls.Count)
            throw new InvalidDataException("AI journal has invalid identities.");
        foreach (var call in state.Calls)
        {
            if (call.Policy is null || call.RequestJson is null || call.CallId == Guid.Empty ||
                call.MonthUtc != Month(call.StartedUtc) || call.RequestSha256 != Hash(call.RequestJson) ||
                call.CountedInputTokens <= 0 || call.CountedInputTokens > call.Policy.MaxInputTokens ||
                call.OutputLimit <= 0 || call.OutputLimit > call.Policy.MaxOutputTokens ||
                call.ReservedCad != call.Policy.CostCad(call.CountedInputTokens, call.OutputLimit) ||
                (call.ChargedCad is null && (call.SettledUtc != null || call.InputTokens != null || call.OutputTokens != null)) ||
                (call.ChargedCad != null && (call.SettledUtc is null || call.SettledUtc < call.StartedUtc ||
                    call.ResponseJson is null || call.InputTokens is null or <= 0 || call.OutputTokens is null or < 0 ||
                    call.ChargedCad != call.Policy.CostCad(call.InputTokens.Value, call.OutputTokens.Value))))
                throw new InvalidDataException("AI journal evidence needs recovery.");
            if (!state.Blocked && (call.InputTokens > call.CountedInputTokens || call.OutputTokens > call.OutputLimit ||
                    call.ChargedCad > call.ReservedCad))
                throw new InvalidDataException("AI journal has an unresolved usage overrun.");
            call.Policy.Validate(call.StartedUtc);
        }
        return state;
    }

    private void Save(State state)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        using (var stream = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(path + ".pending", path, overwrite: true);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lease.Dispose();
        }
    }
}
