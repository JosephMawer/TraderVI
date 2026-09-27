#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Oracle.Learning;

/// <summary>
/// Private, single-owner learning history. Only trusted outcome evaluators may publish snapshots.
/// Model hypotheses have no publication path. The host must protect and preserve this directory.
/// </summary>
public sealed class AiLearningJournal : IDisposable
{
    private readonly string path;
    private readonly FileStream lease;
    private readonly object sync = new();
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record State(int Version, List<AiLearningSnapshot> Snapshots,
        List<AiLearningConsultation> Consultations, List<AiLearningReview> Reviews,
        List<AiExecutionLearningCheck> ExecutionChecks);
    private sealed record Envelope(string Payload, string Sha256);

    public AiLearningJournal(string directory, bool initializeNew = false)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute private learning directory is required.");
        if (initializeNew) Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "ai-learning.json");
        string marker = Path.Combine(directory, "ai-learning.lock");
        if (initializeNew && File.Exists(marker)) throw new InvalidOperationException("Learning history was already initialized.");
        lease = new FileStream(marker, initializeNew ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (initializeNew)
            {
                if (File.Exists(path) || File.Exists(path + ".pending")) throw new InvalidOperationException("Learning history already exists.");
                Save(new(1, [], [], [], []));
            }
            _ = Read();
        }
        catch { lease.Dispose(); throw; }
    }

    public void Publish(AiLearningSnapshot snapshot, DateTimeOffset now)
    {
        AiLearningContracts.Validate(snapshot);
        if (snapshot.PublishedUtc != now || snapshot.ExpiresUtc <= now)
            throw new InvalidDataException("Learning publication must record its actual current availability time.");
        lock (sync)
        {
            var state = Read();
            CheckClock(state, now);
            if (snapshot.Version != state.Snapshots.Count + 1L || state.Snapshots.Any(s => s.SnapshotId == snapshot.SnapshotId) ||
                state.Snapshots.Any(s => s.PublishedUtc > snapshot.PublishedUtc) ||
                state.Consultations.Any(c => c.ConsultedUtc > snapshot.PublishedUtc))
                throw new InvalidOperationException("Learning publications must advance without backdating or reusing identity.");
            // A lesson ID identifies immutable content; changing a lesson requires a new ID.
            foreach (var lesson in snapshot.Lessons)
                if (state.Snapshots.SelectMany(s => s.Lessons).Any(old => old.LessonId == lesson.LessonId &&
                    JsonSerializer.Serialize(old, Json) != JsonSerializer.Serialize(lesson, Json)))
                    throw new InvalidOperationException("An evaluated lesson cannot be rewritten under its existing ID.");
            state.Snapshots.Add(snapshot);
            Save(state);
        }
    }

    /// <summary>Returns null only when a periodic consultation is not due. Actions force a fresh consultation.</summary>
    public AiLearningContext? Consult(string consumer, string contextId, string accountMode,
        ImmutableArray<AiLearningScope> scopes, DateTimeOffset now, TimeSpan interval, bool force = false)
    {
        if (consumer is not ("supervisor" or "execution") || !AiLearningContracts.Text(contextId, 256) ||
            !AiLearningContracts.ValidMode(accountMode) || scopes.IsDefaultOrEmpty || scopes.Length > 100 ||
            scopes.Any(s => !AiLearningContracts.ValidScope(s)) || scopes.Distinct().Count() != scopes.Length ||
            interval <= TimeSpan.Zero || interval > TimeSpan.FromDays(1))
            throw new ArgumentException("Learning consultation requires an exact consumer, scope and bounded interval.");
        scopes = scopes.OrderBy(s => s.PortfolioId, StringComparer.Ordinal).ThenBy(s => s.StrategyVersion, StringComparer.Ordinal).ToImmutableArray();
        lock (sync)
        {
            var state = Read();
            CheckClock(state, now);
            var snapshot = state.Snapshots.LastOrDefault();
            if (snapshot is null || snapshot.PublishedUtc > now || snapshot.ExpiresUtc <= now)
                throw new InvalidDataException("No current evaluated-learning snapshot is available.");
            var lessons = snapshot.Lessons.Where(l => l.AccountMode == accountMode && scopes.Contains(l.Scope) && l.ExpiresUtc > now).ToImmutableArray();
            var ids = lessons.Select(l => l.LessonId).ToImmutableArray();
            var last = state.Consultations.LastOrDefault(c => c.Consumer == consumer && c.ContextId == contextId &&
                c.AccountMode == accountMode && c.Scopes.SequenceEqual(scopes));
            if (!force && last is not null && last.SnapshotId == snapshot.SnapshotId &&
                last.LessonIds.SequenceEqual(ids) && now - last.ConsultedUtc < interval) return null;
            var consultation = new AiLearningConsultation(Guid.NewGuid(), consumer, contextId, accountMode, scopes,
                now, snapshot.SnapshotId, snapshot.Version, ids);
            state.Consultations.Add(consultation);
            Save(state);
            return new(consultation.ConsultationId, snapshot.SnapshotId, snapshot.Version, accountMode,
                snapshot.PublishedUtc, snapshot.ExpiresUtc, lessons);
        }
    }

    internal void RecordReview(AiLearningReview review)
    {
        lock (sync)
        {
            var state = Read();
            CheckClock(state, review.RecordedUtc);
            var consultation = state.Consultations.SingleOrDefault(c => c.ConsultationId == review.ConsultationId);
            var existing = state.Reviews.SingleOrDefault(r => r.ReviewId == review.ReviewId);
            if (review.ReviewId == Guid.Empty || review.ReviewId != review.Packet.PacketId ||
                consultation is null || consultation.Consumer != "supervisor" ||
                review.Packet.Learning is not { } learning || learning.ConsultationId != consultation.ConsultationId ||
                learning.SnapshotId != consultation.SnapshotId || learning.SnapshotVersion != consultation.SnapshotVersion ||
                learning.AccountMode != consultation.AccountMode ||
                !learning.Lessons.Select(l => l.LessonId).SequenceEqual(consultation.LessonIds) ||
                review.Status is not ("Pending" or "Reviewed" or "Rejected") || (review.Status == "Reviewed") != (review.Proposal is not null) ||
                (existing is null ? review.Status != "Pending" : existing.Status != "Pending" || review.Status == "Pending" ||
                    existing.ConsultationId != review.ConsultationId || JsonSerializer.Serialize(existing.Packet, Json) != JsonSerializer.Serialize(review.Packet, Json)))
                throw new InvalidDataException("Learning review does not match its original consultation.");
            var snapshot = state.Snapshots.Single(s => s.SnapshotId == consultation.SnapshotId);
            if (learning.PublishedUtc != snapshot.PublishedUtc || learning.ExpiresUtc != snapshot.ExpiresUtc ||
                JsonSerializer.Serialize(learning.Lessons, Json) != JsonSerializer.Serialize(
                    snapshot.Lessons.Where(l => consultation.LessonIds.Contains(l.LessonId)).ToImmutableArray(), Json))
                throw new InvalidDataException("Learning review cannot rewrite consulted lesson evidence.");
            if (review.Status == "Pending") AiPortfolioReviewer.ValidatePacket(review.Packet, review.RecordedUtc);
            if (review.Proposal is not null)
            {
                AiPortfolioReviewer.ValidatePacket(review.Packet, review.RecordedUtc);
                AiLearningContracts.ValidateAssessments(learning, review.Proposal.LessonAssessments);
                _ = AiPortfolioReviewer.ParseAndValidate(JsonSerializer.Serialize(review.Proposal,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), review.Packet);
            }
            if (existing is null) state.Reviews.Add(review);
            else state.Reviews[state.Reviews.IndexOf(existing)] = review;
            Save(state);
        }
    }

    public AiLearningReview ReadReview(Guid id)
    {
        lock (sync) return Read().Reviews.SingleOrDefault(r => r.ReviewId == id)
            ?? throw new InvalidOperationException("No recorded supervisor review exists.");
    }

    internal void RecordExecutionCheck(AiExecutionLearningCheck check)
    {
        lock (sync)
        {
            var state = Read();
            CheckClock(state, check.RecordedUtc);
            var consultation = state.Consultations.SingleOrDefault(c => c.ConsultationId == check.ConsultationId);
            var review = state.Reviews.SingleOrDefault(r => r.ReviewId == check.ReviewId);
            if (check.CheckId == Guid.Empty || state.ExecutionChecks.Any(c => c.CheckId == check.CheckId) ||
                review?.Proposal is null || consultation is null || consultation.Consumer != "execution" ||
                consultation.ContextId != check.ReviewId.ToString("D") || consultation.AccountMode != review.Packet.Learning?.AccountMode ||
                check.Status is not ("Current" or "ReviewRequired") || !AiLearningContracts.Text(check.Reason, 4000) || check.Assessments.IsDefault)
                throw new InvalidDataException("Execution learning check is not linked to its review and consultation.");
            if (check.Status == "Current")
            {
                var latest = state.Snapshots.Last();
                AiPortfolioReviewer.ValidatePacket(review.Packet, check.RecordedUtc);
                if (latest.SnapshotId != consultation.SnapshotId || latest.SnapshotId != review.Packet.Learning!.SnapshotId ||
                    latest.ExpiresUtc <= check.RecordedUtc ||
                    !consultation.LessonIds.SequenceEqual(review.Packet.Learning.Lessons.Select(l => l.LessonId)))
                    throw new InvalidDataException("Learning changed while execution preparation was being recorded.");
                AiLearningContracts.ValidateAssessments(review.Packet.Learning, check.Assessments.ToArray());
                if (!check.Assessments.Select(a => (a.LessonId, a.Disposition)).OrderBy(a => a.LessonId)
                    .SequenceEqual(review.Proposal.LessonAssessments!.Select(a => (a.LessonId, a.Disposition)).OrderBy(a => a.LessonId)))
                    throw new InvalidDataException("Execution cannot reinterpret the supervisor's lesson assessments.");
            }
            state.ExecutionChecks.Add(check);
            Save(state);
        }
    }

    public ImmutableArray<AiLearningConsultation> ReadConsultations() { lock (sync) return Read().Consultations.ToImmutableArray(); }
    public ImmutableArray<AiExecutionLearningCheck> ReadExecutionChecks() { lock (sync) return Read().ExecutionChecks.ToImmutableArray(); }

    private static void CheckClock(State state, DateTimeOffset now)
    {
        if (state.Snapshots.Any(s => s.PublishedUtc > now) || state.Consultations.Any(c => c.ConsultedUtc > now) ||
            state.Reviews.Any(r => r.RecordedUtc > now) || state.ExecutionChecks.Any(c => c.RecordedUtc > now))
            throw new InvalidOperationException("Learning history cannot move backward in time.");
    }

    private State Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!File.Exists(path) || File.Exists(path + ".pending") || new FileInfo(path).Length > 128_000_000)
            throw new InvalidDataException("Learning history is missing or requires reviewed recovery.");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            if (envelope.Payload is null || envelope.Sha256 != Hash(envelope.Payload)) throw new JsonException();
            var state = JsonSerializer.Deserialize<State>(envelope.Payload, Json) ?? throw new JsonException();
            if (state.Version != 1 || state.Snapshots is null || state.Consultations is null || state.Reviews is null || state.ExecutionChecks is null)
                throw new JsonException();
            return state;
        }
        catch (JsonException) { throw new InvalidDataException("Learning history is invalid; consultation is unavailable."); }
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private void Save(State state)
    {
        string payload = JsonSerializer.Serialize(state, Json);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, Hash(payload)), Json);
        if (bytes.Length > 128_000_000) throw new InvalidDataException("Learning history requires reviewed retention before further writes.");
        using (var file = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { file.Write(bytes); file.Flush(true); }
        File.Move(path + ".pending", path, overwrite: true);
    }
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; lease.Dispose(); } }
}
