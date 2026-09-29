#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle.Llm;

namespace Core.Oracle;

public sealed record AiScheduledReviewResult(AiReviewScheduleStatus Schedule, AiPortfolioProposal? Proposal = null);

/// <summary>
/// Explicit tick adapter for the existing learning-aware reviewer. One instance/scheduler belongs to
/// one account/session. No timer, protection callback, automatic retry or execution interface.
/// Production storage, account binding and host composition remain required.
/// </summary>
public sealed class AiScheduledPortfolioReviewer
{
    private readonly AiReviewScheduler scheduler;
    private readonly AiLearningAwareSupervisor supervisor;
    private readonly Func<AiReviewRequest, CancellationToken, Task<AiPortfolioPacket>> prepare;
    private readonly string accountMode;
    private readonly TimeProvider clock;

    public AiScheduledPortfolioReviewer(AiReviewScheduler scheduler, AiLearningAwareSupervisor supervisor,
        Func<AiReviewRequest, CancellationToken, Task<AiPortfolioPacket>> prepare,
        string accountMode, TimeProvider? clock = null)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        this.prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        if (accountMode is not ("paper" or "live")) throw new ArgumentException("An exact account mode is required.");
        this.accountMode = accountMode;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task<AiScheduledReviewResult> TickAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var request = scheduler.TryBegin(clock.GetUtcNow());
        if (request is null) return new(scheduler.Read(clock.GetUtcNow()));
        AiPortfolioProposal proposal;
        AiPortfolioPacket packet;
        DateTimeOffset validUntil;
        try
        {
            packet = await prepare(request, ct);
            ct.ThrowIfCancellationRequested();
            // Use the admitted identity for spending/learning records; never retry a previous call ID.
            packet = JsonSerializer.Deserialize<AiPortfolioPacket>(JsonSerializer.Serialize(packet))!
                with { PacketId = request.Id };
            if (packet.ExpiresUtc > scheduler.SessionCloseUtc)
                packet = packet with { ExpiresUtc = scheduler.SessionCloseUtc };
            AiPortfolioReviewer.ValidatePacket(packet, clock.GetUtcNow());
            // Retain the scheduling cause with the same private packet/learning/call evidence.
            // Validation below rejects packets older than the triggering evidence.
            var triggerEvidence = request.Events.Select(e => new AiMarketEvidence("schedule-event/" + e.Id,
                e.ObservedUtc, $"Review requested: {e.Kind}; source evidence: {e.EvidenceId}."));
            packet = packet with { Evidence = packet.Evidence.Concat(triggerEvidence).Append(new(
                "schedule-request/" + request.Id, request.StartedUtc,
                $"Review reasons: {string.Join(", ", request.Reasons)}. Requested interval ticks: {request.Interval.Requested.Ticks}; " +
                $"applied interval ticks: {request.Interval.Applied.Ticks}. Reason: {request.Interval.Reason}")).ToArray() };
            AiPortfolioReviewer.ValidatePacket(packet, clock.GetUtcNow());
            var beforeCall = scheduler.Read(clock.GetUtcNow());
            if (beforeCall.ReviewBlock is "OutsideSession" or "EventHistoryRequiresReview" || beforeCall.PendingEvents > 0)
                throw new InvalidDataException("The review session or event history is unavailable.");
            var reviewed = await supervisor.ReviewWithEvidenceAsync(packet, accountMode, ct);
            ct.ThrowIfCancellationRequested();
            AiPortfolioReviewer.ValidatePacket(reviewed.Packet, clock.GetUtcNow());
            proposal = reviewed.Proposal!;
            validUntil = reviewed.Packet.Learning!.Lessons.Select(l => l.ExpiresUtc)
                .Append(reviewed.Packet.Learning.ExpiresUtc).Append(reviewed.Packet.ExpiresUtc).Min();
        }
        catch (OperationCanceledException)
        {
            scheduler.Complete(request.Id, clock.GetUtcNow(), AiReviewOutcome.Cancelled);
            throw;
        }
        catch (LlmBudgetExceededException)
        {
            scheduler.Complete(request.Id, clock.GetUtcNow(), AiReviewOutcome.BudgetUnavailable);
            return new(scheduler.Read(clock.GetUtcNow()));
        }
        catch (InvalidDataException)
        {
            scheduler.Complete(request.Id, clock.GetUtcNow(), AiReviewOutcome.DataUnavailable);
            return new(scheduler.Read(clock.GetUtcNow()));
        }
        catch
        {
            scheduler.Complete(request.Id, clock.GetUtcNow(), AiReviewOutcome.Failed);
            // Preserve unexpected failures for host diagnostics; never log private exception text here.
            throw;
        }
        scheduler.Complete(request.Id, clock.GetUtcNow(), AiReviewOutcome.Reviewed, validUntil);
        var status = scheduler.Read(clock.GetUtcNow());
        return new(status, status.ReviewBlock is null ? proposal : null);
    }
}
