#nullable enable
using Core.Trader.DelphiLive;
using Core.Runtime;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace Core.Trader.Promotion;

public sealed record BrokerRiskHolding(Guid PositionId, string Symbol, int Quantity,
    decimal? SavedPrice, Guid? SavedPriceEvidenceId, DateTimeOffset? SavedPriceAt)
{
    public override string ToString() => "Account boundary holding (financial data omitted)";
}

/// <summary>
/// The real book reconciled AT the boundary, not the latest book read later. A recovering caller must
/// reconstruct boundary holdings/cash from complete broker evidence before constructing this input.
/// Cash here is owned account cash for valuation, not buying power or cash less order reservations.
/// </summary>
public sealed record BrokerRiskBoundaryBook(Guid EvidenceId, Guid BindingId, DateTimeOffset At,
    DateTimeOffset VerifiedAt, decimal Cash, ImmutableArray<BrokerRiskHolding> Holdings)
{
    public override string ToString() => "Reconciled account boundary book (financial data omitted)";
}

public sealed record BrokerRiskHoldingBar(string Symbol, TradingPriceBar Bar)
{
    public override string ToString() => "Account boundary price evidence (financial data omitted)";
}
public enum BrokerRiskBoundaryKind { Opening, Closing }

/// <summary>A dated valuation with the selected family's price definition and exact source fingerprint.</summary>
public sealed record BrokerRiskBoundaryValue(Guid EvidenceId, Guid BindingId, DateOnly TradingDate,
    BrokerRiskBoundaryKind Kind, string ValuationContract, DateTimeOffset At, DateTimeOffset ObservedAt,
    decimal Value, string SourceFingerprint)
{
    public override string ToString() => "Account boundary valuation (financial data omitted)";
}

public sealed record BrokerRiskValuationResult(BrokerRiskBoundaryValue? Value, ImmutableArray<string> MissingEvidence)
{
    public bool Complete => Value is not null && MissingEvidence.IsEmpty;
    public override string ToString() => "Account valuation result (no trading authority)";
}

/// <summary>
/// Shared, offline valuation; does not select a new policy, fetch data, save a reference or pause buys.
/// Recovered price evidence remains recovered evidence and never becomes on-time paper performance.
/// </summary>
public static class BrokerAccountSessionValuation
{
    public const string ShadowOpening = "ShadowSavedOpeningBookV1";
    public const string LiveOpening = "LiveFirstFiveMinuteOpenV1";
    public const string ShadowClosing = "ShadowSavedClosingBookV1";
    public const string SessionClosing = "ExactFiveMinuteSessionCloseV1";

    public static BrokerRiskValuationResult Calculate(Guid evidenceId, TradingStrategyFamily family,
        BrokerRiskBoundaryKind kind, DateOnly date, BrokerRiskBoundaryBook book,
        ImmutableArray<BrokerRiskHoldingBar> bars, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(calendar);
        if (!Enum.IsDefined(family) || !Enum.IsDefined(kind) || evidenceId == Guid.Empty ||
            book.EvidenceId == Guid.Empty || book.BindingId == Guid.Empty || book.Cash < 0m ||
            book.Holdings.IsDefault || bars.IsDefault || book.At == default || book.VerifiedAt < book.At ||
            book.VerifiedAt > now || book.Holdings.Any(h => h.PositionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(h.Symbol) || h.Quantity <= 0) ||
            book.Holdings.Select(h => h.PositionId).Distinct().Count() != book.Holdings.Length ||
            book.Holdings.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != book.Holdings.Length)
            throw new ArgumentException("Valuation requires a complete, owned, reconciled boundary book.");
        var bounds = calendar.GetSessionBounds(date);
        var at = kind == BrokerRiskBoundaryKind.Opening ? bounds.OpenUtc : bounds.CloseUtc;
        if (book.At != at || now < at)
            throw new ArgumentException("The book must describe the exact reviewed session boundary.");

        bool savedMarks = family == TradingStrategyFamily.DailyShadow;
        string contract = savedMarks ? kind == BrokerRiskBoundaryKind.Opening ? ShadowOpening : ShadowClosing
            : kind == BrokerRiskBoundaryKind.Opening ? LiveOpening : SessionClosing;
        var missing = ImmutableArray.CreateBuilder<string>();
        var usedBars = ImmutableArray.CreateBuilder<BrokerRiskHoldingBar>();
        decimal value = book.Cash;
        var observedAt = book.VerifiedAt;
        foreach (var holding in book.Holdings.OrderBy(h => h.PositionId))
        {
            decimal price;
            if (savedMarks)
            {
                // Preserve Shadow's current saved-book definition; do not silently substitute a
                // broker graph or first-bar Open and call that the unchanged paper strategy.
                if (holding.SavedPrice is not > 0m || holding.SavedPriceEvidenceId is null ||
                    holding.SavedPriceEvidenceId == Guid.Empty || holding.SavedPriceAt is null ||
                    holding.SavedPriceAt == default(DateTimeOffset) || holding.SavedPriceAt > book.At)
                { missing.Add("The saved boundary book needs identified price evidence for every holding."); continue; }
                price = holding.SavedPrice.Value;
            }
            else
            {
                var start = kind == BrokerRiskBoundaryKind.Opening ? bounds.OpenUtc : bounds.CloseUtc.AddMinutes(-5);
                var matches = bars.Where(b => string.Equals(b.Symbol, holding.Symbol, StringComparison.OrdinalIgnoreCase) &&
                    b.Bar.StartUtc == start && b.Bar.Minutes == 5).ToArray();
                if (matches.Length != 1)
                { missing.Add("An exact, unambiguous boundary bar is required for every holding."); continue; }
                var bar = matches[0].Bar;
                if (bar.EvidenceId == Guid.Empty || bar.StartUtc.Kind != DateTimeKind.Utc ||
                    bar.ReceivedUtc.Kind != DateTimeKind.Utc || bar.ReceivedUtc < start.AddMinutes(5) ||
                    bar.ReceivedUtc > now.UtcDateTime || bar.Low <= 0m || bar.High < bar.Low ||
                    bar.Open < bar.Low || bar.Open > bar.High || bar.Close < bar.Low || bar.Close > bar.High)
                { missing.Add("Boundary price evidence has invalid values or observation times."); continue; }
                price = kind == BrokerRiskBoundaryKind.Opening ? bar.Open : bar.Close;
                if (bar.ReceivedUtc > observedAt) observedAt = bar.ReceivedUtc;
                usedBars.Add(matches[0]);
            }
            value += holding.Quantity * price;
        }
        if (value <= 0m) missing.Add("Account valuation must be positive to calculate the existing percentage loss checks.");
        if (missing.Count > 0) return new(null, missing.Distinct().ToImmutableArray());
        string fingerprint = EngineStrategySettings.Hash(JsonSerializer.Serialize(new
        {
            Contract = contract,
            Book = book with { Holdings = book.Holdings.OrderBy(h => h.PositionId).ToImmutableArray() },
            Bars = usedBars.OrderBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase).ToArray()
        }));
        return new(new(evidenceId, book.BindingId, date, kind, contract, at, observedAt, value, fingerprint), []);
    }

    /// <summary>
    /// Cross-check only. Agreement at a single boundary does not qualify the broker graph as an
    /// interchangeable valuation source or establish when it becomes available each morning.
    /// The caller must already have checked the graph's owned account against the saved binding.
    /// </summary>
    public static bool? CompareGraph(BrokerRiskBoundaryValue value, WealthsimpleAccountDayValues graph,
        WealthsimpleTfsaAccount expectedAccount)
    {
        if (!ReferenceEquals(graph.Account, expectedAccount))
            throw new ArgumentException("The graph must belong to the explicitly selected account.");
        var point = graph.FindExact(value.At);
        return point is null ? null : System.Math.Abs(point.Value - value.Value) <= 0.01m;
    }
}
