#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Operator-scoped no-transfer reconciliation, not a claim of complete native cash-flow coverage.</summary>
public sealed class BrokerOngoingRiskEvidenceSource(BrokerOngoingManagement agreement, ITsxSessionCalendar calendar,
    Func<ImmutableArray<string>, DateTime, CancellationToken, Task<ImmutableArray<BrokerRiskHoldingBar>>> readBars,
    TimeProvider? time = null, bool reviewOnly = false) : IBrokerRiskEvidenceSource
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state, CancellationToken ct) =>
        ReadAsync(import, state, [], ct);

    public async Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state,
        ImmutableArray<BrokerStrategyOrder> owned, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var current = import.Plan.Observation;
        var requests = ImmutableArray.CreateBuilder<BrokerRiskContinuationRequest>();
        BrokerRiskEvidence Block(string reason) => new(requests.ToImmutable(), null, [reason]);
        if (!agreement.NoOutsideChangesConfirmed || !agreement.PauseBeforeOutsideChanges ||
            agreement.Review.BindingId != import.BindingId || state.BindingId != import.BindingId ||
            state.BaselineId != agreement.Review.BaselineId || agreement.PreparedAt > now ||
            (!reviewOnly && (agreement.ApprovedAt is null || agreement.ApprovedAt > now || state.OngoingAuthorization != agreement.RiskAuthorization)) ||
            current.SnapshotAt > now || now - current.SnapshotAt > TimeSpan.FromSeconds(30) ||
            current.CheckedAt < current.SnapshotAt || current.CheckedAt > now || !current.HoldingsMatch ||
            state.Through > current.SnapshotAt)
            return Block("Ongoing management needs the approved agreement and fresh reconciled account facts.");
        ImmutableArray<BrokerOrderFact> added;
        try { added = Reconcile(agreement, current, owned); }
        catch (InvalidOperationException) { return Block("Cash, holdings or orders differ from the no-transfer agreement. Stop new buys and review outside activity or unresolved orders."); }
        if (reviewOnly) return new([], new(state.BindingId, state.Through, current.SnapshotAt, now,
            BrokerOngoingManagement.Contract, BrokerAccountImport.FinancialFingerprint(current)), []);

        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (!calendar.IsRegularSession(date) || state.TradingDate > date)
            return Block("A reviewed current trading session and forward-only risk history are required.");
        // Recover exact closes and opens in calendar order. A later receipt never claims on-time observation.
        while (state.TradingDate < date)
        {
            ct.ThrowIfCancellationRequested();
            if (state.LastCompletedSession != state.TradingDate)
            {
                var close = await Boundary(BrokerRiskBoundaryKind.Closing, state.TradingDate);
                if (close is null) return Block("Recover the exact closing book and prices before advancing account risk. Existing protection remains independently checked.");
                Append(close);
            }
            var nextDate = calendar.GetNextSession(state.TradingDate);
            var open = await Boundary(BrokerRiskBoundaryKind.Opening, nextDate);
            if (open is null) return Block("The next session's exact opening book or price evidence is missing. New buys wait; prior references and protection are retained.");
            Append(open);
        }
        var fingerprint = BrokerAccountImport.Hash(new { Contract = BrokerOngoingManagement.Contract,
            agreement.RequestId, Current = BrokerAccountImport.FinancialFingerprint(current), Orders = added });
        return new(requests.ToImmutable(), new(state.BindingId, state.Through, current.SnapshotAt, now,
            BrokerOngoingManagement.Contract, fingerprint), []);

        void Append(BrokerRiskContinuationRequest request)
        {
            state = BrokerAccountRiskContinuation.Apply(state, request, calendar, now);
            requests.Add(request);
        }
        async Task<BrokerRiskContinuationRequest?> Boundary(BrokerRiskBoundaryKind kind, DateOnly session)
        {
            var bounds = calendar.GetSessionBounds(session);
            var at = new DateTimeOffset(kind == BrokerRiskBoundaryKind.Opening ? bounds.OpenUtc : bounds.CloseUtc);
            if (at > now || at < state.Through) return null;
            // A cumulative order spanning a boundary cannot locate its individual fills on either side.
            if (added.Any(o => o.CreatedAt < at && o.LastFilledAt >= at)) return null;
            var before = added.Where(o => o.LastFilledAt < at).ToImmutableArray();
            var quantities = before.GroupBy(o => (o.SecurityId, o.Symbol)).Select(g => new
                { g.Key, Quantity = g.Sum(o => (o.Side == "BUY" ? 1 : -1) * o.FilledQuantity) }).ToArray();
            if (quantities.Any(p => p.Quantity < 0 || p.Quantity != decimal.Truncate(p.Quantity) || p.Quantity > int.MaxValue)) return null;
            decimal cash = agreement.CashAnchor.Cash!.Value + before.Sum(o => (o.Side == "BUY" ? -1m : 1m) * o.NetValue!.Value);
            var holdings = quantities.Where(p => p.Quantity > 0).Select(p => new BrokerRiskHolding(
                BrokerManagedStrategy.StableId($"{state.BindingId:D}:{p.Key.SecurityId}:{at:O}"), p.Key.Symbol,
                (int)p.Quantity, null, null, null)).ToImmutableArray();
            var id = BrokerManagedStrategy.StableId($"{agreement.RequestId:D}:{session:yyyy-MM-dd}:{kind}");
            var book = new BrokerRiskBoundaryBook(id, state.BindingId, at, now, cash, holdings);
            var start = kind == BrokerRiskBoundaryKind.Opening ? bounds.OpenUtc : bounds.CloseUtc.AddMinutes(-5);
            var names = quantities.Where(p => p.Quantity > 0).ToDictionary(p => p.Key.Symbol,
                p => owned.First(o => o.SecurityId == p.Key.SecurityId && o.Side == "BUY").Symbol, StringComparer.OrdinalIgnoreCase);
            var marketBars = holdings.IsEmpty ? [] : await readBars(holdings.Select(h => names[h.Symbol]).ToImmutableArray(), start, ct);
            var bars = holdings.SelectMany(h => marketBars.Where(b => string.Equals(b.Symbol, names[h.Symbol], StringComparison.OrdinalIgnoreCase))
                .Select(b => b with { Symbol = h.Symbol })).ToImmutableArray();
            var valuation = BrokerAccountSessionValuation.Calculate(id, TradingStrategyFamily.DelphiLive, kind,
                session, book, bars, calendar, now);
            if (!valuation.Complete) return null;
            var flowHash = BrokerAccountImport.Hash(new { Contract = BrokerOngoingManagement.Contract, agreement.RequestId,
                From = state.Through, Through = at, Current = BrokerAccountImport.FinancialFingerprint(current), Orders = added });
            return new(id, state.BaselineId, state.RevisionId,
                kind == BrokerRiskBoundaryKind.Opening ? BrokerRiskContinuationKind.Opening : BrokerRiskContinuationKind.Closing,
                valuation.Value, new(id, state.BindingId, state.Through, at, now, flowHash), null);
        }
    }

    internal static ImmutableArray<BrokerOrderFact> Reconcile(BrokerOngoingManagement agreement,
        BrokerAccountReconciliation current, ImmutableArray<BrokerStrategyOrder> owned)
    {
        var anchor = agreement.CashAnchor;
        if (anchor.IdentityId != current.IdentityId || anchor.AccountId != current.AccountId ||
            anchor.Cash is not > 0 || anchor.Holdings.Any(h => h.BrokerQuantity != 0) || !current.HoldingsMatch ||
            current.Orders.Select(o => o.ExternalId).Distinct().Count() != current.Orders.Count ||
            owned.Select(o => o.ExternalId).Distinct().Count() != owned.Length ||
            anchor.Orders.Any(o => !current.Orders.Any(n => n.ExternalId == o.ExternalId && n.IsSameOrPostedSuccessor(o))))
            throw new InvalidOperationException("Account or original history differs.");
        var originalIds = anchor.Orders.Select(o => o.ExternalId).ToHashSet(StringComparer.Ordinal);
        var added = current.Orders.Where(o => !originalIds.Contains(o.ExternalId)).ToImmutableArray();
        if (added.Length != owned.Length || added.Any(o => !owned.Any(a => a.ExternalId == o.ExternalId &&
            a.SecurityId == o.SecurityId && BrokerMarketSymbols.Matches(current, o.SecurityId, a.Symbol, o.Symbol) && a.Side == o.Side && a.Quantity == o.SubmittedQuantity) ||
            o.Status is not ("FILLED" or "POSTED") || o.FilledQuantity != o.SubmittedQuantity || o.FilledQuantity <= 0 ||
            o.NetValue is not > 0 || o.Currency != "CAD" || o.Side is not ("BUY" or "SELL") ||
            o.LastFilledAt is null || o.LastFilledAt < o.CreatedAt || o.LastFilledAt > current.SnapshotAt ||
            o.CreatedAt < agreement.ApprovedAt || o.CreatedAt < anchor.SnapshotAt))
            throw new InvalidOperationException("Outside, uncertain or incomplete orders need review.");
        decimal expected = anchor.Cash.Value + added.Sum(o => (o.Side == "BUY" ? -1m : 1m) * o.NetValue!.Value);
        if (current.Cash != expected) throw new InvalidOperationException("Unexplained cash movement.");
        var net = added.GroupBy(o => o.SecurityId).ToDictionary(g => g.Key,
            g => g.Sum(o => (o.Side == "BUY" ? 1m : -1m) * o.FilledQuantity));
        if (net.Any(p => p.Value < 0 || current.Holdings.Where(h => h.SecurityId == p.Key).Sum(h => h.BrokerQuantity) != p.Value) ||
            current.Holdings.Any(h => h.BrokerQuantity > 0 && (!net.TryGetValue(h.SecurityId, out var quantity) || quantity != h.BrokerQuantity)))
            throw new InvalidOperationException("Unexplained holdings movement.");
        return added;
    }
}
