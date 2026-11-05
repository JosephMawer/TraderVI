#nullable enable
using Core.TMX;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Services;

// One display-only snapshot per dashboard lifetime. No bars, collector subscriptions or SQL writes.
public static class MarketContextReader
{
    private static readonly HttpClient client = CreateClient();
    private static HttpClient CreateClient()
    {
        var result = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        result.DefaultRequestHeaders.Add("Origin", "https://money.tmx.com");
        result.DefaultRequestHeaders.Referrer = new Uri("https://money.tmx.com/");
        result.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return result;
    }
    public static async Task<MarketContextSnapshot> ReadAsync(CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("https://app-money.tmx.com/graphql", new
        {
            operationName = "DashboardMarketSnapshot",
            variables = new { symbols = new[] { "^TSX", "^TX60" }.Concat(TsxSectorSymbols.AllSymbols).ToArray() },
            query = """
                query DashboardMarketSnapshot($symbols:[String]) {
                  quotes:getQuoteForSymbols(symbols:$symbols) { symbol percentChange }
                  breadth:getMarketSummary(market:"caMarket") { exchange advancers decliners unchanged }
                }
                """
        }, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return MarketContextSnapshot.Parse(json, DateTimeOffset.UtcNow);
    }
}
