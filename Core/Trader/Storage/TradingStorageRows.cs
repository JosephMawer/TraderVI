#nullable enable
using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Trader.Storage;

/// <summary>Only the new, disconnected compact store uses these rows. No legacy snapshot is embedded.</summary>
public abstract record TradingStorageRow
{
    public Guid BindingId { get; init; }
    public long Version { get; init; }
}
public sealed record TradingApprovedSettings : TradingStorageRow
{
    public Guid PermissionId { get; init; }
    public Guid StrategyId { get; init; }
    public Guid AssignmentId { get; init; }
    public Guid SourceId { get; init; }
    public Guid BaselineId { get; init; }
    public string ImplementationIdentity { get; init; } = "";
    public string ScopeCode { get; init; } = "";
    public DateTime ApprovedUtc { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public decimal PurchaseCeiling { get; init; }
    public bool BuyAllowed { get; init; }
    public bool SellAllowed { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
}

public sealed record TradingAccountCurrent : TradingStorageRow
{
    public Guid PermissionId { get; init; }
    public Guid StrategyId { get; init; }
    public Guid AssignmentId { get; init; }
    public Guid BaselineId { get; init; }
    public string Phase { get; init; } = "";
    public bool ExecutionEnabled { get; init; }
    public bool NewBuysStopped { get; init; }
    public DateTime TradingDate { get; init; }
    public decimal SessionReference { get; init; }
    public decimal DrawdownReference { get; init; }
    public decimal? HighestClosingValue { get; init; }
    public Guid RiskEvidenceId { get; init; }
    public DateTime RiskThroughUtc { get; init; }
    public bool DailyLossHold { get; init; }
    public bool CapitalReviewRequired { get; init; }
    public decimal CommittedPurchaseCost { get; init; }
    public decimal ReservedPurchaseCost { get; init; }
    public decimal? ObservedCash { get; init; }
    public decimal? ObservedBuyingPower { get; init; }
    public DateTime? BrokerObservedUtc { get; init; }
    public string? BrokerCursor { get; init; }
    public string? LastAppliedBrokerFactId { get; init; }
}

public sealed record TradingPositionCurrent : TradingStorageRow
{
    public Guid PositionId { get; init; }
    public string SecurityId { get; init; } = "";
    public string Symbol { get; init; } = "";
    public bool IsOpen { get; init; }
    public int Quantity { get; init; }
    public int ReservedSellQuantity { get; init; }
    public decimal CostBasis { get; init; }
    public DateTime FirstEntryUtc { get; init; }
    public DateTime LastEntryUtc { get; init; }
    public decimal? OriginalTarget { get; init; }
    public int EntryCount { get; init; }
    public int AddOnCount { get; init; }
    public string ProtectionOwner { get; init; } = "";
    public decimal? ProtectionFloor { get; init; }
    public decimal? HighestClose { get; init; }
    public string ProtectionStage { get; init; } = "";
    public DateTime? LastProtectionUtc { get; init; }
    public Guid? ExitDecisionId { get; init; }
    public string? ExitReason { get; init; }
    public DateTime? ExitTriggeredUtc { get; init; }
    public string ProtectionJson { get; init; } = "{}";
}

public sealed record TradingSymbolCurrent : TradingStorageRow
{
    public string Symbol { get; init; } = "";
    public bool IsRelevant { get; init; }
    public Guid StrategyId { get; init; }
    public DateTime TradingDate { get; init; }
    public Guid SessionId { get; init; }
    public int EntriesToday { get; init; }
    public DateTime? LastExitUtc { get; init; }
    public string? LastExitReason { get; init; }
    public DateTime? LastProcessedUtc { get; init; }
    public Guid? LastInputId { get; init; }
    public DateTime? ConfirmationStartedUtc { get; init; }
    public DateTime? SignalExpiresUtc { get; init; }
    public string ProgressJson { get; init; } = "{}";
}

public sealed record TradingOrder : TradingStorageRow
{
    public Guid RequestId { get; init; }
    public Guid DecisionId { get; init; }
    public Guid PermissionId { get; init; }
    public Guid? PositionId { get; init; }
    public string? BrokerOrderId { get; init; }
    public string SecurityId { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public int Quantity { get; init; }
    public decimal ReferencePrice { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string Status { get; init; } = "Prepared";
    public int LatestFilledQuantity { get; init; }
    public int AppliedFilledQuantity { get; init; }
    public decimal LatestNetValue { get; init; }
    public decimal AppliedNetValue { get; init; }
    public int ReservedQuantity { get; init; }
    public decimal ReservedCash { get; init; }
    public bool NeedsWork { get; init; } = true;
    public DateTime? LastCheckedUtc { get; init; }
}

public sealed record TradingRestrictionCurrent : TradingStorageRow
{
    public Guid RestrictionId { get; init; }
    public bool IsActive { get; init; } = true;
    public string ScopeKey { get; init; } = "";
    public string ActionCode { get; init; } = "";
    public string ReasonCode { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string? EvidenceReference { get; init; }
    public DateTime? LastCheckedUtc { get; init; }
    public DateTime? NextRetryUtc { get; init; }
}

public sealed record TradingCheckpoint : TradingStorageRow
{
    public int Slot { get; init; }
    public Guid OperationId { get; init; }
    public string OperationHash { get; init; } = "";
    public string OperationKind { get; init; } = "";
    public DateTime SavedUtc { get; init; }
}

public sealed record TradingCurrentState(TradingAccountCurrent Account, TradingApprovedSettings Settings,
    ImmutableArray<TradingPositionCurrent> Positions, ImmutableArray<TradingSymbolCurrent> Symbols,
    ImmutableArray<TradingOrder> Orders, ImmutableArray<TradingRestrictionCurrent> Restrictions,
    ImmutableArray<TradingCheckpoint> Checkpoints);

/// <summary>Only changed rows. Financial policy and broker I/O do not belong in this storage contract.</summary>
public sealed record TradingStateWrite(Guid OperationId, string Kind, DateTime SavedUtc,
    long ExpectedVersion, TradingAccountCurrent Account)
{
    public ImmutableArray<TradingApprovedSettings> Settings { get; init; } = [];
    public ImmutableArray<TradingPositionCurrent> Positions { get; init; } = [];
    public ImmutableArray<TradingSymbolCurrent> Symbols { get; init; } = [];
    public ImmutableArray<TradingOrder> Orders { get; init; } = [];
    public ImmutableArray<TradingRestrictionCurrent> Restrictions { get; init; } = [];
}
public sealed record TradingSaveResult(long Version, bool AlreadyCommitted, int WrittenRows);
