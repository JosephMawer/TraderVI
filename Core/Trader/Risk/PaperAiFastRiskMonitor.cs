#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Core.Trader.DelphiLive;

namespace Core.Trader.Risk;

/// <summary>Persists verified faster paper values through the existing risk journal; no orders or collection.</summary>
public sealed class PaperAiFastRiskMonitor
{
    private readonly AiPortfolioRiskJournal journal;
    private readonly AiPortfolioRiskObservationWriter writer;
    private readonly ITsxSessionCalendar calendar;
    private readonly TimeSpan accountAge, priceAge, receiptAge;
    internal Guid PortfolioId => journal.Scope.PortfolioId;

    public PaperAiFastRiskMonitor(AiPortfolioRiskJournal journal, AiPortfolioRiskPolicy policy, ITsxSessionCalendar calendar,
        TimeSpan maximumAccountAge, TimeSpan maximumPriceAge, TimeSpan maximumReceiptAge)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(policy);
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        if (journal.Scope.Mode != AiRiskAccountMode.Paper || journal.Scope.AccountScopeId != journal.Scope.PortfolioId ||
            maximumAccountAge <= TimeSpan.Zero || maximumAccountAge > TimeSpan.FromMinutes(5) ||
            maximumPriceAge <= TimeSpan.Zero || maximumPriceAge > TimeSpan.FromDays(1) ||
            maximumReceiptAge <= TimeSpan.Zero || maximumReceiptAge > TimeSpan.FromDays(1))
            throw new ArgumentException("An owned paper journal and explicit bounded freshness settings are required.");
        this.journal = journal;
        writer = new(journal, policy);
        (accountAge, priceAge, receiptAge) = (maximumAccountAge, maximumPriceAge, maximumReceiptAge);
    }

    public AiRiskObservationResult Start(AiPaperAccountValuation? value, DateTimeOffset now, bool hasFreshAiReview,
        ImmutableArray<string> independentBlocks) => Apply(value, now, hasFreshAiReview, independentBlocks, true, false);
    public AiRiskObservationResult Observe(AiPaperAccountValuation? value, DateTimeOffset now, bool hasFreshAiReview,
        ImmutableArray<string> independentBlocks) => Apply(value, now, hasFreshAiReview, independentBlocks, false, false);

    /// <summary>Explicit same-account, same-family transition from saved five-minute evidence; never starts/reset a round.</summary>
    public AiRiskObservationResult Handover(AiPaperAccountValuation? value, DateTimeOffset now, bool hasFreshAiReview,
        ImmutableArray<string> independentBlocks) => Apply(value, now, hasFreshAiReview, independentBlocks, false, true);

    private AiRiskObservationResult Apply(AiPaperAccountValuation? candidate, DateTimeOffset now, bool reviewed,
        ImmutableArray<string> blocks, bool initialize, bool handover)
    {
        if (blocks.IsDefault) throw new ArgumentException("Independent checks must be supplied.", nameof(blocks));
        if (candidate is { SavedBlocks.IsDefault: false }) blocks = blocks.AddRange(candidate.SavedBlocks);
        return writer.Apply(previous =>
        {
            try
            {
                if (candidate is null) throw new InvalidOperationException("A complete account value is required.");
                var evidence = PaperAiFastRiskEvidence.Validate(candidate, PortfolioId, calendar, accountAge, priceAge, receiptAge, now);
                var source = new PortfolioRiskValuationSource(AiPaperAccountValuation.Contract, candidate.EvidenceJson, candidate.EvidenceSha256);
                // Compare against all accepted bars, including earlier rounds, after restart and during retries.
                var history = journal.ReadHistory();
                if (history.LastOrDefault()?.State.LastValuation != previous)
                    throw new InvalidOperationException("Risk history changed during source verification.");
                foreach (var transition in history)
                {
                    var oldSource = (transition.Command.Valuation ?? transition.Command.Entry?.Valuation)?.Source;
                    if (oldSource?.Contract != AiPaperAccountValuation.Contract) continue;
                    if (oldSource.Sha256 != PaperAiFastRiskEvidence.Hash(oldSource.EvidenceJson))
                        throw new InvalidOperationException("Saved source evidence is inconsistent.");
                    var old = PaperAiFastRiskEvidence.Read<PaperAiFastValuationReader.Evidence>(oldSource.EvidenceJson);
                    if (PaperAiFastRiskEvidence.ConflictingBars(old, evidence))
                    {
                        journal.LatchSourceConflict(history.Length, source, now);
                        throw new InvalidOperationException("A previously accepted market bar has conflicting contents.");
                    }
                }
                if (previous is not null)
                {
                    PaperAiFastRiskEvidence.Continuity(previous, evidence, handover);
                    if (candidate.AtUtc == previous.At.UtcDateTime && previous.Source?.Contract == AiPaperAccountValuation.Contract)
                    {
                        var old = PaperAiFastRiskEvidence.Read<PaperAiFastValuationReader.Evidence>(previous.Source.EvidenceJson);
                        if (previous.Value != candidate.Value || previous.ValidUntil.UtcDateTime > candidate.ValidUntilUtc ||
                            now.UtcDateTime >= previous.ValidUntil.UtcDateTime ||
                            !PaperAiFastRiskEvidence.SameBook(old, evidence))
                            throw new InvalidOperationException("A repeated market time cannot replace its account value or source.");
                        return previous; // Preserve original evidence identity and expiry, even with a new receipt.
                    }
                }
                if (!candidate.IsFreshAt(now.UtcDateTime)) throw new InvalidOperationException("Prepared evidence has expired.");
                return new PortfolioRiskValuation(PortfolioId, candidate.EvidenceId, candidate.TradingDate, new(candidate.AtUtc),
                    new(candidate.ValidUntilUtc), candidate.Value, true, true) { Source = source };
            }
            catch (Exception ex) when (ex is JsonException or NullReferenceException)
            { throw new InvalidOperationException("Malformed paper source evidence."); }
        }, now, reviewed, blocks, initialize);
    }
}
