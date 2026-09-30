#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Explicitly authorized read-only quote timing check: at most 16 paper-held symbols, one security
/// lookup and two existing sell-quote reads each, separated by 60 seconds. Five-minute run, 15-second
/// requests; at most 49 logical broker reads including authentication, each with at most one existing
/// DPoP nonce retry. No orders, SQL writes, account balance reads, token refresh or session persistence.
/// Anonymous output measures coverage only; a successful run never authorizes quote-based valuation.
/// </summary>
public sealed class WealthsimpleQuoteTimingProbe : IProbe
{
    public string Slug => "wealthsimple-quote-timing";
    public string Description => "Read-only held-stock broker quote timing; uses saved login without refresh.";

    public async Task RunAsync()
    {
        try { await RunCheckedAsync(); }
        catch (Exception ex)
        { throw new InvalidOperationException($"Broker quote diagnostic stopped: {ex.GetType().Name}. Private details omitted."); }
    }

    private static async Task RunCheckedAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var run = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraderVI", "Wealthsimple", "session.dat");
        byte[] saved = await File.ReadAllBytesAsync(sessionPath, run.Token);
        using var client = WealthsimpleAuthClient.FromProtectedSession(saved, new ReadOnlyProtector());
        using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        authTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        var auth = await client.VerifyAsync(authTimeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { CheckedUtc = DateTime.UtcNow, Authentication = auth.Status.ToString() }));
        if (auth.Status != WealthsimpleAuthStatus.Authenticated)
            throw new InvalidOperationException("Current operator login required; no refresh attempted.");

        var calendar = ReviewedTsxSessionCalendar.Load(Environment.GetEnvironmentVariable("TRADERVI_TSX_CALENDAR_PATH")
            ?? throw new InvalidOperationException("Reviewed calendar required."));
        DateTime began = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(began, ReviewedTsxSessionCalendar.Toronto));
        var bounds = calendar.GetSessionBounds(date);
        if (began < bounds.OpenUtc || began.AddMinutes(5) >= bounds.CloseUtc)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Block = "OutsideRegularSession", bounds.OpenUtc, bounds.CloseUtc,
                QuoteReads = 0 }));
            throw new InvalidOperationException("Regular-session evidence required.");
        }
        var held = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        var targets = held.Order(StringComparer.Ordinal).ToArray();
        if (targets.Length is < 1 or > 16) throw new InvalidOperationException("Target count outside bounds.");
        Console.WriteLine(JsonSerializer.Serialize(new { StartedUtc = began, Targets = targets.Length,
            MaximumLogicalBrokerReads = 1 + targets.Length * 3, MaximumHttpAttempts = 2 + targets.Length * 6 }));
        var securities = new WealthsimpleCanadianSecurity?[targets.Length];
        int lookups = 0, quoteReads = 0;
        var first = new Dictionary<int, ManualBuyQuote>();
        for (int poll = 1; poll <= 2; poll++)
        {
            if (poll == 2) await Task.Delay(TimeSpan.FromSeconds(60), run.Token);
            int withinAge = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!await SessionUnchangedAsync(saved, sessionPath, run.Token))
                    throw new InvalidOperationException("Saved login changed; diagnostic invalidated.");
                if (DateTime.UtcNow >= bounds.CloseUtc) throw new InvalidOperationException("Session ended.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                if (poll == 1)
                {
                    lookups++;
                    var found = await client.FindCanadianSecuritiesAsync(targets[i], timeout.Token);
                    if (found.Status != WealthsimpleReadStatus.Success) throw new InvalidOperationException("Security read failed.");
                    if (found.Data?.Count == 1) securities[i] = found.Data[0];
                }
                if (securities[i] is not { } security)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll, Target = $"T{i + 1:00}", Block = "NoUniqueSupportedSecurity" }));
                    continue;
                }
                quoteReads++;
                var fetched = await client.ReadManualSellQuoteAsync(security, timeout.Token);
                if (fetched.Status is WealthsimpleReadStatus.NotAuthenticated or WealthsimpleReadStatus.Forbidden or WealthsimpleReadStatus.RateLimited)
                    throw new InvalidOperationException("Broker access unavailable; no retry or refresh.");
                var q = fetched.Data;
                bool recent = IsRecentRegularQuote(q);
                if (recent) withinAge++;
                first.TryGetValue(i, out var previous);
                Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll, Target = $"T{i + 1:00}", ReadStatus = fetched.Status.ToString(),
                    q?.QuotedAt, q?.RetrievedAt, q?.MarketStatus,
                    AgeSeconds = q is null ? (double?)null : (q.RetrievedAt - q.QuotedAt).TotalSeconds,
                    PositiveUncrossedBidAsk = q is not null && q.Bid > 0 && q.Ask >= q.Bid,
                    WithinDiagnostic120SecondsAndRegularSession = recent,
                    TimeAdvanced = previous is null || q is null ? (bool?)null : q.QuotedAt > previous.QuotedAt,
                    PricesChanged = previous is null || q is null ? (bool?)null : q.Bid != previous.Bid || q.Ask != previous.Ask }));
                if (poll == 1 && q is not null) first[i] = q;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Poll = poll, RecentRegularQuotes = withinAge, Targets = targets.Length }));
        }
        var after = await TmxHeldPriceFreshnessProbe.ReadHeldSymbolsAsync(run.Token);
        if (!await SessionUnchangedAsync(saved, sessionPath, run.Token))
            throw new InvalidOperationException("Saved login changed; diagnostic invalidated.");
        Console.WriteLine(JsonSerializer.Serialize(new { CompletedUtc = DateTime.UtcNow, SecurityLookups = lookups, QuoteReads = quoteReads,
            TargetSetUnchanged = held.SetEquals(after), SavedSessionUnchanged = true, OrdersSubmitted = 0 }));
        Console.WriteLine("Diagnostic only. Existing sell eligibility applies. Source timestamp semantics and continuous coverage remain unqualified; 120 seconds is not an operating policy.");
    }

    internal static bool IsRecentRegularQuote(ManualBuyQuote? q) => q is not null && q.Bid > 0 && q.Ask >= q.Bid &&
        q.MarketStatus == "OPEN" && q.SessionOpen < q.SessionClose && q.QuotedAt >= q.SessionOpen &&
        q.QuotedAt <= q.RetrievedAt && q.RetrievedAt < q.SessionClose &&
        q.RetrievedAt - q.QuotedAt <= TimeSpan.FromSeconds(120);

    private static async Task<bool> SessionUnchangedAsync(byte[] saved, string path, CancellationToken ct)
    {
        var current = await File.ReadAllBytesAsync(path, ct);
        return CryptographicOperations.FixedTimeEquals(saved, current);
    }

    [SupportedOSPlatform("windows")]
    private sealed class ReadOnlyProtector : IWealthsimpleSessionProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new InvalidOperationException("Diagnostic cannot save credentials.");
        public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext,
            Encoding.UTF8.GetBytes("TraderVI.Wealthsimple.Session.v1"), DataProtectionScope.CurrentUser);
    }
}
