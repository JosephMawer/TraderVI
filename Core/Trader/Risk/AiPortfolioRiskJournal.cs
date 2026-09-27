#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Trader.Risk;

public enum AiRiskAccountMode { Paper, Live }
public sealed record AiRiskJournalScope(Guid AccountScopeId, Guid PortfolioId, AiRiskAccountMode Mode);
public enum AiRiskTransitionKind { Start, Observe, ConfirmClosure, BeginNextRound }
public sealed record AiRiskCommand(Guid CommandId, long ExpectedRevision, AiRiskTransitionKind Kind,
    PortfolioRiskValuation? Valuation = null, AiRoundClosure? Closure = null, AiRoundEntry? Entry = null);
public sealed record AiRiskTransition(long Revision, DateTimeOffset RecordedAt, AiRiskCommand Command,
    AiPortfolioRiskState State);
public sealed record AiRiskJournalSnapshot(long Revision, AiPortfolioRiskState? State);

/// <summary>
/// Private local history, not execution authority. Explicit initialization and exclusive ownership;
/// every transition is replay-validated on recovery. Host must secure/back up the directory and
/// block new risk on storage failure. Checksums detect corruption, not malicious edits or rollback
/// between process lifetimes. Independent holds, stock stops and broker recovery live elsewhere.
/// </summary>
public sealed class AiPortfolioRiskJournal : IDisposable
{
    private sealed record History(int Version, AiRiskJournalScope Scope, List<AiRiskTransition> Transitions);
    private sealed record Envelope(string Payload, string Sha256);
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private const int MaximumBytes = 32_000_000;
    private readonly string path;
    private readonly FileStream lease;
    private readonly AiRiskJournalScope scope;
    private readonly AiPortfolioRiskPolicy policy;
    private readonly object sync = new();
    private bool disposed;
    private long knownRevision = -1;
    private string? knownHash;

    public AiPortfolioRiskJournal(string directory, AiRiskJournalScope scope,
        AiPortfolioRiskPolicy policy, bool initializeNew = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Path.IsPathFullyQualified(directory) || scope.AccountScopeId == Guid.Empty ||
            scope.PortfolioId == Guid.Empty || !Enum.IsDefined(scope.Mode))
            throw new ArgumentException("An absolute private directory and identified account/portfolio scope are required.");
        this.scope = scope;
        this.policy = policy;
        path = Path.Combine(directory, "ai-risk.json");
        if (initializeNew) Directory.CreateDirectory(directory);
        lease = new FileStream(Path.Combine(directory, "ai-risk.lock"),
            initializeNew ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (initializeNew)
            {
                if (File.Exists(path) || File.Exists(path + ".pending"))
                    throw new InvalidOperationException("Risk history already exists; reviewed recovery is required.");
                Save(new(1, scope, []));
            }
            _ = Read();
        }
        catch { lease.Dispose(); throw; }
    }

    public AiRiskJournalSnapshot ReadCurrent()
    {
        lock (sync) { return Snapshot(Read()); }
    }

    public ImmutableArray<AiRiskTransition> ReadHistory()
    {
        lock (sync) { return Read().Transitions.ToImmutableArray(); }
    }

    /// <summary>Persist before returning. An exact command retry returns CURRENT state, never an old permission.</summary>
    public AiRiskJournalSnapshot Apply(AiRiskCommand command, DateTimeOffset now)
    {
        lock (sync)
        {
            var history = Read();
            ArgumentNullException.ThrowIfNull(command);
            var existing = history.Transitions.Find(t => t.Command.CommandId == command.CommandId);
            if (existing is not null)
            {
                if (Serialize(existing.Command) != Serialize(command))
                    throw new InvalidOperationException("A command identity cannot be reused with different input.");
                return Snapshot(history);
            }
            if (command.ExpectedRevision != history.Transitions.Count ||
                (history.Transitions.Count > 0 && now < history.Transitions[^1].RecordedAt))
                throw new InvalidOperationException("Risk commands must use the current revision and ordered recording time.");
            var state = Execute(history.Transitions.LastOrDefault()?.State, command, now);
            var transition = new AiRiskTransition(history.Transitions.Count + 1L, now, command, state);
            history.Transitions.Add(transition);
            Validate(history);
            Save(history);
            return Snapshot(history);
        }
    }

    private AiPortfolioRiskState Execute(AiPortfolioRiskState? previous, AiRiskCommand command, DateTimeOffset now)
    {
        if (command.CommandId == Guid.Empty || !Enum.IsDefined(command.Kind) || now == default)
            throw new InvalidOperationException("An identified risk command and recording time are required.");
        AiPortfolioRiskState result = command.Kind switch
        {
            AiRiskTransitionKind.Start when previous is null && command.Valuation is not null && command.Closure is null && command.Entry is null
                => policy.Start(command.Valuation, now),
            AiRiskTransitionKind.Observe when previous is not null && command.Valuation is not null && command.Closure is null && command.Entry is null
                => policy.Observe(previous, command.Valuation, now),
            AiRiskTransitionKind.ConfirmClosure when previous is not null && command.Closure is not null && command.Valuation is null && command.Entry is null
                => policy.ConfirmClosure(previous, command.Closure, now),
            AiRiskTransitionKind.BeginNextRound when previous is not null && command.Entry is not null && command.Valuation is null && command.Closure is null
                => policy.BeginNextRound(previous, command.Entry, now),
            _ => throw new InvalidOperationException("Risk command does not match the current lifecycle or payload.")
        };
        if (result.PortfolioId != scope.PortfolioId)
            throw new InvalidOperationException("Risk evidence belongs to another portfolio.");
        return result;
    }

    private void Validate(History history)
    {
        if (history.Version != 1 || history.Scope != scope || history.Transitions is null)
            throw new InvalidDataException("Risk history scope or version does not match.");
        AiPortfolioRiskState? previous = null;
        DateTimeOffset lastAt = default;
        var commands = new HashSet<Guid>();
        var rounds = new HashSet<Guid>();
        var sequences = new HashSet<Guid>();
        var evidence = new Dictionary<Guid, string>();
        for (int i = 0; i < history.Transitions.Count; i++)
        {
            var t = history.Transitions[i];
            if (t is null || t.Command is null || t.State is null || t.Revision != i + 1L ||
                t.Command.ExpectedRevision != i || t.RecordedAt < lastAt || !commands.Add(t.Command.CommandId))
                throw new InvalidDataException("Risk history ordering is inconsistent.");
            var expected = Execute(previous, t.Command, t.RecordedAt);
            // Only policy-created identifiers are nondeterministic; all financial/lifecycle fields must replay exactly.
            if (t.Command.Kind is AiRiskTransitionKind.Start or AiRiskTransitionKind.BeginNextRound)
            {
                if (t.State.RoundId == Guid.Empty || !rounds.Add(t.State.RoundId))
                    throw new InvalidDataException("Risk round identity was reused.");
                expected = expected with { RoundId = t.State.RoundId };
                if (previous is null || previous.Phase == AiRiskRoundPhase.SequencePaused)
                {
                    if (t.State.SequenceId == Guid.Empty || !sequences.Add(t.State.SequenceId))
                        throw new InvalidDataException("Risk sequence identity was reused.");
                    expected = expected with { SequenceId = t.State.SequenceId };
                }
            }
            if (expected != t.State) throw new InvalidDataException("Risk state does not match its recorded evidence.");
            var valuation = t.Command.Valuation ?? t.Command.Entry?.Valuation;
            if (valuation is not null) CheckEvidence(valuation.EvidenceId, Serialize(valuation));
            if (t.Command.Closure is { } closure) CheckEvidence(closure.EvidenceId, Serialize(closure));
            previous = t.State;
            lastAt = t.RecordedAt;
        }
        void CheckEvidence(Guid id, string payload)
        {
            if (evidence.TryGetValue(id, out var old) && old != payload)
                throw new InvalidDataException("Risk evidence identity has conflicting contents.");
            evidence[id] = payload;
        }
    }

    private History Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(path) || File.Exists(path + ".pending") || new FileInfo(path).Length > MaximumBytes)
            throw new InvalidDataException("Risk history is missing or requires reviewed recovery; new risk is blocked.");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            if (envelope.Payload is null || envelope.Sha256 != Hash(envelope.Payload)) throw new JsonException();
            var history = JsonSerializer.Deserialize<History>(envelope.Payload, Json) ?? throw new JsonException();
            Validate(history);
            if (knownRevision >= 0 && (knownRevision != history.Transitions.Count || knownHash != envelope.Sha256))
                throw new InvalidDataException("Risk history changed outside its owner; reviewed recovery is required.");
            knownRevision = history.Transitions.Count;
            knownHash = envelope.Sha256;
            return history;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException("Risk history is invalid; reviewed recovery is required."); }
    }

    private void Save(History history)
    {
        string payload = Serialize(history);
        string hash = Hash(payload);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, hash), Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Risk history requires reviewed retention before further writes.");
        using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { file.Write(bytes); file.Flush(true); }
        File.Move(path + ".pending", path, overwrite: true);
        knownRevision = history.Transitions.Count;
        knownHash = hash;
    }

    private static AiRiskJournalSnapshot Snapshot(History history) => new(history.Transitions.Count, history.Transitions.LastOrDefault()?.State);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; lease.Dispose(); } }
}
