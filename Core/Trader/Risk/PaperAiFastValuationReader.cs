#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Trader.DelphiLive;

namespace Core.Trader.Risk;

public sealed record ShadowPaperAccountRead(ShadowPaperRiskObservation Book, DateTime UpdatedUtc, DateTime? LastFillUtc)
{
    public override string ToString() => "Verified Shadow account (financial data omitted)";
}

public interface ISystemShadowAccountReader
{
    Task<ShadowPaperAccountRead?> ReadAccountAsync(Guid portfolioId, DateOnly tradingDate, CancellationToken cancellationToken = default);
}

/// <summary>Reconciled current value with retained source evidence. Not risk permission or a journal transition.</summary>
public sealed record AiPaperAccountValuation(Guid PortfolioId, Guid EvidenceId, DateOnly TradingDate,
    DateTime AtUtc, DateTime CheckedUtc, DateTime ValidUntilUtc, decimal Cash, decimal HoldingsValue,
    ImmutableArray<string> SavedBlocks, string EvidenceJson, string EvidenceSha256)
{
    public const string Contract = "PaperAccountOneMinuteValuationV1";
    public decimal Value => checked(Cash + HoldingsValue);
    public bool IsFreshAt(DateTime nowUtc) => nowUtc.Kind == DateTimeKind.Utc && nowUtc >= CheckedUtc && nowUtc < ValidUntilUtc;
    public override string ToString() => "AI paper account value (financial data omitted; not trading authority)";
}

public sealed record AiPaperValuationRead(AiPaperAccountValuation? Valuation, string? BlockReason)
{
    public override string ToString() => Valuation is null ? $"AI paper valuation unavailable: {BlockReason}" : "AI paper valuation prepared";
}

/// <summary>
/// Reads the owned book, collects prices, and reads the book again. A changed book or any missing,
/// stale or differently timed price rejects the entire result. No source/account/journal mutations.
/// Production age limits, persistence, cross-observation continuity and runtime qualification remain host work.
/// </summary>
public sealed class PaperAiFastValuationReader
{
    private sealed record Holding(Guid PositionId, string Symbol, int Quantity);
    private sealed record Account(string Family, decimal Cash, ImmutableArray<Holding> Holdings,
        ImmutableArray<string> Blocks, string EvidenceJson, string IdentityJson, DateTime ReadStartedUtc,
        DateTime ReadCompletedUtc, Action<DateTime> ValidateAt);
    private sealed record BookEvidence(string Family, string BookJson, DateTime ReadStartedUtc, DateTime ReadCompletedUtc);
    private sealed record Evidence(string Contract, Guid PortfolioId, DateOnly TradingDate, DateTime AtUtc,
        DateTime CheckedUtc, DateTime ValidUntilUtc, decimal Cash, decimal HoldingsValue,
        ImmutableArray<string> SavedBlocks, BookEvidence Before, BookEvidence After, AiRiskPriceRead? Prices);
    private sealed class Rejected(string reason) : Exception(reason);

    private readonly Guid portfolioId;
    private readonly Func<DateOnly, CancellationToken, Task<Account>> readAccount;
    private readonly TmxAiRiskPriceSource prices;
    private readonly ITsxSessionCalendar calendar;
    private readonly TimeSpan maximumAccountAge;
    private readonly TimeProvider clock;

    public PaperAiFastValuationReader(Guid portfolioId, ISystemShadowAccountReader source,
        TmxAiRiskPriceSource prices, ITsxSessionCalendar calendar, TimeSpan maximumAccountAge, TimeProvider? clock = null)
        : this(portfolioId, prices, calendar, maximumAccountAge, clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        readAccount = async (date, ct) =>
        {
            var began = Now;
            var raw = await source.ReadAccountAsync(portfolioId, date, ct) ?? throw new Rejected("AccountUnavailable");
            ct.ThrowIfCancellationRequested();
            var ended = Now;
            var book = raw.Book;
            if (book is null || book.TradingDate != date || book.ObservedUtc < began || book.ObservedUtc > ended ||
                book.At != book.ObservedUtc || raw.UpdatedUtc.Kind != DateTimeKind.Utc || raw.UpdatedUtc > book.ObservedUtc ||
                raw.LastFillUtc is { } fill && (fill.Kind != DateTimeKind.Utc || fill > book.ObservedUtc))
                throw new Rejected("InvalidAccountReceipt");
            void Validate(DateTime at)
            {
                if (raw.LastFillUtc > at) throw new Rejected("TradeAfterPrice");
                SystemShadowAiPortfolioRiskMonitor.ValidateAccount(portfolioId, book with { At = at }, new(ended));
            }
            Validate(book.At);
            // Receipts change on each SQL read. Everything else, including history, settings and guards, must agree.
            var identity = raw with { Book = book with { EvidenceId = Guid.Empty, At = default, ObservedUtc = default } };
            return new("Shadow", book.Portfolio.CashBalance,
                book.Positions.Where(h => h.Status == "Open").OrderBy(h => h.PositionId)
                    .Select(h => new Holding(h.PositionId, h.Symbol, h.Shares)).ToImmutableArray(),
                SystemShadowAiPortfolioRiskMonitor.WithSavedHolds(book, []), JsonSerializer.Serialize(raw),
                JsonSerializer.Serialize(identity), began, ended, Validate);
        };
    }

    public PaperAiFastValuationReader(Guid portfolioId, IDelphiLivePortfolioReader source,
        TmxAiRiskPriceSource prices, ITsxSessionCalendar calendar, TimeSpan maximumAccountAge, TimeProvider? clock = null)
        : this(portfolioId, prices, calendar, maximumAccountAge, clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        readAccount = async (date, ct) =>
        {
            var began = Now;
            var book = await source.LoadPortfolioAsync(portfolioId, ct) ?? throw new Rejected("AccountUnavailable");
            ct.ThrowIfCancellationRequested();
            var ended = Now;
            DelphiLiveAiPortfolioRiskMonitor.ValidateAccount(portfolioId, book, new(ended));
            if (book.CurrentSession != date || book.EffectiveSession > date ||
                !book.Marks.IsDefaultOrEmpty && book.Marks[^1].Reason == "CorporateActionUnsupported")
                throw new Rejected("InvalidAccountSession");
            void Validate(DateTime at) => DelphiLiveAiPortfolioRiskMonitor.ValidateLedger(book, at);
            Validate(ended);
            string json = JsonSerializer.Serialize(book);
            return new("DelphiLive", book.Cash,
                book.OpenPositions.OrderBy(h => h.PositionId).Select(h => new Holding(h.PositionId, h.Symbol, h.Quantity)).ToImmutableArray(),
                DelphiLiveAiPortfolioRiskMonitor.WithSavedHolds(book, []), json, json, began, ended, Validate);
        };
    }

    private PaperAiFastValuationReader(Guid portfolioId, TmxAiRiskPriceSource prices, ITsxSessionCalendar calendar,
        TimeSpan maximumAccountAge, TimeProvider? clock)
    {
        if (portfolioId == Guid.Empty || maximumAccountAge <= TimeSpan.Zero || maximumAccountAge > TimeSpan.FromMinutes(5))
            throw new ArgumentException("An owned paper account and explicit bounded account age are required.");
        this.portfolioId = portfolioId;
        this.prices = prices ?? throw new ArgumentNullException(nameof(prices));
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        this.maximumAccountAge = maximumAccountAge;
        this.clock = clock ?? TimeProvider.System;
        readAccount = null!; // Both public constructors install a concrete authoritative reader.
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AiPaperValuationRead> ReadAsync(DateOnly date, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var bounds = calendar.GetSessionBounds(date);
            var started = Now;
            if (started < bounds.OpenUtc || started >= bounds.CloseUtc) throw new Rejected("OutsideSession");
            var before = await readAccount(date, ct);
            var symbols = before.Holdings.Select(h => h.Symbol).ToImmutableArray();
            AiRiskPriceRead? priceRead = symbols.IsEmpty ? null : await prices.ReadAsync(date, symbols, ct);
            var after = await readAccount(date, ct);
            var now = Now;
            ct.ThrowIfCancellationRequested();
            if (before.ReadStartedUtc < started || before.ReadCompletedUtc < before.ReadStartedUtc ||
                after.ReadStartedUtc < before.ReadCompletedUtc || after.ReadCompletedUtc < after.ReadStartedUtc || now < after.ReadCompletedUtc)
                throw new Rejected("ClockChanged");
            if (before.IdentityJson != after.IdentityJson) throw new Rejected("AccountChangedDuringPrices");
            var until = new[] { before.ReadStartedUtc.Add(maximumAccountAge), after.ReadStartedUtc.Add(maximumAccountAge), bounds.CloseUtc }.Min();
            // Cash has no market bar. Anchor its evidence to the first read, never to a later display/poll time.
            DateTime at = before.ReadStartedUtc;
            decimal holdingsValue = 0m;
            if (priceRead is not null)
            {
                if (priceRead.TradingDate != date || priceRead.SessionCloseUtc != bounds.CloseUtc ||
                    priceRead.CheckedUtc < before.ReadCompletedUtc || priceRead.CheckedUtc > after.ReadStartedUtc ||
                    !priceRead.IsCompleteAndFreshAt(now) || priceRead.Results.Length != symbols.Length)
                    throw new Rejected("PricesIncompleteOrExpired");
                var evidence = priceRead.Results.Select(r => r.Evidence!).ToArray();
                if (evidence.Select(e => e.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != symbols.Length ||
                    evidence.Any(e => !symbols.Contains(e.Symbol, StringComparer.OrdinalIgnoreCase) || e.FetchStartedUtc < before.ReadCompletedUtc))
                    throw new Rejected("PriceOwnershipMismatch");
                at = evidence[0].CompletedUtc;
                if (at < bounds.OpenUtc || at > priceRead.RequestedEndUtc || evidence.Any(e => e.CompletedUtc != at))
                    throw new Rejected("PricesHaveDifferentTimes");
                until = evidence.SelectMany(e => new[] { e.PriceExpiresUtc, e.ReceiptExpiresUtc }).Append(until).Min();
                foreach (var holding in after.Holdings)
                    holdingsValue = checked(holdingsValue + holding.Quantity * evidence.Single(e =>
                        string.Equals(e.Symbol, holding.Symbol, StringComparison.OrdinalIgnoreCase)).Bar.Close);
            }
            after.ValidateAt(at);
            if (now >= until) throw new Rejected("AccountOrPricesExpired");
            _ = checked(after.Cash + holdingsValue);
            var evidenceRecord = new Evidence(AiPaperAccountValuation.Contract, portfolioId, date, at, now, until,
                after.Cash, holdingsValue, after.Blocks,
                new(before.Family, before.EvidenceJson, before.ReadStartedUtc, before.ReadCompletedUtc),
                new(after.Family, after.EvidenceJson, after.ReadStartedUtc, after.ReadCompletedUtc), priceRead);
            string json = JsonSerializer.Serialize(evidenceRecord);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return new(new(portfolioId, new Guid(hash.AsSpan(0, 16)), date, at, now, until, after.Cash, holdingsValue,
                after.Blocks, json, Convert.ToHexString(hash)), null);
        }
        catch (Rejected ex) { return new(null, ex.Message); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        { return new(null, "InvalidAccountOrPriceEvidence"); }
        // Cancellation and database/storage failures propagate; the caller must withhold new risk.
    }
}
