#nullable enable
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class PortfoliosViewModel
{
    public const int StrategyWorkspaceView = 0, AccountDirectoryView = 1, PerformanceView = 2,
        StrategyReviewView = 3, AccountActivityView = 4;
    private WealthsimpleAccountViewModel? workspaceBroker;
    private BrokerExecutionHostStatus? workspaceHost;
    private string? workspaceSymbol;
    public ObservableCollection<string> WorkspaceSymbols { get; } = [];
    public IReadOnlyList<PortfolioComparisonRow> WorkspaceStrategies => ComparisonRows.Where(r => r.IsPaperStrategy).ToArray();
    public PortfolioComparisonRow? WorkspaceSelection
    {
        get => SelectedComparison?.IsPaperStrategy == true ? SelectedComparison : null;
        set { if (value?.IsPaperStrategy == true) SelectedComparison = value; }
    }
    public string? WorkspaceSymbol
    {
        get => workspaceSymbol;
        set { if (Set(ref workspaceSymbol, value)) OnPropertyChanged(nameof(WorkspaceDecisionStages)); }
    }
    private WealthsimpleTfsaSnapshot? BrokerSnapshot => workspaceBroker?.Snapshot;
    private BrokerManagedSession? BrokerSession => PortfolioStrategyWorkspace.MatchSession(BrokerSnapshot, workspaceBroker?.ManagedStrategy?.VerifiedSession);
    private Core.Trader.DelphiLive.DelphiLivePortfolioSnapshot? WorkspacePaper => SelectedPortfolio?.DelphiLivePortfolioId is Guid id && liveAccounts.TryGetValue(id, out var account)
        ? account.Snapshot : null;
    public bool HasWorkspaceSelection => WorkspaceSelection is not null;
    public bool CanReviewWorkspace => workspaceBroker?.CanRefresh == true && WorkspacePaper is { } paper &&
        TfsaLiveStrategySelection.Rank([paper]).Single().SupportsTransitionReview;
    public string WorkspaceReviewHelp => CanReviewWorkspace ? "Opens the existing TFSA transition review. Approval and execution remain separate."
        : "Load the linked account in Trading to review an eligible intraday source. Other sources retain their existing Strategy review route and eligibility limits.";
    public string WorkspaceStrategyName => WorkspaceSelection is null ? "Select a Paper strategy"
        : IsLiveSelected && string.IsNullOrWhiteSpace(WorkspaceSelection.Source.GateStrategyName) ? "Delphi Live"
        : WorkspaceSelection.StrategyName;
    public string WorkspaceStrategyFamily => WorkspaceSelection is null ? "No Paper account selected"
        : IsLiveSelected ? "Intraday strategy · Live describes incoming session data" : "Daily Shadow strategy";
    public string WorkspacePaperPolicy => WorkspacePaper is { } paper ? $"Paper policy · {paper.PolicyVersionId:D}"
        : "Paper policy identity unavailable in this summary; inspect current assignments.";
    public string WorkspaceRealPolicy => BrokerSession is { } session
        ? $"Real complete version · {session.Strategy.Strategy.VersionId:D}\nReal policy · {session.Strategy.Strategy.PolicyVersionId:D}"
        : "Real approved version unavailable";
    public string WorkspaceRelationship => PortfolioStrategyWorkspace.Relationship(
        WorkspaceSelection?.Source.DelphiLivePortfolioId ?? WorkspaceSelection?.Source.SystemPortfolioId,
        WorkspacePaper?.PolicyVersionId, WorkspaceSelection?.Source.GateProfile, BrokerSession);
    public string WorkspacePaperName => WorkspaceSelection?.DisplayName ?? "No Paper account selected";
    public string WorkspacePaperValue => WorkspaceSelection?.ValueText ?? "Unavailable";
    public string WorkspacePaperCash => WorkspaceSelection?.CashText ?? "Unavailable";
    public string WorkspacePaperHoldings => WorkspaceSelection?.OpenText ?? "Unavailable";
    public string WorkspacePaperStatus => WorkspaceSelection is { } row ? $"{row.Source.Status} · saved {row.Source.FreshnessText}"
        : "Choose an account above. Missing facts remain unavailable.";
    public string WorkspaceRealName => BrokerSnapshot?.Account.DisplayName ?? "Linked Real TFSA";
    public string WorkspaceRealStrategy => BrokerSession?.Strategy.Strategy.Name ?? "Approved strategy unavailable";
    public string WorkspaceRealValue => BrokerSnapshot?.Value?.ToString() ?? "Unavailable";
    public string WorkspaceRealCash => BrokerSnapshot?.Cash?.ToString() ?? "Unavailable";
    public string WorkspaceRealHoldings => BrokerSnapshot?.Holdings.Count.ToString() ?? "Unavailable";
    public string WorkspaceRealStatus => workspaceBroker?.Status ?? "Open Trading to load the linked broker account. This view does not request broker data.";
    public string WorkspaceApproval => PortfolioStrategyWorkspace.Approval(BrokerSession, DateTimeOffset.UtcNow);
    public string WorkspaceExecution => workspaceHost is null ? "Host status unavailable"
        : $"{workspaceHost.State} · {PortfolioStrategyWorkspace.At(workspaceHost.CheckedAt.UtcDateTime)} · {workspaceHost.Message}";
    public string WorkspaceComparisonContext => WorkspaceSymbols.Count == 0 ? "No saved symbol evidence loaded. Refresh saved data or open Trading."
        : "Each column uses its own saved evidence and time. Different times or rule versions are not a matched experiment; missing facts are unavailable.";
    public IReadOnlyList<PortfolioDecisionStage> WorkspaceDecisionStages
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WorkspaceSymbol)) return PortfolioStrategyWorkspace.Compare(null, null);
            PortfolioSymbolEvidence? paper = null;
            if (WorkspacePaper is { } live) paper = PortfolioStrategyWorkspace.Paper(live, WorkspaceSymbol);
            else if (WorkspaceSelection is not null && displayedDetailsCode == SelectedPortfolio?.StableCode)
            {
                var candidate = Candidates.FirstOrDefault(c => c.Symbol == WorkspaceSymbol);
                var holding = Holdings.FirstOrDefault(h => h.Symbol == WorkspaceSymbol && h.Status is "Held" or "Open");
                paper = new(WorkspaceSymbol, candidate is null ? "No saved Paper candidate state" : $"{candidate.State} · {candidate.ReasonText}",
                    "Inspect Paper account activity for recorded execution checks",
                    holding is null ? "No open holding in loaded Paper activity" : $"Paper holding · {holding.Shares} simulated shares · average cost {holding.AverageCost:N4}",
                    holding is null ? "Evaluate entries under Paper controls" : "Evaluate protection and exits under Paper controls",
                    PortfolioStrategyWorkspace.At(candidate?.LastEvaluatedUtc));
            }
            return PortfolioStrategyWorkspace.Compare(paper, PortfolioStrategyWorkspace.Real(BrokerSnapshot, BrokerSession, WorkspaceSymbol));
        }
    }

    public void AttachWorkspaceBroker(WealthsimpleAccountViewModel? broker)
    {
        if (workspaceBroker is not null) workspaceBroker.PropertyChanged -= WorkspaceBrokerChanged;
        workspaceBroker = broker;
        if (broker is not null) broker.PropertyChanged += WorkspaceBrokerChanged;
        NotifyWorkspace();
    }
    public void SetWorkspaceExecutionStatus(BrokerExecutionHostStatus status) { workspaceHost = status; NotifyWorkspace(); }
    private void WorkspaceBrokerChanged(object? sender, PropertyChangedEventArgs args) => NotifyWorkspace();
    private void NotifyWorkspace()
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (WorkspacePaper is { } paper)
        {
            symbols.UnionWith(paper.CandidateStates.Keys);
            symbols.UnionWith(paper.OpenPositions.Select(p => p.Symbol));
            symbols.UnionWith(paper.Fills.Select(f => f.Symbol));
        }
        else if (WorkspaceSelection is not null && displayedDetailsCode == SelectedPortfolio?.StableCode)
        {
            symbols.UnionWith(Candidates.Select(c => c.Symbol)); symbols.UnionWith(Holdings.Select(h => h.Symbol));
        }
        if (BrokerSnapshot is { } snapshot) symbols.UnionWith(snapshot.Holdings.Select(h => h.Symbol));
        if (BrokerSession is { } session)
        {
            symbols.UnionWith(session.Memory.Symbols.Keys);
            symbols.UnionWith(session.Orders.Select(o => o.Symbol));
            if (session.LatestDecision is { } trace)
            {
                symbols.UnionWith(trace.Entries.Select(e => e.Symbol));
                if (!string.IsNullOrWhiteSpace(trace.SelectedSymbol)) symbols.Add(trace.SelectedSymbol);
            }
        }
        Replace(WorkspaceSymbols, symbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        WorkspaceSymbol = WorkspaceSymbols.FirstOrDefault(s => s == workspaceSymbol) ?? WorkspaceSymbols.FirstOrDefault();
        foreach (string name in new[] { nameof(WorkspaceStrategies), nameof(WorkspaceSelection), nameof(HasWorkspaceSelection), nameof(CanReviewWorkspace), nameof(WorkspaceReviewHelp),
            nameof(WorkspaceStrategyName), nameof(WorkspaceStrategyFamily), nameof(WorkspacePaperPolicy), nameof(WorkspaceRealPolicy),
            nameof(WorkspaceRelationship), nameof(WorkspacePaperName), nameof(WorkspacePaperValue), nameof(WorkspacePaperCash),
            nameof(WorkspacePaperHoldings), nameof(WorkspacePaperStatus), nameof(WorkspaceRealName), nameof(WorkspaceRealStrategy),
            nameof(WorkspaceRealValue), nameof(WorkspaceRealCash), nameof(WorkspaceRealHoldings), nameof(WorkspaceRealStatus),
            nameof(WorkspaceApproval), nameof(WorkspaceExecution), nameof(WorkspaceComparisonContext), nameof(WorkspaceDecisionStages) })
            OnPropertyChanged(name);
    }
}
