#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraderVI.WPF.Viewmodels;

public sealed record EngineHealthRequirement(string Code, string State, string Detail)
{
    [JsonIgnore] public string Label => State + " · " + Detail;
}

// Read-only evidence, never an eligibility grant. The engine owns atomic clearance checks.
public sealed record EngineHealthRecovery(Guid IncidentId, Guid? RunId, string Scope, string RunState,
    long? VerifiedUnits, int? CompletedGroups, int? TotalGroups, string CurrentGroup,
    DateTimeOffset? CheckpointAt, double? UnitsPerSecond, string Progress, string Clearance,
    IReadOnlyList<EngineHealthRequirement> Requirements)
{
    [JsonIgnore] public string NavigationLabel => "Review " + Scope + " recovery…";
    public string ActionBoundary => "Opens the existing recovery controls for this incident and scope. Opening does not start verification or clear a hold. A completed pass never enables Real trading.";

    public static Guid[] SelectIncidentIds(EngineIntegrityStatus status, EngineAuditRun run, EngineRecoveryScope scope, Guid? focusedIncident) =>
        status.Incidents.Where(i => (scope switch
        {
            EngineRecoveryScope.Real => i.Scope is EngineIntegrityScope.Global or EngineIntegrityScope.Broker,
            EngineRecoveryScope.Paper => i.Scope is EngineIntegrityScope.Paper or EngineIntegrityScope.Research,
            _ => true
        }) && (focusedIncident is null || i.IncidentId == focusedIncident ||
            i.FailureCode == "RecoveryVerificationPending" && i.EvidenceKey == "Recovery/" + run.RunId.ToString("D")))
        .Select(i => i.IncidentId).ToArray();

    internal static EngineHealthRecovery Describe(EngineIntegrityIncident incident, EngineAuditRun? run,
        EngineAuditRun? previous, bool fresh, DateTimeOffset now)
    {
        var scope = incident.Scope is EngineIntegrityScope.Paper or EngineIntegrityScope.Research ? EngineRecoveryScope.Paper
            : incident.Scope == EngineIntegrityScope.Broker ? EngineRecoveryScope.Real : EngineRecoveryScope.All;
        bool complete = run?.Status == "Complete" && run.Progress.Complete;
        bool checkpointValid = run != null && run.UpdatedUtc != default && run.UpdatedUtc <= now.UtcDateTime;
        double? rate = null;
        if (fresh && run?.Status == "Running" && checkpointValid && previous?.RunId == run.RunId &&
            previous.Status == "Running" && previous.Plan.BoundaryUtc == run.Plan.BoundaryUtc &&
            previous.Plan.Scope == run.Plan.Scope && previous.Plan.Targets.SequenceEqual(run.Plan.Targets) &&
            previous.Progress.TargetIndex <= run.Progress.TargetIndex && previous.UpdatedUtc != default &&
            run.UpdatedUtc > previous.UpdatedUtc && run.Progress.VerifiedUnits > previous.Progress.VerifiedUnits)
            rate = (run.Progress.VerifiedUnits - previous.Progress.VerifiedUnits) / (run.UpdatedUtc - previous.UpdatedUtc).TotalSeconds;
        string group = run is null ? "No matching run returned" : run.Progress.Complete ? "All planned groups checked"
            : run.Progress.TargetIndex >= 0 && run.Progress.TargetIndex < run.Plan.Targets.Length
                ? run.Plan.Targets[run.Progress.TargetIndex].Family switch
                {
                    EngineAuditFamily.PaperRevisions => "Paper portfolio history",
                    EngineAuditFamily.LegacyInputs => "Historical research inputs",
                    EngineAuditFamily.BrokerRevisions => "Real-account history",
                    EngineAuditFamily.SharedInputs => "Shared market inputs",
                    EngineAuditFamily.SharedContent => "Shared evidence content",
                    _ => "Unknown history group"
                } : "Group unavailable";
        string movement = !fresh ? "Last-known receipt; refresh before relying on progress."
            : !checkpointValid ? "Checkpoint time is unavailable or invalid."
            : run!.Status != "Running" ? "Saved pass result; no active progress inferred."
            : now.UtcDateTime - run.UpdatedUtc > TimeSpan.FromMinutes(15) ? "No checkpoint for over 15 minutes. Inspect the audit worker before assuming the pass is stuck."
            : rate is { } speed ? $"Progress observed · {speed:N1} records/second between saved checkpoints."
            : "Waiting for a second advancing checkpoint to measure speed; Running alone does not prove progress.";
        string progress = run is null ? "No matching recovery receipt is available."
            : $"{run.Status} · {run.Progress.VerifiedUnits:N0} records checked · {run.Progress.TargetIndex} of {run.Plan.Targets.Length} groups complete.\n" +
              "Checking · " + group + ".\n" + movement + "\n" +
              (checkpointValid ? "Last saved checkpoint · " + EngineHealthIssue.Local(run.UpdatedUtc) + ". " : "") +
              "Groups vary in cost and size; a reliable percentage or finish time is not available.";
        string clearance = !fresh ? "Cannot assess — current restriction status is unverified."
            : run is null ? "Not ready — no recovery proof covering this incident was returned."
            : run.Status == "Failed" ? "Not ready — verification failed. Inspect the failing evidence; do not clear or retry blindly."
            : !complete ? "Not ready — the entire recovery pass must complete successfully."
            : "Verification complete — live clearance checks and an explicit clearance action are still required.";
        var requirements = new List<EngineHealthRequirement> {
            new("CurrentReceipt", fresh ? "Observed" : "Unknown", "A fresh integrity-status read must be available."),
            new("ScopeAndIncident", fresh && run != null ? "Receipt matches" : "Unverified",
                "Recovery must cover this incident's scope and original boundary; a pending hold must match its exact run."),
            new("CompletedPass", !fresh ? "Unknown" : complete ? "Passed in receipt" : run?.Status == "Failed" ? "Failed" : "Pending",
                "Run status must be Complete and every planned group must be verified."),
            new("CurrentHistory", "Engine recheck required", "Current history boundaries must still match the verified plan exactly."),
            new("ContinuityAndGuards", "Engine recheck required", "Restart continuity, database identity and immutable-history guards must remain valid."),
            new("ExplicitClearance", "Explicit action required", "Select only covered unresolved incidents and supply a truthful reason; the engine validates them together before clearing.")
        };
        if (scope == EngineRecoveryScope.Real)
            requirements.Insert(5, new("CurrentRealState", "Engine recheck required",
                "Real-only clearance also validates saved audit receipts and reconstructed broker history against current observation subscriptions."));
        return new(incident.IncidentId, run?.RunId, scope.ToString(), run?.Status ?? "Unavailable", run?.Progress.VerifiedUnits,
            run?.Progress.TargetIndex, run?.Plan.Targets.Length, group, checkpointValid ? run!.UpdatedUtc : null,
            rate, progress, clearance, requirements);
    }
}

// Display-only, allowlisted diagnostic contract. No broker balances, credentials, owner IDs,
// raw exceptions, database identities or mutation commands enter the export.
public sealed record EngineHealthIssue(string Id, string Code, string Category, string Domain,
    string Title, string Explanation, string Impact, string NextStep, string Evidence,
    DateTimeOffset? FirstObservedAt, DateTimeOffset? ObservedAt, string Source,
    string Certainty = "Reported", string Action = "Inspect", bool RequiresApproval = false)
{
    public EngineHealthRecovery? Recovery { get; init; }
    [JsonIgnore] public bool HasRecovery => Recovery != null;
    [JsonIgnore] public string Heading => $"{Domain} · {Category}";
    [JsonIgnore] public string Color => Category == "Restriction" ? "#FF7D87" : Category == "Warning" ? "#F5B94C" : "#ADBBCC";
    [JsonIgnore] public string Timing => (FirstObservedAt is { } first ? "First recorded " + Local(first) + ". " : "") +
        (ObservedAt is { } at ? "Observed " + Local(at) + "." : "Observation time unavailable.");
    internal static string Local(DateTimeOffset value) => PaperTradingMonitor.ToToronto(value.UtcDateTime).ToString("MMM d, yyyy HH:mm:ss") + " Toronto";
}

public sealed record EngineHealthSnapshot(DateTimeOffset GeneratedAt, string EngineState,
    IReadOnlyList<EngineHealthIssue> Issues, IReadOnlyList<EngineHealthIssue> History)
{
    public string Contract => "TraderVI.EngineHealth.v1";
    public string Coverage => "Reported integrity restrictions, worker failures, execution status, collector, daily prep, connection and holdings freshness. " +
        "Execution reports its latest outcome, not every possible order-level check. History contains the engine's retained audit receipts and notices, not a complete event log. " +
        "Unknown causes stay unknown. Reading or copying this snapshot does not authorize repairs, verification runs, hold clearance or trading.";
    [JsonIgnore] public IEnumerable<EngineHealthIssue> Restrictions => Issues.Where(i => i.Category == "Restriction");
    [JsonIgnore] public IEnumerable<EngineHealthIssue> Warnings => Issues.Where(i => i.Category == "Warning");
    [JsonIgnore] public IEnumerable<EngineHealthIssue> Waiting => Issues.Where(i => i.Category == "Status");
    [JsonIgnore] public string Summary => $"Engine · {EngineState}    |    {Restrictions.Count()} reported restrictions    |    {Warnings.Count()} warnings or verification gaps";
    public string ToDiagnosticJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static EngineHealthSnapshot Build(IndependentEngineStatus? engine, EngineIntegrityStatus? integrity,
        DateTimeOffset? integrityObservedAt, bool integrityReadFailed, BrokerExecutionHostStatus? execution,
        DateTimeOffset now, IEnumerable<EngineHealthIssue>? signals = null, EngineIntegrityStatus? previousIntegrity = null)
    {
        var issues = new List<EngineHealthIssue>();
        var history = new List<EngineHealthIssue>();
        bool engineFresh = Fresh(engine?.CheckedAt, now);
        bool integrityFresh = integrity != null && !integrityReadFailed && Fresh(integrityObservedAt, now);
        if (!engineFresh)
            issues.Add(new("engine-unverified", "EngineStatusUnavailable", "Warning", "Engine", "Engine status cannot be verified",
                "No current engine receipt is available. A retained Running label is not proof that the process or protection is working.",
                "Current background work and protection cannot be established from this screen.", "Open Controls and inspect the engine connection before taking operational action.",
                "Receipts older than two minutes or dated in the future are not current.", null, engine?.CheckedAt, "Engine.Status"));
        else if (engine!.State != "Running" || engine.VerificationOnly || engine.RecoveryMaintenance)
            issues.Add(new("engine-mode", "EngineMode", "Status", "Engine", engine.VerificationOnly ? "Verification mode" : engine.RecoveryMaintenance ? "Recovery maintenance mode" : "Engine " + engine.State,
                "The engine reports this operating mode. Process state alone does not establish trading permission.",
                "Normal automatic work may be stopped or restricted by the selected mode.", "Review the intended operating mode in Controls.",
                "Reported state · " + engine.State, null, engine.CheckedAt, "Engine.Status"));
        if (!integrityFresh)
            issues.Add(new("integrity-unverified", "IntegrityStatusUnavailable", "Warning", "History", "Current restrictions could not be verified",
                integrityReadFailed ? "The latest history-status read failed. Previously read restrictions remain visible below." : "History status has not been loaded or is stale.",
                "An empty or old list must not be interpreted as all clear.", "Use Refresh status. If this persists, inspect the engine connection in Controls.",
                "No hold is cleared by a failed read.", null, integrityObservedAt, "Engine.IntegrityStatus"));
        if (integrity != null)
        {
            foreach (var incident in integrity.Incidents.OrderBy(i => i.Scope is EngineIntegrityScope.Global or EngineIntegrityScope.Broker ? 0 : 1).ThenBy(i => i.DiscoveredUtc))
                issues.Add(DescribeIncident(incident, integrity, integrityObservedAt, integrityFresh, now, previousIntegrity));
            foreach (var run in integrity.Runs.OrderByDescending(r => r.Plan.BoundaryUtc)) history.Add(DescribeAudit(run));
            // ReadStatus retains the latest receipt per kind/status/scope. Older failures can
            // remain alongside later success; they are history, not a fresh active failure.
            foreach (var group in integrity.Runs.GroupBy(r => (r.Plan.Kind, r.Plan.Scope)))
            {
                var latest = group.OrderByDescending(r => r.Plan.BoundaryUtc).ThenByDescending(r => r.UpdatedUtc).First();
                foreach (var run in group.Where(r => r.Status == "Running" || r.RunId == latest.RunId && r.Status != "Complete"))
                {
                    var item = DescribeAudit(run);
                    issues.Add(item with { Category = run.Status == "Running" ? "Status" : "Warning",
                        Title = (integrityFresh ? "" : "Last known · ") + item.Title, ObservedAt = integrityObservedAt });
                }
            }
            var archive = integrity.Runs.Where(r => r.Plan.Kind == EngineAuditKind.Archive && r.Status == "Complete")
                .OrderByDescending(r => r.Plan.BoundaryUtc).FirstOrDefault();
            if (archive is null || now.UtcDateTime - archive.Plan.BoundaryUtc > TimeSpan.FromDays(7))
                issues.Add(new("archive-coverage", "ArchiveCoverage", "Warning", "History", archive is null ? "Archive coverage is unverified" : "Archive verification is overdue",
                    archive is null ? "No completed archive audit was returned." : "The newest completed archive boundary is over seven days old.",
                    "Older history has not been qualified through a current archive boundary.", "Review the audit receipts in History and investigate unfinished or failed checks.",
                    archive is null ? "Coverage is unknown." : "Last completed boundary · " + EngineHealthIssue.Local(archive.Plan.BoundaryUtc), null, integrityObservedAt, "Engine.IntegrityStatus"));
            if (integrity.PaperContinuation is { } continuation)
                issues.Add(new("paper-continuation", "PaperContinuation", "Warning", "Paper", "Temporary Paper continuation is recorded",
                    "An expiring continuation record does not clear historical integrity findings. Its applicability must be checked by the engine.",
                    "Do not infer that every Paper or Research restriction has been lifted.", "Inspect the original continuation and recovery receipts before relying on continued work.",
                    "Expires · " + EngineHealthIssue.Local(continuation.ExpiresUtc), continuation.CreatedUtc, integrityObservedAt, "Engine.IntegrityStatus"));
        }
        if (engine != null)
        {
            var blocked = engine.Work.Where(w => w.State == "Blocked").ToArray();
            if (blocked.Length > 0)
                issues.Add(new("worker-failures", "WorkerFailures", "Warning", "Background tasks", (engineFresh ? "" : "Last known · ") + $"{blocked.Length} tasks reported blocked",
                    "A task threw an exception on its last attempt. The receipt records the exception type, but not the exact cause or a link to an integrity incident.",
                    string.Join("; ", blocked.Select(w => w.Name)) + ". Existing restrictions may explain these failures; that relationship is not recorded per attempt.",
                    "Review applicable restrictions first. If a task remains blocked after those are resolved, investigate its original failure evidence. Do not restart or clear a hold solely to hide this warning.",
                    string.Join("\n", blocked.Select(w => $"{w.Name} · {w.LastFailureType ?? "type unavailable"} · {w.ConsecutiveFailures} consecutive failures · last failure {(w.LastFailed is { } at ? EngineHealthIssue.Local(at) : "unavailable")}")),
                    null, engine.CheckedAt, "Engine.Status.Work", "Cause not established"));
            foreach (var notice in engine.Notices.OrderByDescending(n => n.ObservedAt))
                history.Add(new("notice/" + notice.Code + "/" + notice.ObservedAt.ToString("O"), notice.Code, "Status", "Engine notice", notice.Code + " · " + notice.Severity,
                    notice.Message, "A historical notice alone does not prove that a condition is still active or resolved.",
                    "Compare with current restrictions and the latest status before acting.", "Original engine notice.", notice.ObservedAt, notice.ObservedAt, "Engine.Status.Notices"));
            var warnings = engine.Notices.Where(n => n.Code != "IntegrityCoverage").GroupBy(n => n.Code)
                .Select(g => g.OrderByDescending(n => n.ObservedAt).First()).Where(n => n.Severity is "Warning" or "Error").ToArray();
            if (warnings.Length > 0)
                issues.Add(new("reported-warnings", "ReportedWarnings", "Warning", "Engine notices", "Additional warning reports need context",
                    "These are the latest retained warning/error notices for their codes. The notice feed does not record whether each condition is still active or resolved.",
                    "These reports may overlap the restrictions above. Do not count them as additional confirmed root causes.",
                    "Compare the dated reports with current status and inspect their original evidence in History.",
                    string.Join("\n", warnings.Select(n => $"{n.Code} · {EngineHealthIssue.Local(n.ObservedAt)} · {n.Message}")),
                    null, engine.CheckedAt, "Engine.Status.Notices", "Current applicability unknown"));
        }
        var readiness = ExecutionReadiness.From(execution, now);
        issues.Add(new("real-execution", "Execution/" + readiness.Title, readiness.Title == "Blocked" ? "Restriction" :
                readiness.Title == "Unable to verify" ? "Warning" : "Status", "Real account", readiness.Title,
            readiness.Detail, "This is the host's latest execution outcome. Other account, order, price and risk checks remain independent.",
            "Inspect Real execution in Controls for its current reason and existing review actions.", readiness.Checked, null, execution?.CheckedAt, "Engine.ExecutionStatus"));
        if (signals != null) issues.AddRange(signals);
        // Startup DTOs use default timestamps until their first receipt. Export null, not year 1.
        static EngineHealthIssue Normalize(EngineHealthIssue item) => item with
        {
            ObservedAt = item.ObservedAt == default(DateTimeOffset) ? null : item.ObservedAt,
            FirstObservedAt = item.FirstObservedAt == default(DateTimeOffset) ? null : item.FirstObservedAt
        };
        return new(now, engineFresh ? engine!.State : "Unverified", issues.Select(Normalize).ToArray(), history.Select(Normalize).ToArray());
    }

    private static EngineHealthIssue DescribeIncident(EngineIntegrityIncident incident, EngineIntegrityStatus status, DateTimeOffset? observed, bool fresh,
        DateTimeOffset now, EngineIntegrityStatus? previous)
    {
        bool inherited = incident.FailureCode == "PaperRecoveryRequired" && incident.Scope == EngineIntegrityScope.Paper &&
            incident.OriginDiscoveredUtc != null && incident.OriginEvidenceKey != null;
        bool continuity = inherited && incident.OriginEvidenceKey!.StartsWith("EngineContinuity/", StringComparison.Ordinal);
        var boundary = incident.OriginDiscoveredUtc ?? incident.DiscoveredUtc;
        bool pending = incident.FailureCode == "RecoveryVerificationPending";
        var recovery = status.Runs.Where(r => r.Plan.Kind == EngineAuditKind.Recovery && r.Plan.BoundaryUtc <= now.UtcDateTime &&
            (pending ? incident.EvidenceKey == "Recovery/" + r.RunId.ToString("D") : r.Plan.BoundaryUtc >= boundary) &&
            (incident.Scope switch {
                EngineIntegrityScope.Paper or EngineIntegrityScope.Research => r.Plan.Scope is EngineRecoveryScope.All or EngineRecoveryScope.Paper,
                EngineIntegrityScope.Broker => r.Plan.Scope == EngineRecoveryScope.Real,
                _ => r.Plan.Scope == EngineRecoveryScope.All }))
            .OrderByDescending(r => r.Plan.BoundaryUtc).FirstOrDefault();
        string explanation = pending
            ? "Starting verification creates a temporary restriction for that recovery run. This is expected while verification is incomplete and remains until explicit guarded clearance. It is not a second diagnosis of damaged history."
            : continuity
            ? "At startup, the engine could not establish trusted continuity with its saved state. Real-account recovery subsequently passed, leaving Paper history outside that proof. The original receipt does not identify whether the cause was an unclean shutdown, missing evidence or a mismatch. This restriction is not proof of corrupted Paper data."
            : inherited ? "A completed Real-only recovery left this Paper restriction in place. Real recovery does not verify Paper history. The original trigger requires inspection."
            : incident.FailureCode is "EngineRecoveryRequired" or "EngineContinuityLost"
                ? "The engine could not establish trusted continuity between its local restart record and database state. The recorded code does not identify the exact mismatch or prove data corruption."
                : "An unresolved integrity restriction is recorded. Its exact cause is not explained by the available status contract; inspect the original evidence before selecting a remedy.";
        string verification = recovery is null ? "No returned recovery check for this scope covers the original incident. Older successful checks do not cover a later interruption."
            : $"Latest covering recovery · {recovery.Plan.Scope} / {recovery.Status}. A completed run still needs current proof checks and explicit clearance; it is not clearance itself.";
        string next = recovery?.Status == "Running" ? "Monitor the existing recovery in History. Inspect its progress before starting another pass."
            : recovery?.Status == "Failed" ? "Inspect the failed recovery evidence in History, address the cause, then review a new verification pass."
            : recovery?.Status == "Complete" ? "Open History → History checks and recovery. Review whether the completed proof is still eligible; explicitly clear only qualified holds with a reason."
            : "Open History → History checks and recovery. Select the affected scope and review starting verification. It checks saved history; it does not repair it. Clearance is a separate explicit step after a qualifying pass.";
        string title = incident.FailureCode switch
        {
            "PaperRecoveryRequired" => "Paper history has not been verified after an interruption",
            "EngineRecoveryRequired" => "Saved state needs verification after restart",
            "EngineContinuityLost" => "Engine and database continuity could not be verified",
            "RecoveryVerificationPending" => "Recovery verification has not been cleared",
            "ArchiveVerificationFailed" => "A history verification pass failed",
            _ => "Integrity restriction needs investigation · " + incident.FailureCode
        };
        return new("incident/" + incident.IncidentId.ToString("D"), incident.FailureCode, "Restriction",
            incident.Scope == EngineIntegrityScope.Broker ? "Real account" : incident.Scope == EngineIntegrityScope.Paper ? "Paper (simulated trading)" : incident.Scope.ToString(),
            (fresh ? "" : "Last known · ") + title,
            explanation + "\n" + verification,
            $"New positions are blocked in the affected scope. {(incident.ProtectionUntrusted ? "Affected automatic protection is unavailable under this restriction." : "This restriction does not itself disable protection; independent checks still apply.")} " +
            (status.PaperContinuation is not null ? "A continuation record exists; any temporary exception must be verified separately by the engine. " : "") +
            (incident.Scope == EngineIntegrityScope.Paper ? "Dependent research is restricted too. This Paper-scoped finding does not itself hold the Real account." : ""),
            next, $"Code · {incident.FailureCode}\nOriginal trigger · {EngineHealthIssue.Local(boundary)}\nRestriction recorded · {EngineHealthIssue.Local(incident.DiscoveredUtc)}\n" +
                (inherited ? "Verified origin link · " + incident.OriginEvidenceKey : "Evidence reference · " + incident.EvidenceKey),
            boundary, observed, "Engine.IntegrityStatus.Incidents", "Recorded restriction; exact initiating cause may be unknown", "ReviewRecovery", true)
        { Recovery = EngineHealthRecovery.Describe(incident, recovery, previous?.Runs.FirstOrDefault(r => r.RunId == recovery?.RunId), fresh, now) };
    }

    private static EngineHealthIssue DescribeAudit(EngineAuditRun run) => new("audit/" + run.RunId.ToString("D"), "Audit/" + run.Status,
        "Status", "History", $"{run.Plan.Kind} · {run.Plan.Scope} · {run.Status}",
        $"{run.Progress.VerifiedUnits:N0} records checked; {run.Progress.TargetIndex} of {run.Plan.Targets.Length} groups complete. " +
        "Updated " + EngineHealthIssue.Local(run.UpdatedUtc) + ".",
        run.Status == "Complete" ? "This receipt verifies its fixed history boundary. It does not clear restrictions or enable trading." : "This pass has not established complete coverage of its planned boundary.",
        run.Status == "Running" ? "Monitor progress in History checks and recovery. A Running receipt may be paused or awaiting its scheduled window; it does not prove current progress."
            : run.Status == "Complete" ? "Retain as evidence. Any hold clearance must recheck that the proof still applies."
            : "Inspect the failed or unfinished pass before deciding whether another verification is appropriate.",
        $"Run · {run.RunId:D}\nBoundary · {EngineHealthIssue.Local(run.Plan.BoundaryUtc)}\nScope · {run.Plan.Scope}",
        run.Plan.BoundaryUtc, run.UpdatedUtc, "Engine.IntegrityStatus.Runs");

    internal static bool Fresh(DateTimeOffset? at, DateTimeOffset now) => at is { } value && value != default && value <= now && now - value <= TimeSpan.FromMinutes(2);
}
