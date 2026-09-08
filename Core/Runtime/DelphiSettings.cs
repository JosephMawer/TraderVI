#nullable enable
using Core.Db;
using Core.ML.Engine.Profit;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Core.Runtime;

/// <summary>Only the eight persisted, executed daily decision gates are editable here.</summary>
public sealed record DelphiGateSettings(double MinCompositeScore, double MinUpProb,
    double MinBreakoutProb, double MinDirectionEdge, double MaxDownProb,
    double BreadthVetoThreshold, double StrongBreakoutOverride, double StrongEdgeOverride)
{
    public static DelphiGateSettings From(StrategyConfig c) => new(c.MinCompositeScore, c.MinUpProb,
        c.MinBreakoutProb, c.MinDirectionEdge, c.MaxDownProb, c.BreadthVetoThreshold,
        c.StrongBreakoutOverride, c.StrongEdgeOverride);

    public void Validate()
    {
        Range(MinCompositeScore, 0, 1, "Minimum composite");
        Range(MinUpProb, 0, 1, "Minimum up probability");
        Range(MinBreakoutProb, 0, 1, "Minimum breakout probability");
        Range(MaxDownProb, 0, 1, "Maximum down probability");
        Range(MinDirectionEdge, -1, 1, "Minimum direction edge");
        Range(BreadthVetoThreshold, -1, 1, "Breadth veto");
        Range(StrongBreakoutOverride, 0, 1, "Strong breakout override");
        Range(StrongEdgeOverride, -1, 1, "Strong edge override");
    }

    private static void Range(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{label} must be between {min} and {max}.");
    }
}

public sealed record DelphiStrategySettings(StrategyVersionInfo Strategy, StoredProfitModelSet? ModelSet,
    string? ModelSetHash, string? UnavailableReason)
{
    public string DisplayName => $"{Strategy.VersionName}{(Strategy.IsActive ? " • active" : "")}" +
        (ModelSet is null ? " • no preserved models" : $" • set {ModelSet.ModelSetId.ToString("N")[..8]}");
    public string Fingerprint => StoredProfitModelSet.HashJson(JsonSerializer.Serialize(Strategy));
    public void ValidateSelectable()
    {
        if (UnavailableReason is not null) throw new InvalidOperationException(UnavailableReason);
        if (!Strategy.HasOfficialEvidenceIdentity || ModelSet is null || ModelSetHash is null)
            throw new InvalidOperationException("This strategy has no complete preserved model assignment. Select a reviewed model set.");
        ModelSet.Validate(Strategy.VersionId, Strategy.DecisionRef);
    }
}

public sealed record DelphiSettingsCatalog(IReadOnlyList<DelphiStrategySettings> Strategies)
{
    public DelphiStrategySettings Active => Strategies.SingleOrDefault(x => x.Strategy.IsActive)
        ?? throw new InvalidOperationException("Exactly one active daily strategy is required.");
}

/// <summary>A frozen review. Repository rechecks both source and active snapshots before writing.</summary>
public sealed record DelphiSettingsChange(Guid EventId, DelphiStrategySettings ExpectedActive,
    DelphiStrategySettings Source, DelphiGateSettings Gates, Guid TargetId, string TargetName,
    string ReviewNote, string CodeIdentity)
{
    public bool CreatesVersion => TargetId != Source.Strategy.VersionId;

    public static DelphiSettingsChange Prepare(DelphiSettingsCatalog catalog, DelphiStrategySettings source,
        DelphiGateSettings gates, string newName, string reviewNote, string codeIdentity)
    {
        var active = catalog.Active;
        source.ValidateSelectable();
        if (!catalog.Strategies.Contains(source)) throw new InvalidOperationException("Reload settings before reviewing.");
        gates.Validate();
        reviewNote = reviewNote.Trim();
        if (reviewNote.Length is < 1 or > 350)
            throw new ArgumentException("Enter a review reason of 1–350 characters.");
        bool changed = gates != DelphiGateSettings.From(source.Strategy.ToConfig());
        if (!changed && source.Strategy.VersionId == active.Strategy.VersionId)
            throw new InvalidOperationException("These settings are already active.");
        newName = newName.Trim();
        if (changed && (newName.Length is < 1 or > 32 || catalog.Strategies.Any(x =>
                string.Equals(x.Strategy.VersionName, newName, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Use a unique strategy name of 1–32 characters for changed thresholds.");
        if (changed && (string.IsNullOrWhiteSpace(codeIdentity) || codeIdentity.Length is < 7 or > 128))
            throw new InvalidOperationException("The running engine must have a verifiable code identity.");
        return new(Guid.NewGuid(), active, source, gates,
            changed ? Guid.NewGuid() : source.Strategy.VersionId,
            changed ? newName : source.Strategy.VersionName, reviewNote, codeIdentity);
    }

    public void ValidateCurrent(DelphiSettingsCatalog current)
    {
        if (current.Active.Strategy.VersionId != ExpectedActive.Strategy.VersionId ||
            current.Active.Fingerprint != ExpectedActive.Fingerprint ||
            current.Active.ModelSetHash != ExpectedActive.ModelSetHash)
            throw new InvalidOperationException("Active settings changed after review. Reload and review again.");
        var source = current.Strategies.SingleOrDefault(x => x.Strategy.VersionId == Source.Strategy.VersionId);
        if (source is null || source.Fingerprint != Source.Fingerprint || source.ModelSetHash != Source.ModelSetHash)
            throw new InvalidOperationException("Selected strategy changed after review. Reload and review again.");
        source.ValidateSelectable();
        Gates.Validate();
        if (CreatesVersion && current.Strategies.Any(x => x.Strategy.VersionId == TargetId ||
            string.Equals(x.Strategy.VersionName, TargetName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The new strategy name is already in use.");
    }

    public StoredProfitModelSet CreateBinding(DateTime utcNow) => Source.ModelSet! with
    {
        StrategyVersionId = TargetId,
        ReviewReference = $"ADR-0059; {ReviewNote}", ReviewedBy = Environment.UserName, ReviewedUtc = utcNow
    };

    public void VerifyArtifacts()
    {
        Source.ValidateSelectable();
        foreach (var model in Source.ModelSet!.Models)
            UnifiedProfitSignalModel.VerifyStoredArtifact(model.Registry, Source.ModelSet.ToBinding());
    }

    public string ReviewSummary =>
        $"Active: {ExpectedActive.Strategy.VersionName}\nSelected: {TargetName}\n" +
        $"Model set: {Source.ModelSet!.ModelSetId}\n" +
        $"Policy: {Source.Strategy.DecisionRef}; inputs: {Source.ModelSet.InputContract}\n\n" +
        string.Join("\n", Source.ModelSet.Models.Select(x => $"{x.Registry.TaskType}: {x.Registry.Name} ({x.Registry.ModelId})")) +
        $"\n\n{(CreatesVersion ? "Create a new strategy with the edited thresholds." : "Select the existing strategy and its saved thresholds.")}\n" +
        $"Composite ≥ {Gates.MinCompositeScore:G}; up ≥ {Gates.MinUpProb:G}; breakout ≥ {Gates.MinBreakoutProb:G}\n" +
        $"Edge ≥ {Gates.MinDirectionEdge:G}; down veto ≥ {Gates.MaxDownProb:G}; breadth veto ≤ {Gates.BreadthVetoThreshold:G}\n" +
        $"Override: breakout ≥ {Gates.StrongBreakoutOverride:G} and edge ≥ {Gates.StrongEdgeOverride:G}\n\n" +
        $"Reason: {ReviewNote}\n\n" +
        (CreatesVersion ? "Saves this complete strategy as a new version. No assignment or recommendation changes until a separate Assign action." :
        "Assigns the saved version to daily Delphi while dependent systems remain paused and starts an official reevaluation using local SQL data. " +
        "The reevaluation publishes new recommendations and preserves the previous evidence. Independent portfolio strategies retain their own assignments.");
}
