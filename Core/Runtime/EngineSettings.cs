#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Runtime;

public sealed record EngineStrategyVersion(Guid VersionId, string Family, string Name, string SettingsJson,
    string SettingsHash, DateTime CreatedUtc)
{
    public string DisplayName => Name;
    public T Read<T>()
    {
        if (SettingsHash != EngineStrategySettings.Hash(SettingsJson)) throw new InvalidOperationException("Saved strategy checksum failed.");
        return JsonSerializer.Deserialize<T>(SettingsJson, EngineStrategySettings.JsonOptions)
            ?? throw new InvalidOperationException("Saved strategy is empty.");
    }
}

public sealed record EngineSettingsTarget(Guid TargetId, string Family, string Name, string State,
    Guid? VersionId, Guid? AssignmentId, string ActiveName)
{
    public string DisplayName => $"{Name} · {State} · {ActiveName}";
}
public sealed record EngineSettingsCatalog(bool Installed, IReadOnlyList<EngineStrategyVersion> Versions,
    IReadOnlyList<EngineSettingsTarget> Targets);
public sealed record EngineSettingField(string Key, string Label, string Help, string Value);

/// <summary>Explicit supported surface: never expose a policy identity or an ignored runtime constant as editable.</summary>
public static class EngineStrategySettings
{
    public const string Live = "DelphiLive", Shadow = "SystemShadow", Tracked = "TrackedPositions";
    public static readonly Guid TrackedTargetId = new("0675a2e8-033c-4ff9-9d63-584307fc1bbb");
    public static JsonSerializerOptions JsonOptions { get; } = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(json)));
    public static object Defaults(string family) => family switch
    {
        Live => DelphiLivePolicyDefinition.Version1,
        Shadow => SystemShadowPolicyConfig.Version1,
        Tracked => DelayedIntradaySwingPolicyConfig.Version1,
        _ => throw new ArgumentException("Unknown strategy family.")
    };
    public static object Read(string family, string json) => JsonSerializer.Deserialize(json, Defaults(family).GetType(), JsonOptions)
        ?? throw new ArgumentException("Strategy settings are empty.");
    public static string Serialize(object config) => JsonSerializer.Serialize(config, config.GetType(), JsonOptions);
    public static void Validate(string family, object config)
    {
        switch (family)
        {
            case Live: ((DelphiLivePolicyDefinition)config).Validate(); break;
            case Shadow: SystemShadowPolicy.ValidateConfig((SystemShadowPolicyConfig)config); break;
            case Tracked:
                var tracked = (DelayedIntradaySwingPolicyConfig)config;
                DelayedIntradaySwingExitPolicy.ValidateConfig(tracked);
                if (tracked.PollIntervalMinutes != 15 || tracked.ExpectedSourceDelayMinutes != 15 || tracked.LateDataAgeMinutes != 45)
                    throw new ArgumentException("The delayed evidence contract remains 15-minute bars with the reviewed freshness policy.");
                if (tracked.StrongBreakoutProbability is < 0 or > 1 || tracked.MaximumStrongDownProbability is < 0 or > 1 || tracked.StrongDirectionEdge is < -1 or > 1)
                    throw new ArgumentException("Probabilities must be 0–1 and direction edge must be −1 to 1.");
                break;
            default: throw new ArgumentException("Unknown strategy family.");
        }
    }
    private static readonly string[] LiveFields = ["SelectedRawMoveThreshold", "SelectedExcessMoveThreshold", "SelectedRulerSessions",
        "DirectionalVolumeThreshold", "StructureBufferUnits", "MinimumStructureReferences", "HardLossFraction",
        "FastDownsideReturnFloor", "ProfitFloorActivationGainFraction", "TrailingActivationGainFraction", "TrailingDistanceFraction",
        "MaximumHoldings", "EntryTargetNavFraction", "MaximumSameSessionEntriesPerSymbol", "DailyLossGuardFraction", "CapitalReviewDrawdownFraction"];
    public static IReadOnlyList<EngineSettingField> Fields(string family, object config) => config.GetType().GetProperties()
        .Where(p => family == Live ? LiveFields.Contains(p.Name) : p.SetMethod is not null &&
            (family != Tracked || p.Name is not ("PollIntervalMinutes" or "ExpectedSourceDelayMinutes" or "LateDataAgeMinutes")))
        .Select(p => new EngineSettingField(p.Name, System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<=[a-z])(?=[A-Z])", " "),
            EngineSettingDescriptions.Help(p.Name), Convert.ToString(p.GetValue(config), CultureInfo.InvariantCulture)!)).ToArray();
    public static object Edit(string family, object source, IEnumerable<EngineSettingField> fields, Guid id)
    {
        object result = Read(family, Serialize(source));
        var allowed = Fields(family, source).Select(f => f.Key).ToHashSet();
        foreach (var field in fields)
        {
            if (!allowed.Remove(field.Key)) throw new ArgumentException("Unknown or duplicate strategy field.");
            var property = result.GetType().GetProperty(field.Key)!;
            try
            {
                object value;
                if (property.PropertyType == typeof(int)) value = int.Parse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (property.PropertyType == typeof(decimal)) value = decimal.Parse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                else if (property.PropertyType == typeof(double))
                {
                    double number = double.Parse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(number)) throw new FormatException();
                    value = number;
                }
                else throw new ArgumentException("Unsupported setting type.");
                property.SetValue(result, value);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or TargetInvocationException)
            { throw new ArgumentException($"{field.Label}: enter a valid number."); }
        }
        if (allowed.Count != 0) throw new ArgumentException("All settings must be supplied.");
        if (result is DelphiLivePolicyDefinition policy) result = policy with { PolicyVersionId = id };
        Validate(family, result);
        return result;
    }
}

public static class EngineStrategyReassignment
{
    public static (IntradaySwingPositionState State, IReadOnlyList<DelayedIntradayBar> Bars) PrepareTrackedReplay(
        IntradaySwingPositionState opened, decimal? boundaryHigh, IReadOnlyList<DelayedIntradayBar> bars,
        DateTime assignedUtc, DelayedIntradaySwingPolicyConfig policy)
    {
        var historical = bars.Where(b => b.EndUtc <= assignedUtc).ToArray();
        var state = RebaseTracked(opened, boundaryHigh, historical, policy);
        // The first decision after a cutover uses the latest known close, never an earlier intrabar low
        // against a newly calculated floor. Subsequent full bars retain the policy's normal OHLC semantics.
        var latest = historical.LastOrDefault();
        var replay = new List<DelayedIntradayBar>();
        if (latest is not null) replay.Add(latest with { Open = latest.Close, High = latest.Close, Low = latest.Close });
        replay.AddRange(bars.Where(b => b.EndUtc > assignedUtc).Select(b => b.StartUtc < assignedUtc
            ? b with { Open = b.Close, High = b.Close, Low = b.Close } : b));
        return (state, replay);
    }
    public static IntradaySwingPositionState RebaseTracked(IntradaySwingPositionState prior, decimal? observedHigh,
        IReadOnlyList<DelayedIntradayBar> bars, DelayedIntradaySwingPolicyConfig policy)
    {
        DelayedIntradaySwingExitPolicy.ValidateConfig(policy);
        decimal high = System.Math.Max(prior.HighestCompletedClose, observedHigh ?? prior.EntryPrice);
        if (bars.Count > 0) high = System.Math.Max(high, bars.Max(b => b.Close));
        decimal breakEven = DelayedIntradaySwingExitPolicy.BreakEvenExitPrice(prior.EntryPrice, policy);
        bool armed = high >= breakEven;
        return prior with { HighestCompletedClose = high, ProfitProtectionArmed = armed,
            TrailingStopPrice = armed ? System.Math.Max(breakEven, high * (1m - policy.TrailingLossFraction)) : null };
    }
    public static SystemShadowTrailingState Rebase(SystemShadowTrailingState prior, SystemShadowPolicyConfig policy)
    {
        SystemShadowPolicy.ValidateConfig(policy);
        decimal breakEven = prior.AverageCost / (1m - policy.ExitFrictionRate);
        bool armed = prior.HighestCompletedFifteenMinuteClose >= breakEven;
        return prior with { ProfitProtectionArmed = armed, TrailingStopPrice = armed
            ? System.Math.Max(breakEven, prior.HighestCompletedFifteenMinuteClose * (1m - policy.TrailingLossFraction)) : null };
    }
    public static DelphiLiveProfitProtectionState Rebase(DelphiLiveProfitProtectionState prior, DelphiLivePolicyDefinition policy, DateTime now)
    {
        decimal? high = prior.HighestCompletedFiveMinuteClose;
        var stage = high >= prior.AveragePurchasePrice * (1m + policy.TrailingActivationGainFraction) ? DelphiLiveProfitProtectionStage.Trailing :
            high >= prior.AveragePurchasePrice * (1m + policy.ProfitFloorActivationGainFraction) ? DelphiLiveProfitProtectionStage.BreakEven : DelphiLiveProfitProtectionStage.None;
        decimal? floor = stage == DelphiLiveProfitProtectionStage.None ? null : stage == DelphiLiveProfitProtectionStage.BreakEven
            ? prior.AveragePurchasePrice : System.Math.Max(prior.AveragePurchasePrice, high!.Value * (1m - policy.TrailingDistanceFraction));
        return prior with { Stage = stage, FloorPrice = floor, FloorPersistedUtc = floor is null ? null : now };
    }
    public static DelphiLivePortfolioSnapshot Rebase(DelphiLivePortfolioSnapshot prior, DelphiLivePolicyDefinition policy, DateTime now)
    {
        policy.Validate();
        if (now.Kind != DateTimeKind.Utc || now < prior.UpdatedUtc || policy.PolicyVersionId == prior.PolicyVersionId)
            throw new InvalidOperationException("Assignment requires a new policy and a current UTC boundary.");
        return prior with
        {
            PolicyVersionId = policy.PolicyVersionId, Revision = prior.Revision + 1, UpdatedUtc = now, LastCompletedCycleId = null,
            CandidateStates = ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty,
            Positions = prior.Positions.Select(p => p.ClosedUtc is null ? p with { Protection = Rebase(p.Protection, policy, now) } : p).ToImmutableArray(),
            Actions = prior.Actions.Select(a => a.Status is "Pending" or "ExitPendingOvernight"
                ? a with { Status = "Cancelled", CompletedUtc = now, TerminalReason = "StrategyReassigned" } : a).ToImmutableArray(),
            // Risk guards are conservative latches: changing a threshold never clears an already-required review.
            Guards = prior.Guards with
            {
                DailyBuyingPaused = prior.Guards.DailyBuyingPaused || prior.Guards.DailyReturn <= -policy.DailyLossGuardFraction,
                CapitalReviewRequired = prior.Guards.CapitalReviewRequired || prior.Guards.DrawdownFromHighestClosingNav <= -policy.CapitalReviewDrawdownFraction
            }
        };
    }
}
