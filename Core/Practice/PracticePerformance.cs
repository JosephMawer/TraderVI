#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Practice;

public sealed record PracticeAccountPerformance(string Account, decimal StartingCash, decimal EndingCash,
    decimal? EndingValue, decimal? TotalReturn, decimal? MaximumObservedDrawdown, int MissingValuationFrames,
    decimal? MeanObservedExposure, int BuyFills, int SellFills, decimal GrossTurnoverFraction, decimal EstimatedFrictionCost);

internal sealed class PracticePerformance
{
    private readonly Dictionary<string, List<PracticeAccountFrame>> accounts = [];
    public void Observe(PracticeFrame frame)
    {
        if (frame.Poll is null) return;
        foreach (var account in frame.Accounts)
        {
            if (!accounts.TryGetValue(account.Code, out var history)) accounts.Add(account.Code, history = []);
            history.Add(account);
        }
    }
    public IReadOnlyList<PracticeAccountPerformance> Summarize(PracticeShadowStore ledger) => accounts.Select(kv =>
    {
        var last = kv.Value[^1];
        var values = kv.Value.Where(x => x.NetAssetValue.HasValue).ToArray();
        decimal peak = 1000m, drawdown = 0m;
        foreach (var row in values)
        {
            peak = System.Math.Max(peak, row.NetAssetValue!.Value);
            drawdown = System.Math.Min(drawdown, row.NetAssetValue.Value / peak - 1m);
        }
        var fills = ledger.Fills.Where(f => f.PortfolioId == last.PortfolioId).ToArray();
        int missing = kv.Value.Count - values.Length;
        return new PracticeAccountPerformance(kv.Key, 1000m, last.Cash, last.NetAssetValue, last.Return,
            missing == 0 ? -drawdown : null, missing,
            values.Length == 0 ? null : values.Average(x => x.NetAssetValue > 0 ? (x.NetAssetValue.Value - x.Cash) / x.NetAssetValue.Value : 0m),
            fills.Count(f => f.Side == "Buy"), fills.Count(f => f.Side == "Sell"), fills.Sum(f => f.Shares * f.RawPrice) / 1000m,
            fills.Sum(f => f.Shares * System.Math.Abs(f.AdjustedPrice - f.RawPrice)));
    }).ToArray();
}
