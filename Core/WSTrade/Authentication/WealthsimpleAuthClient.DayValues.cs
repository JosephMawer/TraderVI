#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    /// <summary>Explicit read only; usable by either a desktop caller or a future standalone host.</summary>
    public Task<WealthsimpleReadResult<WealthsimpleAccountDayValues>> ReadTfsaDayValuesAsync(
        WealthsimpleTfsaAccount account, CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account is null || catalogIdentity != session.IdentityId || !tfsaAccounts.Contains(account))
            throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "account selection");
        using var json = await QueryAsync("TraderVITfsaDayValues", WealthsimpleAccountQueries.DayValues,
            new { id = account.Id, currency = "CAD", timeRange = "ONE_DAY", marketSession = "REGULAR" }, ct);
        var node = Required(json.RootElement, "data", "account");
        RequireId(node, account.Id);
        var graph = Required(node, "financials", "currentCombined", "graphData");
        var retrievedAt = clock.GetUtcNow();
        if (!graph.TryGetProperty("previousClose", out var previousJson)) throw Invalid();
        var previous = previousJson.ValueKind == JsonValueKind.Null ? null : ReadDayValuePoint(previousJson, false);
        if (previous?.At > retrievedAt) throw Invalid();
        var points = ImmutableArray.CreateBuilder<WealthsimpleAccountValuePoint>();
        DateTimeOffset? lastAt = previous?.At;
        foreach (var pointJson in RequiredArray(graph, "data"))
        {
            var point = ReadDayValuePoint(pointJson, true);
            // Reject duplicate/conflicting/reversed times rather than silently sorting or overwriting.
            if (point.At > retrievedAt || lastAt is { } last && point.At <= last) throw Invalid();
            points.Add(point);
            lastAt = point.At;
        }
        return new WealthsimpleAccountDayValues(account, retrievedAt, previous, points.ToImmutable());
    }, ct);

    private static WealthsimpleAccountValuePoint ReadDayValuePoint(JsonElement point, bool includeDeposits)
    {
        var text = RequiredText(point, "dateTime");
        bool explicitOffset = text.EndsWith("Z", StringComparison.Ordinal) ||
            text.Length >= 6 && (text[^6] is '+' or '-') && text[^3] == ':';
        if (!explicitOffset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at)) throw Invalid();
        var value = Money(point, "netLiquidationValue", cad: true) ?? throw Invalid();
        if (value.Amount < 0m) throw Invalid();
        decimal? deposits = includeDeposits
            ? (Money(point, "netDeposits", cad: true) ?? throw Invalid()).Amount : null;
        return new(at.ToUniversalTime(), value.Amount, deposits);
    }
}
