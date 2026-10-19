#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;

namespace Core.Trader.Promotion;

/// <summary>Builds only the expressly approved scoped cash source from saved native facts.
/// Does not create initial references, qualify other launch checks, save authority or place orders.</summary>
public static class BrokerSupervisedTrialPreparation
{
    public static BrokerNoTransferSessionScope Prepare(BrokerAccountRiskBaseline baseline,
        BrokerImportReceipt baselineImport, BrokerAccountRiskState continued,
        ImmutableArray<ManualSellRecord> openingSales, string reviewedBy,
        ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var anchor = baselineImport.Plan.Observation;
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (baselineImport.RequestId != baseline.ImportRequestId || !baselineImport.Plan.CanImport ||
            baselineImport.BindingId != baseline.BindingId || baseline.AccountLabel != "TFSA" ||
            baseline.ObservationAt != anchor.SnapshotAt || baseline.TradingDate != date ||
            continued.BaselineId != baseline.BaselineId || continued.BindingId != baseline.BindingId ||
            continued.TradingDate != date || continued.Through != baseline.ObservationAt ||
            continued.SessionReference != baseline.SessionReference || continued.DrawdownReference != baseline.DrawdownReference ||
            continued.DailyLossHold || continued.CapitalReviewRequired)
            throw new InvalidOperationException("Use the original saved cash-only baseline/import and preserve its current references and holds. Trial preparation cannot reset or replace them.");
        var scope = new BrokerNoTransferSessionScope(
            BrokerManagedStrategy.StableId($"{baseline.BaselineId:D}:{baselineImport.RequestId:D}:LimitedFirstSessionTrialV1"),
            baseline.BindingId, baseline.BaselineId, date, now, reviewedBy, anchor)
            { OpeningSales = openingSales, LimitedFirstSessionTrial = true,
                PurchaseExpiresAt = calendar.GetSessionBounds(date).CloseUtc };
        scope.Validate(calendar);
        if (now >= calendar.GetSessionBounds(date).CloseUtc || !scope.OpeningSalesVerified)
            throw new InvalidOperationException("The limited trial requires both original full sales and verified empty inventories before today's close.");
        return scope;
    }
}
