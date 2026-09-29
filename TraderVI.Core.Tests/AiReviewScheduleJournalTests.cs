using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Oracle.Llm;
using Core.Trader.DelphiLive;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiReviewScheduleJournalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-schedule-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly AiReviewScheduleScope scope = new(Guid.NewGuid(), Guid.NewGuid(), "paper");
    private static readonly DateTimeOffset Open = AiReviewSchedulerTests.Open;
    private static readonly DateTimeOffset Close = AiReviewSchedulerTests.Close;
    private static readonly DateOnly Date = AiReviewSchedulerTests.Date;
    private static readonly AiReviewTiming Timing = new(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(195), TimeSpan.FromMinutes(5));
    private string PathName => Path.Combine(directory, "ai-review-schedule.json");
    private AiReviewScheduleJournal New(DateTimeOffset now, bool initialize = false, AiReviewScheduleScope changedScope = null,
        ITsxSessionCalendar calendar = null, AiReviewTiming timing = null) =>
        new(directory, changedScope ?? scope, calendar ?? AiReviewSchedulerTests.Calendar(), Date, timing ?? Timing, now, initialize);

    [Fact]
    public void PendingEventsCadenceAndBudgetBlockSurviveRepeatedReopen()
    {
        var item = AiReviewSchedulerTests.Event("entry", Open);
        Guid failed;
        using (var journal = New(Open, true))
        {
            journal.RequestInterval(Open, TimeSpan.FromMinutes(3), "Synthetic faster attention");
            journal.RequestEvent(item, Open);
            failed = journal.TryBegin(Open)!.Id;
            journal.Complete(failed, Open, AiReviewOutcome.BudgetUnavailable);
        }
        using (var journal = New(Open.AddSeconds(20)))
        {
            var recovered = journal.Read(Open.AddSeconds(20));
            Assert.Equal("BudgetUnavailable", recovered.ReviewBlock);
            Assert.Equal(failed, recovered.LastAttempt!.Request.Id);
            Assert.Equal(TimeSpan.FromMinutes(3), recovered.Interval.Applied);
            Assert.Equal("Synthetic faster attention", recovered.Interval.Reason);
            Assert.Equal(1, recovered.PendingEvents);
            Assert.False(journal.RequestEvent(item, Open.AddSeconds(20)));
            Assert.Null(journal.TryBegin(Open.AddSeconds(59)));
        }
        using (var journal = New(Open.AddMinutes(1)))
        {
            var next = journal.TryBegin(Open.AddMinutes(1))!;
            Assert.NotEqual(failed, next.Id);
            Assert.Equal(item, Assert.Single(next.Events));
            Assert.Equal("BudgetUnavailable", journal.Read(Open.AddMinutes(1)).ReviewBlock);
            journal.Complete(next.Id, Open.AddMinutes(1), AiReviewOutcome.Reviewed, Close);
            Assert.Null(journal.Read(Open.AddMinutes(1)).ReviewBlock);
        }
    }

    [Fact]
    public void InterruptedReviewIsRecordedOnceAndCannotBeResubmittedOrCompletedUnderOldIdentity()
    {
        Guid original;
        using (var journal = New(Open, true))
        {
            journal.RequestEvent(AiReviewSchedulerTests.Event("original", Open), Open);
            original = journal.TryBegin(Open)!.Id;
            journal.RequestEvent(AiReviewSchedulerTests.Event("during-call", Open.AddSeconds(10)), Open.AddSeconds(10));
        }
        using (var journal = New(Open.AddSeconds(20)))
        {
            var status = journal.Read(Open.AddSeconds(20));
            Assert.False(status.InFlight);
            Assert.Equal("Interrupted", status.ReviewBlock);
            Assert.Equal(original, status.LastAttempt!.Request.Id);
            Assert.Equal(AiReviewOutcome.Interrupted, status.LastAttempt.Outcome);
            Assert.Equal(2, status.PendingEvents);
            Assert.Throws<InvalidOperationException>(() => journal.Complete(original, Open.AddSeconds(20), AiReviewOutcome.Reviewed, Close));
        }
        using (var journal = New(Open.AddSeconds(30)))
        {
            Assert.Equal(Open.AddSeconds(20), journal.Read(Open.AddSeconds(30)).LastAttempt!.CompletedUtc);
            Assert.Null(journal.TryBegin(Open.AddSeconds(79)));
            var next = journal.TryBegin(Open.AddSeconds(80))!;
            Assert.NotEqual(original, next.Id);
            Assert.Equal(2, next.Events.Length);
        }
    }

    [Fact]
    public void ReopenDoesNotRenewSuccessOrLoseCadenceAndCompletedCheckpoints()
    {
        using (var journal = New(Open, true))
        {
            var first = journal.TryBegin(Open)!;
            journal.Complete(first.Id, Open, AiReviewOutcome.Reviewed, Close);
        }
        using var reopened = New(Open.AddSeconds(5));
        Assert.Equal("FreshReviewRequired", reopened.Read(Open.AddSeconds(5)).ReviewBlock);
        Assert.Equal(Open.AddMinutes(15), reopened.Read(Open.AddSeconds(5)).NextReviewUtc);
        Assert.Null(reopened.TryBegin(Open.AddMinutes(14)));
        var next = reopened.TryBegin(Open.AddMinutes(15))!;
        Assert.DoesNotContain("SessionStart", next.Reasons);
    }

    [Fact]
    public void SessionChangeCarriesUnfinishedEventsAndFailuresWithoutInventingMissedReviews()
    {
        using (var journal = New(Open, true))
        {
            journal.RequestInterval(Open, TimeSpan.FromMinutes(4), "Carry cadence");
            journal.RequestEvent(AiReviewSchedulerTests.Event("prior-session", Open), Open);
            journal.Complete(journal.TryBegin(Open)!.Id, Open, AiReviewOutcome.Failed);
        }
        var tomorrow = Open.AddDays(1);
        using (var journal = New(tomorrow.AddHours(-1)))
        {
            journal.AdvanceSession(Date.AddDays(1), tomorrow.AddHours(-1));
            Assert.Equal("OutsideSession", journal.Read(tomorrow.AddHours(-1)).ReviewBlock);
            Assert.Equal(tomorrow, journal.Read(tomorrow.AddHours(-1)).NextReviewUtc);
        }
        using (var journal = New(tomorrow))
        {
            var state = journal.Read(tomorrow);
            Assert.Equal("Failed", state.ReviewBlock);
            Assert.Equal(TimeSpan.FromMinutes(4), state.Interval.Applied);
            var next = journal.TryBegin(tomorrow)!;
            Assert.Equal(Date.AddDays(1), next.TradingDate);
            Assert.Contains("SessionStart", next.Reasons);
            Assert.Equal(Open, Assert.Single(next.Events).ObservedUtc);
            journal.Complete(next.Id, tomorrow, AiReviewOutcome.Reviewed, tomorrow.AddMinutes(10));
            Assert.Null(journal.Read(tomorrow).ReviewBlock);
        }
    }

    [Fact]
    public void AdvanceUsesReviewedCalendarAndRejectsInFlightFutureAndBackwardsSessions()
    {
        using var journal = New(Open, true);
        var attempt = journal.TryBegin(Open)!;
        Assert.Throws<InvalidOperationException>(() => journal.AdvanceSession(Date.AddDays(1), Close));
        journal.Complete(attempt.Id, Close, AiReviewOutcome.Failed);
        Assert.Throws<InvalidOperationException>(() => journal.AdvanceSession(Date, Close));
        Assert.Throws<InvalidOperationException>(() => journal.AdvanceSession(Date.AddDays(1), Close));
        Assert.Throws<InvalidOperationException>(() => journal.AdvanceSession(Date.AddDays(2), Close));
        journal.AdvanceSession(Date.AddDays(1), Open.AddDays(1));
    }

    [Fact]
    public void EventConflictBlockSurvivesRecoveryAndSessionChange()
    {
        var item = AiReviewSchedulerTests.Event("entry", Open);
        using (var journal = New(Open, true))
        {
            journal.RequestEvent(item, Open);
            Assert.Throws<InvalidOperationException>(() => journal.RequestEvent(item with { EvidenceId = "changed" }, Open));
        }
        using var reopened = New(Open.AddDays(1));
        reopened.AdvanceSession(Date.AddDays(1), Open.AddDays(1));
        Assert.Equal("EventHistoryRequiresReview", reopened.Read(Open.AddDays(1)).ReviewBlock);
        Assert.Null(reopened.TryBegin(Open.AddDays(1)));
    }

    [Fact]
    public async Task ExclusiveOwnershipAndConcurrentTicksCannotAdmitTwoReviews()
    {
        using var journal = New(Open, true);
        Assert.ThrowsAny<IOException>(() => New(Open));
        Assert.ThrowsAny<IOException>(() => New(Open, true));
        var requests = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => journal.TryBegin(Open))));
        Assert.Single(requests.Where(r => r != null));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("portfolio")]
    [InlineData("mode")]
    [InlineData("timing")]
    [InlineData("clock")]
    public void RecoveryCannotReplaceScopeConfigurationOrMoveTimeBackwards(string change)
    {
        using (var journal = New(Open, true)) journal.TryBegin(Open.AddMinutes(1));
        Assert.ThrowsAny<Exception>(() => New(change == "clock" ? Open : Open.AddMinutes(2), changedScope: change switch
        {
            "account" => scope with { AccountScopeId = Guid.NewGuid() },
            "portfolio" => scope with { PortfolioId = Guid.NewGuid() },
            "mode" => scope with { AccountMode = "live" },
            _ => scope
        }, timing: change == "timing" ? Timing with { InitialInterval = TimeSpan.FromMinutes(1) } : Timing));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("pending")]
    [InlineData("corrupt")]
    public void BrokenStorageNeverInitializesFreshHistory(string failure)
    {
        using (var journal = New(Open, true)) journal.TryBegin(Open);
        if (failure == "missing") File.Delete(PathName);
        if (failure == "pending") File.WriteAllText(PathName + ".pending", "interrupted save");
        if (failure == "corrupt") File.WriteAllText(PathName, "broken JSON");
        Assert.Throws<InvalidDataException>(() => New(Open.AddMinutes(1)));
        Assert.ThrowsAny<IOException>(() => New(Open.AddMinutes(1), true));
    }

    [Fact]
    public void FailedWriteCannotReturnAdmissionOrLetTheSameOwnerContinue()
    {
        using var journal = New(Open, true);
        // Any interrupted-save artifact must block admission and poison this owner, even if removed.
        Directory.CreateDirectory(PathName + ".pending");
        Assert.ThrowsAny<Exception>(() => journal.TryBegin(Open));
        Directory.Delete(PathName + ".pending");
        Assert.Throws<InvalidDataException>(() => journal.TryBegin(Open.AddMinutes(1)));
        Assert.Throws<InvalidDataException>(() => journal.Read(Open.AddMinutes(1)));
    }

    [Fact]
    public void ChangedFileIsRejectedEvenWithARecomputedChecksum()
    {
        using var journal = New(Open, true);
        Rewrite(payload => payload["Version"] = 2);
        Assert.Throws<InvalidDataException>(() => journal.TryBegin(Open));
    }

    [Fact]
    public void RecoveryReplaysTransitionsRatherThanTrustingAChecksum()
    {
        using (var journal = New(Open, true))
        {
            var first = journal.TryBegin(Open)!;
            journal.Complete(first.Id, Open, AiReviewOutcome.Reviewed, Close);
            journal.TryBegin(Open.AddMinutes(15));
        }
        Rewrite(payload =>
        {
            var commands = payload["Commands"]!.AsArray();
            commands[2]!["RequestId"] = commands[0]!["RequestId"]!.GetValue<string>();
        });
        Assert.Throws<InvalidDataException>(() => New(Open.AddMinutes(16)));
    }

    [Fact]
    public void InterruptedReviewRecoveryDoesNotReleaseOrReplaceItsSpendingReservation()
    {
        Guid original;
        decimal reserved;
        using (var journal = New(Open, true))
        using (var spend = new AiCallJournal(directory, initializeNew: true))
        {
            original = journal.TryBegin(Open)!.Id;
            spend.Reserve(original, new(1.5m, 1.2m, Open.AddDays(2)), Open, "synthetic pending model request", 100, 100);
            reserved = Assert.Single(spend.ReadAll()).ReservedCad;
        }
        using var reopened = New(Open.AddMinutes(2));
        using var retainedSpend = new AiCallJournal(directory);
        var call = Assert.Single(retainedSpend.ReadAll());
        Assert.Equal(original, call.CallId);
        Assert.Null(call.ChargedCad);
        Assert.Equal(reserved, call.ReservedCad);
        Assert.Equal(original, reopened.Read(Open.AddMinutes(2)).LastAttempt!.Request.Id);
        Assert.NotEqual(original, reopened.TryBegin(Open.AddMinutes(3))!.Id);
        Assert.Single(retainedSpend.ReadAll());
    }

    [Fact]
    public void MissedSessionsAndWeekendLeadToOneCurrentReviewWithOriginalPendingEvidence()
    {
        var friday = new DateOnly(2026, 9, 25);
        var calendar = new ReviewedTsxSessionCalendar(new("synthetic", "test fixture", friday, Date.AddDays(1), [friday, Date, Date.AddDays(1)]));
        var fridayOpen = new DateTimeOffset(calendar.GetSessionBounds(friday).OpenUtc);
        using (var journal = new AiReviewScheduleJournal(directory, scope, calendar, friday, Timing, fridayOpen, true))
        {
            journal.RequestEvent(AiReviewSchedulerTests.Event("friday", fridayOpen), fridayOpen);
            journal.TryBegin(fridayOpen);
        }
        using var recovered = new AiReviewScheduleJournal(directory, scope, calendar, friday, Timing, Open.AddDays(1));
        recovered.AdvanceSession(Date.AddDays(1), Open.AddDays(1));
        var attempt = recovered.TryBegin(Open.AddDays(1).AddMinutes(1))!;
        Assert.Equal(Date.AddDays(1), attempt.TradingDate);
        Assert.Equal(new[] { "SessionStart", "EntrySignal" }, attempt.Reasons);
        Assert.Equal(fridayOpen, Assert.Single(attempt.Events).ObservedUtc);
        Assert.Null(recovered.TryBegin(Open.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public void ChangedCalendarBoundsCannotRewriteHistoricalScheduling()
    {
        using (var journal = New(Open, true)) journal.TryBegin(Open);
        Assert.Throws<InvalidDataException>(() => New(Open.AddMinutes(1), calendar: new ChangedCalendar()));
    }

    private sealed class ChangedCalendar : ITsxSessionCalendar
    {
        private readonly ITsxSessionCalendar original = AiReviewSchedulerTests.Calendar();
        public bool IsRegularSession(DateOnly date) => original.IsRegularSession(date);
        public DateOnly GetImmediatelyPrecedingSession(DateOnly date) => original.GetImmediatelyPrecedingSession(date);
        public DateOnly GetNextSession(DateOnly date) => original.GetNextSession(date);
        public int GetSessionOrdinal(DateOnly date) => original.GetSessionOrdinal(date);
        public DelphiLiveSessionBounds GetSessionBounds(DateOnly date)
        {
            var bounds = original.GetSessionBounds(date);
            return new(date, bounds.OpenUtc.AddMinutes(1), bounds.CloseUtc);
        }
    }

    private void Rewrite(Action<JsonObject> edit)
    {
        var envelope = JsonNode.Parse(File.ReadAllText(PathName))!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        edit(payload);
        string changed = payload.ToJsonString();
        envelope["Payload"] = changed;
        envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(changed)));
        File.WriteAllText(PathName, envelope.ToJsonString());
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
