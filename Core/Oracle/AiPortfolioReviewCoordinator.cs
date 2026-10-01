#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Risk;

namespace Core.Oracle;

public interface IAiReviewStore
{
    AiReviewScheduleScope Scope { get; }
    Task SaveAsync(AiLearningReview review, CancellationToken ct = default);
}

public interface IAiPortfolioReviewInputs
{
    AiReviewScheduleScope Scope { get; }
    Task<AiPortfolioPacket> PrepareAsync(AiReviewRequest request, CancellationToken ct = default);
}

public sealed record AiPaperReviewState(string? ActiveChoiceId, ImmutableArray<string> IndependentBlocks);

/// <summary>Connects saved portfolio/engine readers and the existing risk/packet checks. No new data policy.</summary>
public sealed class AiPaperPortfolioReviewInputs : IAiPortfolioReviewInputs
{
    private readonly IAiPortfolioCatalogReader catalog;
    private readonly AiShadowDecisionContextReader shadow;
    private readonly AiLiveDecisionContextReader live;
    private readonly PaperAiPortfolioRiskReader risk;
    private readonly AiPortfolioRiskJournal journal;
    private readonly Func<AiPaperReviewState> readState;
    private readonly TimeProvider clock;
    public AiReviewScheduleScope Scope { get; }

    public AiPaperPortfolioReviewInputs(IAiPortfolioCatalogReader catalog, AiShadowDecisionContextReader shadow,
        AiLiveDecisionContextReader live, PaperAiPortfolioRiskReader risk, AiPortfolioRiskJournal journal,
        Func<AiPaperReviewState> readState, TimeProvider? clock = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.shadow = shadow ?? throw new ArgumentNullException(nameof(shadow));
        this.live = live ?? throw new ArgumentNullException(nameof(live));
        this.risk = risk ?? throw new ArgumentNullException(nameof(risk));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.readState = readState ?? throw new ArgumentNullException(nameof(readState));
        this.clock = clock ?? TimeProvider.System;
        if (journal.Scope.Mode != AiRiskAccountMode.Paper || journal.Scope.AccountScopeId != journal.Scope.PortfolioId ||
            risk.PortfolioId != journal.Scope.PortfolioId)
            throw new ArgumentException("Review inputs must use the same paper account and risk journal.");
        Scope = new(journal.Scope.AccountScopeId, journal.Scope.PortfolioId, "paper");
    }

    public async Task<AiPortfolioPacket> PrepareAsync(AiReviewRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = readState() ?? throw new InvalidDataException("Current portfolio state is required.");
        if (state.IndependentBlocks.IsDefault) throw new InvalidDataException("Explicit account holds are required.");
        var choices = await catalog.ReadAsync(ct);
        var contexts = ImmutableArray.CreateBuilder<AiStrategyDecisionContext>();
        foreach (var choice in choices.Entries)
        {
            ct.ThrowIfCancellationRequested();
            choice.Validate();
            if (choice.Strategy is null) continue; // The packet retains undefined choices and their reasons.
            contexts.Add(choice.Strategy.Family switch
            {
                TradingStrategyFamily.DailyShadow => await shadow.ReadAsync(choice, request.TradingDate, ct),
                TradingStrategyFamily.DelphiLive => await live.ReadAsync(choice, request.TradingDate, ct),
                _ => throw new InvalidDataException("Unsupported portfolio engine.")
            });
        }
        // Observe an explicitly initialized history; never create/reset a round as a side effect of a review.
        var observation = await risk.ObserveAsync(request.TradingDate, false, state.IndependentBlocks, ct);
        ct.ThrowIfCancellationRequested();
        var current = readState();
        if (current is null || current.ActiveChoiceId != state.ActiveChoiceId || current.IndependentBlocks.IsDefault ||
            !current.IndependentBlocks.SequenceEqual(state.IndependentBlocks))
            throw new InvalidDataException("Active portfolio or account holds changed while gathering inputs.");
        return AiPortfolioPacketBuilder.Build(choices, journal, observation, state.ActiveChoiceId,
            contexts.ToImmutable(), clock.GetUtcNow());
    }
}

/// <summary>
/// One account's review-only coordinator. The host calls TickAsync; the existing durable schedule
/// decides when to gather inputs and call the supplied client. SQL records are evidence, not orders.
/// Callers retain ownership of journals/client. Construction performs no I/O or activation.
/// </summary>
public sealed class AiPortfolioReviewCoordinator
{
    private readonly AiScheduledPortfolioReviewer runner;
    private readonly AiLearningJournal learning;
    private readonly IAiReviewStore store;

    public AiPortfolioReviewCoordinator(AiReviewScheduleJournal schedule, IAiPortfolioReviewInputs inputs,
        ILlmClient client, AiLearningJournal learning, IAiReviewStore store, TimeSpan learningRefreshInterval,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(client);
        this.learning = learning ?? throw new ArgumentNullException(nameof(learning));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        if (schedule.Scope != inputs.Scope || schedule.Scope != store.Scope || schedule.Scope.AccountMode != "paper")
            throw new ArgumentException("Coordinator, inputs and storage must share the same paper scope.");
        if (learningRefreshInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(learningRefreshInterval));
        var supervisor = new AiLearningAwareSupervisor(new AiPortfolioReviewer(client, clock), learning,
            learningRefreshInterval, clock, store);
        runner = new(schedule, supervisor, inputs.PrepareAsync, "paper", clock);
    }

    public Task<AiScheduledReviewResult> TickAsync(CancellationToken ct = default) => runner.TickAsync(ct);

    /// <summary>
    /// Production composition over the existing SQL readers. Requires installed review storage,
    /// explicitly prepared journals/client and caller-qualified ages; creates no timer or new history.
    /// </summary>
    public static AiPortfolioReviewCoordinator FromSavedPaperInputs(AiReviewScheduleJournal schedule,
        PaperAiPortfolioRiskReader riskReader, AiPortfolioRiskJournal riskJournal, Func<AiPaperReviewState> readState,
        ReviewedTsxSessionCalendar calendar, TimeSpan catalogMaximumAge, TimeSpan contextMaximumAge, int historyDays,
        OpenAiResponsesLlmClient client, AiLearningJournal learning, TimeSpan learningRefreshInterval, TimeProvider? clock = null)
    {
        if (contextMaximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(contextMaximumAge));
        var inputs = new AiPaperPortfolioReviewInputs(new AiPortfolioCatalogRepository(catalogMaximumAge),
            new(new AiShadowDecisionRepository(historyDays), calendar, contextMaximumAge, clock),
            new(new AiLiveDecisionRepository(historyDays), calendar, contextMaximumAge, clock),
            riskReader, riskJournal, readState, clock);
        return new(schedule, inputs, client, learning, new AiPortfolioReviewRepository(inputs.Scope), learningRefreshInterval, clock);
    }

    /// <summary>Repair a missed SQL save from durable local evidence, without repeating the AI call.</summary>
    public Task PersistReviewAsync(Guid reviewId, CancellationToken ct = default) =>
        store.SaveAsync(learning.ReadReview(reviewId), ct);
}
