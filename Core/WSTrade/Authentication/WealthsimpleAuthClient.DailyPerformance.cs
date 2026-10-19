#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    public Task<WealthsimpleReadResult<WealthsimpleDailyPerformance>> ReadTfsaDailyPerformanceAsync(
        WealthsimpleTfsaAccount account, CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account is null || catalogIdentity != session.IdentityId || !tfsaAccounts.Contains(account))
            throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "account selection");
        using var json = await QueryAsync("TraderVITfsaDailyPerformance", WealthsimpleAccountQueries.DailyPerformance,
            new { id = account.Id, currency = "CAD", timeRange = "ONE_DAY", marketSession = "REGULAR", includeSimpleReturns = true }, ct);
        var node = Required(json.RootElement, "data", "account");
        RequireId(node, account.Id);
        var graph = Required(node, "financials", "currentCombined", "graphData");
        var retrievedAt = clock.GetUtcNow();
        if (!graph.TryGetProperty("previousClose", out var previous)) throw Invalid();
        DateTimeOffset? previousAt = previous.ValueKind == JsonValueKind.Null ? null : PerformanceTime(previous, "dateTime");
        if (previousAt > retrievedAt) throw Invalid();
        DateTimeOffset? lastAt = previousAt, asOf = null, referenceAt = null;
        DateOnly? day = null;
        WealthsimpleMoney? amount = null;
        decimal? rate = null;
        foreach (var point in RequiredArray(graph, "data"))
        {
            var at = PerformanceTime(point, "dateTime");
            var pointDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, PerformanceToronto).DateTime);
            if (at > retrievedAt || lastAt is { } last && at <= last || day is { } d && pointDay != d) throw Invalid();
            day = pointDay; lastAt = asOf = at;
            // Always replace with the latest point, including when its return is missing.
            amount = null; rate = null; referenceAt = null;
            var returns = Optional(point, "simpleReturns");
            if (returns.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            referenceAt = PerformanceTime(returns, "referenceDateTime");
            if (previousAt == null || referenceAt != previousAt || referenceAt >= at ||
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(referenceAt.Value, PerformanceToronto).DateTime) >= pointDay)
                throw Invalid();
            amount = Money(returns, "amount", cad: true);
            rate = NullableNumber(Optional(returns, "rate"));
        }
        return new WealthsimpleDailyPerformance(account, retrievedAt, asOf, referenceAt, amount, rate);
    }, ct);

    private static TimeZoneInfo PerformanceToronto { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static DateTimeOffset PerformanceTime(JsonElement node, string field)
    {
        var text = RequiredText(node, field);
        bool explicitOffset = text.EndsWith("Z", StringComparison.Ordinal) ||
            text.Length >= 6 && (text[^6] is '+' or '-') && text[^3] == ':';
        if (!explicitOffset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) throw Invalid();
        return at.ToUniversalTime();
    }
}
