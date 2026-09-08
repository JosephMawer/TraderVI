#nullable enable
using Core.Runtime;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record TradingPaperEvidence(Guid PaperAccountId, string AccountName, string Family,
    string FactsJson, ImmutableArray<string> Limitations);

public sealed record TradingPromotionReview(Guid RequestId, string AccountLabel,
    CompleteTradingStrategy Strategy, TradingPaperEvidence Evidence, Guid? PriorPromotionId,
    Guid? AccountReportId, DateTime PreparedUtc, ImmutableArray<string> OperationalBlocks)
{
    public Guid? PreviousReviewPromotionId { get; init; }
    public bool CanApprove => OperationalBlocks.IsEmpty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => EngineStrategySettings.Hash(JsonSerializer.Serialize(this));
}

public sealed record TradingPromotionApproval(TradingPromotionReview Review, string Reviewer, string Reason)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string RequestHash => EngineStrategySettings.Hash(JsonSerializer.Serialize(this));
    public void Validate()
    {
        Review.Strategy.Validate();
        if (Review.RequestId == Guid.Empty || Review.Evidence.PaperAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(Review.AccountLabel) || Review.AccountLabel.Length > 64 ||
            Review.PreparedUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(Reviewer) || Reviewer.Length > 128 ||
            string.IsNullOrWhiteSpace(Reason) || Reason.Length > 1024)
            throw new ArgumentException("Approval requires a complete review, target account, reviewer and reason.");
        if (!Review.CanApprove) throw new InvalidOperationException(string.Join(" ", Review.OperationalBlocks));
    }
}

/// <summary>Consequential boundary: atomically revalidate and append approval without executing a trade.</summary>
public interface ITradingPromotionStore
{
    Task<TradingStrategyAssignment> ApproveAsync(TradingPromotionApproval approval, CancellationToken token = default);
}

public static class TradingPromotionPolicy
{
    // Performance and research limitations are deliberately advisory under the operator's ADR-0062 decision.
    // Operational correctness is independent of whether a human accepts the investment evidence.
    public static ImmutableArray<string> OperationalBlocks(bool installed, TradingSettingsAccess access,
        ReportedTradingAccount? account, string currentLedgerFingerprint)
    {
        var blocks = ImmutableArray.CreateBuilder<string>();
        if (!installed) blocks.Add("Install the reviewed promotion migration 029 first.");
        if (!access.CanEdit) blocks.Add(access.Description);
        if (account is null) blocks.Add("Report this Real account's cash, holdings and outstanding-order state in Trading first.");
        else
        {
            account.Validate();
            if (account.Currency != "CAD") blocks.Add("This strategy requires a CAD account.");
            if (account.LedgerFingerprint != currentLedgerFingerprint) blocks.Add("Account facts changed. Reconcile and review again.");
            if (account.Holdings.Length != 0 || account.PendingOrderCount is null or > 0)
                blocks.Add("The Real account must have no holdings or outstanding orders before promotion.");
        }
        return blocks.ToImmutable();
    }

    public static void Recheck(TradingPromotionApproval approval, TradingPromotionReview fresh)
    {
        approval.Validate();
        if (!fresh.CanApprove) throw new InvalidOperationException(string.Join(" ", fresh.OperationalBlocks));
        if (approval.Review.AccountLabel != fresh.AccountLabel || approval.Review.Strategy.Fingerprint != fresh.Strategy.Fingerprint ||
            approval.Review.PriorPromotionId != fresh.PriorPromotionId || approval.Review.AccountReportId != fresh.AccountReportId ||
            approval.Review.Evidence.PaperAccountId != fresh.Evidence.PaperAccountId)
            throw new InvalidOperationException("Strategy, account or prior assignment changed. Prepare a fresh review.");
    }

    public static TradingStrategyAssignment Retry(string savedHash, TradingStrategyAssignment saved,
        TradingPromotionApproval request) => savedHash == request.RequestHash
            ? saved : throw new InvalidOperationException("This approval request was already used with different content.");
}
