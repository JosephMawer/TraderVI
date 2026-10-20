#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Builds only the expressly approved scoped cash source from saved native facts.
/// Does not create initial references, qualify other launch checks, save authority or place orders.</summary>
public static class BrokerSupervisedTrialPreparation
{
    public static ImmutableArray<ManualSellRecord> OpeningSaleCandidates(BrokerAccountReconciliation anchor,
        IReadOnlyList<ManualSellRecord> journal, DateOnly session) => journal.Where(s =>
            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(s.Intent.CreatedAt.UtcDateTime)) == session &&
            BrokerNoTransferSessionScope.IsVerifiedOpeningSale(s, anchor)).OrderBy(s => s.Intent.CreatedAt)
            .ThenBy(s => s.Intent.RequestId).ToImmutableArray();

    /// <summary>The operator identifies the original two sales; never infer them from a count or the latest rows.</summary>
    public static ImmutableArray<ManualSellRecord> SelectOpeningSales(BrokerAccountReconciliation anchor,
        IReadOnlyList<ManualSellRecord> journal, DateOnly session, ImmutableArray<Guid> selectedIds)
    {
        if (selectedIds.IsDefault || selectedIds.Length != 2 || selectedIds.Distinct().Count() != 2)
            throw new InvalidOperationException("Select exactly the two original opening sales, excluding any later test liquidation.");
        var candidates = OpeningSaleCandidates(anchor, journal, session);
        if (selectedIds.Any(id => journal.Count(s => s.Intent.RequestId == id) != 1 ||
            candidates.Count(s => s.Intent.RequestId == id) != 1))
            throw new InvalidOperationException("Each selected original sale needs its unchanged native full-fill and inventory verification before the saved baseline.");
        var sales = selectedIds.Select(id => candidates.Single(s => s.Intent.RequestId == id)).ToImmutableArray();
        if (sales.Select(s => s.Intent.SecurityId).Distinct().Count() != 2)
            throw new InvalidOperationException("The original opening sales must refer to two distinct holdings.");
        return sales;
    }

    public static BrokerNoTransferSessionScope Prepare(BrokerAccountRiskBaseline baseline,
        BrokerImportReceipt baselineImport, BrokerAccountRiskState continued,
        ImmutableArray<ManualSellRecord> openingSales, string reviewedBy,
        ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var anchor = baselineImport.Plan.Observation;
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        var carry = continued.TrialCarry;
        bool originalSession = carry is null && baseline.TradingDate == date && continued.Through == baseline.ObservationAt;
        bool carriedSession = carry is not null && carry.Session == date &&
            BrokerAccountImport.Hash(carry.Baseline) == BrokerAccountImport.Hash(baseline) &&
            BrokerAccountImport.Hash(carry.OriginalAnchor) == BrokerAccountImport.Hash(anchor) &&
            continued.RevisionId == BrokerTrialCarryForward.RequestId(baseline.BaselineId) &&
            continued.Through == carry.ReviewedAccount.SnapshotAt;
        if (baselineImport.RequestId != baseline.ImportRequestId || !baselineImport.Plan.CanImport ||
            baselineImport.BindingId != baseline.BindingId || baseline.AccountLabel != "TFSA" ||
            baseline.ObservationAt != anchor.SnapshotAt || !(originalSession || carriedSession) ||
            continued.BaselineId != baseline.BaselineId || continued.BindingId != baseline.BindingId ||
            continued.TradingDate != date ||
            continued.SessionReference != baseline.SessionReference || continued.DrawdownReference != baseline.DrawdownReference ||
            continued.DailyLossHold || continued.CapitalReviewRequired)
            throw new InvalidOperationException("Use the original saved cash-only baseline/import and preserve its current references and holds. Trial preparation cannot reset or replace them.");
        var scope = new BrokerNoTransferSessionScope(
            BrokerManagedStrategy.StableId($"{baseline.BaselineId:D}:{baselineImport.RequestId:D}:LimitedFirstSessionTrialV1" +
                (carry is null ? "" : ":" + carry.Fingerprint)),
            baseline.BindingId, baseline.BaselineId, date, now, reviewedBy, anchor)
            { OpeningSales = openingSales, LimitedFirstSessionTrial = true, TrialCarry = carry,
                FullFillOnlyNativeTrial = carry is not null,
                PurchaseExpiresAt = calendar.GetSessionBounds(date).CloseUtc };
        scope.Validate(calendar);
        if (now >= calendar.GetSessionBounds(date).CloseUtc || !scope.OpeningSalesVerified)
            throw new InvalidOperationException("The limited trial requires both original full sales and verified empty inventories before today's close.");
        return scope;
    }
}
