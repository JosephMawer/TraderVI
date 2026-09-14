#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>> FindCanadianSecuritiesAsync(
        string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32 || symbol.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
            throw new ArgumentException("An exact Canadian stock symbol is required.", nameof(symbol));
        return ReadLockedAsync<IReadOnlyList<WealthsimpleCanadianSecurity>>(async () =>
        {
            using var json = await QueryAsync("TraderVICanadianSecurity", SecuritySearchQuery, new { query = symbol }, ct);
            var matches = new List<WealthsimpleCanadianSecurity>(); var ids = new HashSet<string>();
            foreach (var security in RequiredArray(Required(json.RootElement, "data", "securitySearch"), "results"))
            {
                var stock = Optional(security, "stock"); var mic = StringValue(stock, "primaryMic");
                if (StringValue(security, "currency") != "CAD" || StringValue(security, "securityType") != "EQUITY" ||
                    mic is not ("XTSE" or "XTSX") || !MatchesCanadianSymbol(StringValue(stock, "symbol"), symbol, mic)) continue;
                var id = RequiredText(security, "id"); if (!ids.Add(id)) throw Invalid();
                matches.Add(new(id, RequiredText(stock, "symbol"), mic) { SourceClient = this, SourceIdentity = session.IdentityId });
            }
            return matches.AsReadOnly();
        }, ct);
    }
    private static bool MatchesCanadianSymbol(string? actual, string requested, string mic)
    {
        if (actual == null) return false;
        string suffix = mic == "XTSE" ? ".TO" : ".V";
        string Normalize(string value) => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value[..^suffix.Length] : value;
        return string.Equals(Normalize(actual), Normalize(requested), StringComparison.OrdinalIgnoreCase);
    }
    private const string SecuritySearchQuery = """
        query TraderVICanadianSecurity($query: String!) {
          securitySearch(input: {query: $query}) {
            results { id currency securityType stock { symbol primaryMic } }
          }
        }
        """;
}
