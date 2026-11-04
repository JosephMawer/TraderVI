using Core.Runtime;
using Core.Trader.Promotion;
using System;
using System.Linq;
using System.Text.Json;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class EngineHealthSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 21, 0, 0, TimeSpan.Zero);
    private static IndependentEngineStatus Engine => new(Guid.NewGuid(), "Running", Now, []);
    private static BrokerExecutionHostStatus Execution => new("Waiting", "Execution is waiting for a reviewed regular TSX session.", Now);
    private static EngineIntegrityIncident Paper => new(Guid.NewGuid(), EngineIntegrityScope.Paper, null,
        "ScopedFrom/" + Guid.NewGuid(), "PaperRecoveryRequired", Now.AddDays(-4).UtcDateTime, true)
    { OriginDiscoveredUtc = Now.AddDays(-4).AddHours(-1).UtcDateTime, OriginEvidenceKey = "EngineContinuity/" + Guid.NewGuid() };
    private static EngineAuditRun Audit(EngineRecoveryScope scope, string state, int days, EngineAuditKind kind = EngineAuditKind.Recovery) =>
        new(Guid.NewGuid(), new(kind, Now.AddDays(days).UtcDateTime, []) { Scope = scope },
            new(0, 0, null, 12, state == "Complete"), 1, state, Now.AddDays(days).AddMinutes(2).UtcDateTime);
    private static EngineHealthSnapshot Build(EngineIntegrityStatus status, bool failed = false) =>
        EngineHealthSnapshot.Build(Engine, status, Now, failed, Execution, Now);

    [Fact]
    public void Inherited_paper_hold_explains_real_only_proof_and_missing_new_paper_check()
    {
        var incident = Paper;
        var result = Build(new([incident], [Audit(EngineRecoveryScope.Real, "Complete", -3), Audit(EngineRecoveryScope.Paper, "Complete", -5)]));
        var issue = Assert.Single(result.Restrictions);
        Assert.Contains("Real-account recovery subsequently passed", issue.Explanation);
        Assert.Contains("not proof of corrupted", issue.Explanation);
        Assert.Contains("No returned recovery check for this scope", issue.Explanation);
        Assert.Contains("does not itself hold the Real account", issue.Impact);
        Assert.Equal(incident.OriginDiscoveredUtc, issue.FirstObservedAt?.UtcDateTime);
        Assert.True(issue.RequiresApproval);
    }

    [Theory]
    [InlineData("Running", "Monitor the existing recovery")]
    [InlineData("Failed", "Inspect the failed recovery")]
    [InlineData("Complete", "Review whether the completed proof is still eligible")]
    public void Next_step_uses_a_covering_paper_recovery_without_claiming_clearance(string state, string next)
    {
        var result = Build(new([Paper], [Audit(EngineRecoveryScope.Paper, state, -1)]));
        var issue = Assert.Single(result.Restrictions);
        Assert.Contains(next, issue.NextStep);
        Assert.Contains("not clearance itself", issue.Explanation);
    }

    [Fact]
    public void Original_cause_is_not_invented_when_the_origin_link_is_missing()
    {
        var incident = Paper with { OriginEvidenceKey = null, OriginDiscoveredUtc = null };
        var issue = Assert.Single(Build(new([incident], [])).Restrictions);
        Assert.DoesNotContain("Real-account recovery subsequently passed", issue.Explanation);
        Assert.Contains("exact cause is not explained", issue.Explanation);
    }

    [Fact]
    public void Later_completed_pass_keeps_older_failure_in_history_without_an_active_audit_warning()
    {
        var result = Build(new([], [Audit(EngineRecoveryScope.Real, "Failed", -5), Audit(EngineRecoveryScope.Real, "Complete", -1)]));
        Assert.DoesNotContain(result.Issues, i => i.Code == "Audit/Failed");
        Assert.Contains(result.History, i => i.Code == "Audit/Failed");
    }

    [Fact]
    public void Older_running_audit_remains_visible_even_if_a_newer_completed_receipt_exists()
    {
        var result = Build(new([], [Audit(EngineRecoveryScope.All, "Running", -5), Audit(EngineRecoveryScope.All, "Complete", -1)]));
        Assert.Contains(result.Waiting, i => i.Code == "Audit/Running");
    }

    [Fact]
    public void Failed_refresh_retains_findings_and_marks_them_last_known()
    {
        var result = Build(new([Paper], []), failed: true);
        Assert.StartsWith("Last known", Assert.Single(result.Restrictions).Title);
        Assert.Contains(result.Warnings, i => i.Code == "IntegrityStatusUnavailable");
    }

    [Theory]
    [InlineData(-121)]
    [InlineData(1)]
    public void Invalid_observation_times_cannot_produce_current_health(int seconds)
    {
        var result = EngineHealthSnapshot.Build(Engine with { CheckedAt = Now.AddSeconds(seconds) }, new([], []), Now.AddSeconds(seconds), false, Execution, Now);
        Assert.Equal("Unverified", result.EngineState);
        Assert.Contains(result.Warnings, i => i.Code == "IntegrityStatusUnavailable");
        Assert.Contains(result.Warnings, i => i.Code == "EngineStatusUnavailable");
    }

    [Fact]
    public void Missing_data_is_a_verification_gap_not_a_healthy_empty_list()
    {
        var result = EngineHealthSnapshot.Build(null, null, null, false, null, Now);
        Assert.Contains(result.Warnings, i => i.Code == "IntegrityStatusUnavailable");
        Assert.Contains(result.Warnings, i => i.Code == "Execution/Unable to verify");
    }

    [Fact]
    public void Startup_default_time_is_unavailable_in_display_and_export()
    {
        var result = EngineHealthSnapshot.Build(null, null, null, false, new("Off", "Startup", default), Now);
        var issue = Assert.Single(result.Issues.Where(i => i.Id == "real-execution"));
        Assert.Null(issue.ObservedAt);
        Assert.Equal("Observation time unavailable.", issue.Timing);
        Assert.DoesNotContain("0001-01-01", result.ToDiagnosticJson());
    }

    [Fact]
    public void Worker_failure_causes_are_not_silently_attributed_to_a_simultaneous_hold()
    {
        var engine = Engine with { Work = [new("Paper inputs", "Blocked", Now, null, 3) { LastFailureType = "InvalidOperationException", LastFailed = Now }] };
        var result = EngineHealthSnapshot.Build(engine, new([Paper], []), Now, false, Execution, Now);
        var worker = Assert.Single(result.Warnings.Where(i => i.Code == "WorkerFailures"));
        Assert.Equal("Cause not established", worker.Certainty);
        Assert.Contains("relationship is not recorded", worker.Impact);
        Assert.Contains("3 consecutive failures", worker.Evidence);
    }

    [Fact]
    public void Market_wait_is_neutral_and_export_preserves_the_same_issue_identity_and_approval_boundary()
    {
        var result = Build(new([Paper], []));
        Assert.Contains(result.Waiting, i => i.Code == "Execution/Waiting for market");
        using var json = JsonDocument.Parse(result.ToDiagnosticJson());
        Assert.Equal("TraderVI.EngineHealth.v1", json.RootElement.GetProperty("Contract").GetString());
        var first = json.RootElement.GetProperty("Issues").EnumerateArray().First(i => i.GetProperty("Code").GetString() == "PaperRecoveryRequired");
        Assert.Equal(Assert.Single(result.Restrictions).Id, first.GetProperty("Id").GetString());
        Assert.True(first.GetProperty("RequiresApproval").GetBoolean());
        Assert.False(first.TryGetProperty("OwnerId", out _));
        Assert.False(first.TryGetProperty("Color", out _));
    }

    [Fact]
    public void Notices_preserve_uncertain_applicability_and_later_reports_for_the_same_code()
    {
        var engine = Engine with { Notices = [new(Now.AddHours(-1), "Error", "Collection", "Earlier failure"),
            new(Now, "Information", "Collection", "Later outcome"), new(Now, "Warning", "Research", "Review required")] };
        var result = EngineHealthSnapshot.Build(engine, new([], []), Now, false, Execution, Now);
        var warning = Assert.Single(result.Warnings.Where(i => i.Code == "ReportedWarnings"));
        Assert.Contains("Review required", warning.Evidence);
        Assert.DoesNotContain("Earlier failure", warning.Evidence);
        Assert.Equal("Current applicability unknown", warning.Certainty);
        Assert.Contains(result.History, i => i.Explanation == "Earlier failure");
    }

    [Fact]
    public void Pending_hold_links_to_its_exact_run_even_when_recorded_after_the_plan_boundary()
    {
        var run = Audit(EngineRecoveryScope.Paper, "Running", -1);
        var pending = Paper with { FailureCode = "RecoveryVerificationPending", EvidenceKey = "Recovery/" + run.RunId,
            DiscoveredUtc = run.Plan.BoundaryUtc.AddSeconds(1), OriginDiscoveredUtc = null, OriginEvidenceKey = null };
        var result = Build(new([pending], [run, Audit(EngineRecoveryScope.Paper, "Complete", 0)]));
        var issue = Assert.Single(result.Restrictions);
        Assert.Equal(run.RunId, issue.Recovery!.RunId);
        Assert.Contains("expected while verification", issue.Explanation);
        Assert.StartsWith("Not ready", issue.Recovery.Clearance);
    }

    [Fact]
    public void Completed_proof_leaves_live_guards_and_explicit_clearance_unverified()
    {
        var issue = Assert.Single(Build(new([Paper], [Audit(EngineRecoveryScope.Paper, "Complete", -1)])).Restrictions);
        Assert.Contains("live clearance checks", issue.Recovery!.Clearance);
        Assert.Equal("Passed in receipt", issue.Recovery.Requirements.Single(r => r.Code == "CompletedPass").State);
        Assert.Equal("Engine recheck required", issue.Recovery.Requirements.Single(r => r.Code == "CurrentHistory").State);
        Assert.Equal("Engine recheck required", issue.Recovery.Requirements.Single(r => r.Code == "ContinuityAndGuards").State);
    }

    [Theory]
    [InlineData(EngineIntegrityScope.Broker, false)]
    [InlineData(EngineIntegrityScope.Paper, true)]
    public void All_history_receipt_cannot_qualify_a_real_only_clearance(EngineIntegrityScope scope, bool qualifies)
    {
        var incident = Paper with { Scope = scope };
        var recovery = Assert.Single(Build(new([incident], [Audit(EngineRecoveryScope.All, "Complete", -1)])).Restrictions).Recovery!;
        Assert.Equal(qualifies, recovery.RunId.HasValue);
    }

    [Fact]
    public void Complete_status_with_incomplete_cursor_cannot_claim_a_pass()
    {
        var run = Audit(EngineRecoveryScope.Paper, "Complete", -1) with { Progress = new(0, 0, null, 3, false) };
        var recovery = Assert.Single(Build(new([Paper], [run])).Restrictions).Recovery!;
        Assert.StartsWith("Not ready", recovery.Clearance);
        Assert.Equal("Pending", recovery.Requirements.Single(r => r.Code == "CompletedPass").State);
    }

    [Fact]
    public void Speed_requires_advancing_checkpoints_from_the_same_fixed_plan()
    {
        var run = Audit(EngineRecoveryScope.Paper, "Running", -1) with { UpdatedUtc = Now.UtcDateTime, Progress = new(0, 0, null, 200, false) };
        var prior = run with { UpdatedUtc = Now.AddSeconds(-20).UtcDateTime, Progress = new(0, 0, null, 100, false) };
        var status = new EngineIntegrityStatus([Paper], [run]);
        EngineHealthRecovery Project(EngineAuditRun old, bool failed = false) => Assert.Single(EngineHealthSnapshot.Build(
            Engine, status, Now, failed, Execution, Now, previousIntegrity: new([], [old])).Restrictions).Recovery!;
        Assert.Equal(5d, Project(prior).UnitsPerSecond);
        Assert.Contains("Progress observed", Project(prior).Progress);
        Assert.Null(Project(prior with { RunId = Guid.NewGuid() }).UnitsPerSecond);
        Assert.Null(Project(run).UnitsPerSecond);
        Assert.Null(Project(prior, failed: true).UnitsPerSecond);
        Assert.StartsWith("Cannot assess", Project(prior, failed: true).Clearance);
    }

    [Fact]
    public void Old_running_checkpoint_requests_investigation_without_declaring_it_stuck()
    {
        var recovery = Assert.Single(Build(new([Paper], [Audit(EngineRecoveryScope.Paper, "Running", -1)])).Restrictions).Recovery!;
        Assert.Contains("No checkpoint for over 15 minutes", recovery.Progress);
        Assert.Contains("before assuming", recovery.Progress);
    }

    [Fact]
    public void Focused_clearance_selection_excludes_unrelated_and_other_domain_incidents()
    {
        var original = Paper;
        var run = Audit(EngineRecoveryScope.Paper, "Complete", -1);
        var pending = Paper with { FailureCode = "RecoveryVerificationPending", EvidenceKey = "Recovery/" + run.RunId };
        var unrelatedPending = Paper with { FailureCode = "RecoveryVerificationPending", EvidenceKey = "Recovery/" + Guid.NewGuid() };
        var global = Paper with { Scope = EngineIntegrityScope.Global };
        var status = new EngineIntegrityStatus([original, pending, unrelatedPending, global, Paper], [run]);
        var ids = EngineHealthRecovery.SelectIncidentIds(status, run, EngineRecoveryScope.Paper, original.IncidentId);
        Assert.Equal(new[] { original.IncidentId, pending.IncidentId }, ids);
        using var json = JsonDocument.Parse(Build(status).ToDiagnosticJson());
        var exported = json.RootElement.GetProperty("Issues")[0].GetProperty("Recovery");
        Assert.True(exported.TryGetProperty("Requirements", out _));
        Assert.False(exported.TryGetProperty("Database", out _));
    }
}
