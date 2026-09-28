#nullable enable
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Trader.DelphiLive;
using Evidence = Core.Trader.Risk.PaperAiFastValuationReader.Evidence;
using BookEvidence = Core.Trader.Risk.PaperAiFastValuationReader.BookEvidence;

namespace Core.Trader.Risk;

/// <summary>Revalidates retained source bytes at the durable risk boundary; checksums are not source authentication.</summary>
internal static class PaperAiFastRiskEvidence
{
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static T Read<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("Source evidence is absent.");
    internal static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new InvalidOperationException("Complete consistent paper source evidence is required.");
    }
    private static bool Utc(DateTime value) => value.Kind == DateTimeKind.Utc;
    private static string ShadowIdentity(ShadowPaperAccountRead value) => JsonSerializer.Serialize(value with
        { Book = value.Book with { EvidenceId = Guid.Empty, At = default, ObservedUtc = default } });

    internal static Evidence Validate(AiPaperAccountValuation v, Guid portfolio, ITsxSessionCalendar calendar,
        TimeSpan accountAge, TimeSpan priceAge, TimeSpan receiptAge, DateTimeOffset now)
    {
        Require(v.PortfolioId == portfolio && v.EvidenceId != Guid.Empty && v.EvidenceSha256 == Hash(v.EvidenceJson));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(v.EvidenceJson));
        Require(v.EvidenceId == new Guid(hash.AsSpan(0, 16)));
        var e = Read<Evidence>(v.EvidenceJson);
        Require(e.Contract == AiPaperAccountValuation.Contract && e.PortfolioId == portfolio && e.TradingDate == v.TradingDate &&
            e.AtUtc == v.AtUtc && e.CheckedUtc == v.CheckedUtc && e.ValidUntilUtc == v.ValidUntilUtc &&
            e.Cash == v.Cash && e.HoldingsValue == v.HoldingsValue && !e.SavedBlocks.IsDefault && !v.SavedBlocks.IsDefault &&
            e.SavedBlocks.SequenceEqual(v.SavedBlocks) && Utc(e.AtUtc) && Utc(e.CheckedUtc) && Utc(e.ValidUntilUtc) &&
            e.AtUtc <= e.CheckedUtc && e.CheckedUtc <= now.UtcDateTime && e.ValidUntilUtc > e.CheckedUtc &&
            e.Before is not null && e.After is not null && e.Before.Family == e.After.Family);
        var bounds = calendar.GetSessionBounds(e.TradingDate);
        Require(e.AtUtc >= bounds.OpenUtc && e.AtUtc < bounds.CloseUtc && e.CheckedUtc < bounds.CloseUtc &&
            e.Before.ReadStartedUtc >= bounds.OpenUtc && e.Before.ReadCompletedUtc <= e.After.ReadStartedUtc &&
            e.After.ReadCompletedUtc <= e.CheckedUtc && e.ValidUntilUtc <= bounds.CloseUtc);
        foreach (var book in new[] { e.Before, e.After })
            Require(Utc(book.ReadStartedUtc) && Utc(book.ReadCompletedUtc) && book.ReadStartedUtc <= book.ReadCompletedUtc &&
                e.ValidUntilUtc <= book.ReadStartedUtc.Add(accountAge));
        decimal cash;
        ImmutableArray<(string Symbol, int Quantity)> held;
        ImmutableArray<string> blocks;
        if (e.After.Family == "Shadow")
        {
            var before = Read<ShadowPaperAccountRead>(e.Before.BookJson);
            var after = Read<ShadowPaperAccountRead>(e.After.BookJson);
            ValidateShadow(before, e.Before, e.TradingDate, portfolio);
            ValidateShadow(after, e.After, e.TradingDate, portfolio);
            Require(ShadowIdentity(before) == ShadowIdentity(after) &&
                (after.LastFillUtc is null || after.LastFillUtc <= e.AtUtc));
            SystemShadowAiPortfolioRiskMonitor.ValidateAccount(portfolio, after.Book with { At = e.AtUtc }, now);
            cash = after.Book.Portfolio.CashBalance;
            held = after.Book.Positions.Where(h => h.Status == "Open").Select(h => (h.Symbol, h.Shares)).ToImmutableArray();
            blocks = SystemShadowAiPortfolioRiskMonitor.WithSavedHolds(after.Book, []);
        }
        else
        {
            Require(e.After.Family == "DelphiLive" && e.Before.BookJson == e.After.BookJson);
            var book = Read<DelphiLivePortfolioSnapshot>(e.After.BookJson);
            DelphiLiveAiPortfolioRiskMonitor.ValidateAccount(portfolio, book, new(e.Before.ReadCompletedUtc));
            Require(book.CurrentSession == e.TradingDate && book.EffectiveSession <= e.TradingDate &&
                (book.Marks.IsDefaultOrEmpty || book.Marks[^1].Reason != "CorporateActionUnsupported"));
            DelphiLiveAiPortfolioRiskMonitor.ValidateLedger(book, e.AtUtc);
            cash = book.Cash;
            held = book.OpenPositions.Select(h => (h.Symbol, h.Quantity)).ToImmutableArray();
            blocks = DelphiLiveAiPortfolioRiskMonitor.WithSavedHolds(book, []);
        }
        Require(cash == e.Cash && blocks.SequenceEqual(e.SavedBlocks));
        decimal holdingsValue = 0m;
        if (held.IsEmpty)
            Require(e.Prices is null && e.AtUtc == e.Before.ReadStartedUtc);
        else
        {
            var p = e.Prices!;
            Require(p is not null && p.TradingDate == e.TradingDate && p.SessionCloseUtc == bounds.CloseUtc &&
                Utc(p.RequestedEndUtc) && Utc(p.CheckedUtc) && p.RequestedEndUtc.Ticks % TimeSpan.TicksPerMinute == 0 &&
                p.RequestedEndUtc >= e.AtUtc && p.RequestedEndUtc <= p.CheckedUtc &&
                p.CheckedUtc >= e.Before.ReadCompletedUtc && p.CheckedUtc <= e.After.ReadStartedUtc &&
                !p.Results.IsDefault && p.Results.Length == held.Length &&
                p.Results.Select(r => r.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() == held.Length);
            foreach (var h in held)
            {
                var result = p.Results.Single(r => string.Equals(r.Symbol, h.Symbol, StringComparison.OrdinalIgnoreCase));
                var price = result.Evidence!;
                Require(result.Status == AiRiskPriceStatus.Fresh && price is not null && price.Symbol == result.Symbol &&
                    price.Bar is not null && Utc(price.Bar.TimestampUtc) && price.Bar.TimestampUtc.Ticks % TimeSpan.TicksPerMinute == 0 &&
                    price.Bar.TimestampUtc >= bounds.OpenUtc && price.CompletedUtc == e.AtUtc &&
                    Utc(price.FetchStartedUtc) && Utc(price.ReceivedUtc) && price.FetchStartedUtc >= e.Before.ReadCompletedUtc &&
                    price.FetchStartedUtc <= price.ReceivedUtc && price.ReceivedUtc >= e.AtUtc && price.ReceivedUtc <= p.CheckedUtc &&
                    Utc(price.PriceExpiresUtc) && Utc(price.ReceiptExpiresUtc) &&
                    price.PriceExpiresUtc >= e.AtUtc && price.PriceExpiresUtc <= e.AtUtc.Add(priceAge) &&
                    price.ReceiptExpiresUtc >= price.ReceivedUtc && price.ReceiptExpiresUtc <= price.ReceivedUtc.Add(receiptAge) &&
                    e.ValidUntilUtc <= price.PriceExpiresUtc && e.ValidUntilUtc <= price.ReceiptExpiresUtc &&
                    price.Bar.Low > 0 && price.Bar.High >= price.Bar.Low && price.Bar.Open >= price.Bar.Low &&
                    price.Bar.Open <= price.Bar.High && price.Bar.Close >= price.Bar.Low && price.Bar.Close <= price.Bar.High && price.Bar.Volume >= 0);
                holdingsValue = checked(holdingsValue + h.Quantity * price.Bar.Close);
            }
        }
        Require(holdingsValue == e.HoldingsValue);
        return e;
    }

    private static void ValidateShadow(ShadowPaperAccountRead a, BookEvidence receipt, DateOnly date, Guid portfolio)
    {
        Require(a.Book is not null && a.Book.TradingDate == date && a.Book.At == a.Book.ObservedUtc &&
            a.Book.ObservedUtc >= receipt.ReadStartedUtc && a.Book.ObservedUtc <= receipt.ReadCompletedUtc &&
            Utc(a.UpdatedUtc) && a.UpdatedUtc <= a.Book.ObservedUtc &&
            (a.LastFillUtc is null || Utc(a.LastFillUtc.Value) && a.LastFillUtc <= a.Book.ObservedUtc));
        SystemShadowAiPortfolioRiskMonitor.ValidateAccount(portfolio, a.Book, new(receipt.ReadCompletedUtc));
    }

    internal static bool ConflictingBars(Evidence old, Evidence current) => old.Prices is { } previous && current.Prices is { } next &&
        previous.Results.Any(p => next.Results.Any(n => p.Evidence!.Symbol == n.Evidence!.Symbol &&
            p.Evidence.Bar.TimestampUtc == n.Evidence.Bar.TimestampUtc &&
            JsonSerializer.Serialize(p.Evidence.Bar) != JsonSerializer.Serialize(n.Evidence.Bar)));

    internal static bool SameBook(Evidence a, Evidence b) => a.After.Family == b.After.Family &&
        (a.After.Family == "Shadow" ? ShadowIdentity(Read<ShadowPaperAccountRead>(a.After.BookJson)) ==
            ShadowIdentity(Read<ShadowPaperAccountRead>(b.After.BookJson)) : a.After.BookJson == b.After.BookJson);

    internal static void Continuity(PortfolioRiskValuation previous, Evidence next, bool handover)
    {
        var source = previous.Source ?? throw new InvalidOperationException("Previous source evidence is required.");
        Require(source.Sha256 == Hash(source.EvidenceJson));
        Evidence? old = source.Contract == AiPaperAccountValuation.Contract ? Read<Evidence>(source.EvidenceJson) : null;
        Require(old is not null ? old.After.Family == next.After.Family && old.CheckedUtc <= next.CheckedUtc : handover);
        if (next.After.Family == "Shadow")
        {
            Require(old is not null || source.Contract == SystemShadowAiPortfolioRiskMonitor.ValuationContract);
            var a = old is null ? Read<ShadowPaperRiskObservation>(source.EvidenceJson) : Read<ShadowPaperAccountRead>(old.After.BookJson).Book;
            var b = Read<ShadowPaperAccountRead>(next.After.BookJson).Book;
            Require(a.Generation.GenerationId == b.Generation.GenerationId && a.Generation.TotalAccountValue == b.Generation.TotalAccountValue &&
                a.Generation.PolicyVersion == b.Generation.PolicyVersion && a.Portfolio.StrategyVersionId == b.Portfolio.StrategyVersionId &&
                a.Portfolio.ComparisonAccount == b.Portfolio.ComparisonAccount && a.Portfolio.Settings == b.Portfolio.Settings &&
                a.ObservedUtc <= b.ObservedUtc && a.Positions.All(h => b.Positions.Any(n => n.PositionId == h.PositionId &&
                    (h.Status == "Closed" ? n == h : n.Symbol == h.Symbol && n.EntryUtc == h.EntryUtc && n.Shares >= h.Shares &&
                        n.CostBasis >= h.CostBasis && (n.Shares != h.Shares || n.CostBasis == h.CostBasis && n.AverageCost == h.AverageCost)))));
        }
        else
        {
            Require(old is not null || source.Contract == DelphiLiveAiPortfolioRiskMonitor.ValuationContract);
            var b = Read<DelphiLivePortfolioSnapshot>(next.After.BookJson);
            var a = old is null ? Read<DelphiLiveAiPortfolioRiskMonitor.Source>(source.EvidenceJson) : Project(Read<DelphiLivePortfolioSnapshot>(old.After.BookJson));
            Require(a.GenerationId == b.GenerationId && a.StartingCapital == b.StartingCapital && a.PolicyVersionId == b.PolicyVersionId &&
                a.Role == b.Role && a.ComparisonAccount == b.ComparisonAccount && a.Revision <= b.Revision && a.UpdatedUtc <= b.UpdatedUtc &&
                a.Fills.All(f => b.Fills.Contains(f)) && a.Positions.All(h => b.Positions.Any(n => n.PositionId == h.PositionId &&
                    n.Symbol == h.Symbol && n.Quantity == h.Quantity && n.AveragePurchasePrice == h.Cost && n.OpenedUtc == h.OpenedUtc &&
                    n.EntryActionId == h.EntryActionId && (h.ClosedUtc is null || n.ClosedUtc == h.ClosedUtc && n.ExitActionId == h.ExitActionId))));
            if (a.Revision == b.Revision)
                Require(old is not null ? old.After.BookJson == next.After.BookJson : JsonSerializer.Serialize(a) == JsonSerializer.Serialize(Project(b)));
        }
    }

    private static DelphiLiveAiPortfolioRiskMonitor.Source Project(DelphiLivePortfolioSnapshot p) => new(p.GenerationId,
        p.PolicyVersionId, p.Role, p.ComparisonAccount, p.Revision, p.StartingCapital, p.UpdatedUtc, p.Cash,
        p.Positions.OrderBy(h => h.PositionId).Select(h => new DelphiLiveAiPortfolioRiskMonitor.Held(h.PositionId, h.Symbol, h.Quantity,
            h.AveragePurchasePrice, h.OpenedUtc, h.EntryActionId, h.ClosedUtc, h.ExitActionId)).ToImmutableArray(),
        p.Fills.OrderBy(f => f.FillId).ToImmutableArray(), p.Marks.IsDefaultOrEmpty ? null! : p.Marks[^1] with
            { Positions = p.Marks[^1].Positions.OrderBy(m => m.PositionId).ToImmutableArray() });
}
