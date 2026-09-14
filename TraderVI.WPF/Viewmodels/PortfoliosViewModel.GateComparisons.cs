#nullable enable
using Core.Db;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class PortfoliosViewModel
{
    private async Task DescribeGateComparisonsAsync(List<PortfolioOverviewRow> rows, CancellationToken ct)
    {
        var repository = new PaperComparisonRepository();
        var accounts = (await repository.ReadAccountsAsync(ct)).ToDictionary(a => a.AccountId);
        var sources = new Dictionary<Guid, PaperComparisonSource?>();
        DateTime now = DateTime.UtcNow, date = PaperTradingMonitor.ToToronto(now).Date;
        foreach (var batch in accounts.Values.Select(a => a.BatchId).Distinct())
            sources[batch] = await repository.ReadSourceAsync(batch, date, ct);
        for (int i = 0; i < rows.Count; i++)
        {
            Guid? id = rows[i].SystemPortfolioId ?? rows[i].DelphiLivePortfolioId;
            if (!id.HasValue || !accounts.TryGetValue(id.Value, out var account)) continue;
            sources.TryGetValue(account.BatchId, out var source);
            string explanation = $"Fixed {account.ProfileName} profile. Matched pair {account.PairId.ToString("N")[..8]}; " +
                $"independent CAD 1,000 starting cash. Start: {PaperTradingMonitor.ToToronto(account.EffectiveUtc):MMM d HH:mm} Toronto. ";
            if (source is null)
                explanation += "Waiting for a valid comparison source under the pair's assigned daily strategy. Holdings remain protected. ";
            else
            {
                int considered, admitted;
                if (account.StrategyCode == "DelphiLive")
                {
                    considered = source.Candidates.Count;
                    admitted = PaperGateComparison.SelectLive(source, account.Profile).Count;
                }
                else
                {
                    var definition = SystemShadowPortfolioDefinition.Version1.Single(d => d.Code == account.StrategyCode);
                    var lenses = source.Candidates.SelectMany(c => c.SourceLenses).Where(l => l.Lens == definition.Lens && l.Published && l.Rank >= 1 && l.Rank <= definition.MaximumPositions).ToArray();
                    considered = lenses.Length;
                    admitted = lenses.Count(l => PaperGateComparison.Admits(account.Profile, l.Published, l.Rank, l.Eligible, definition.MaximumPositions));
                }
                explanation += $"{admitted} of {considered} published candidates admitted. " +
                    (account.Profile == PaperGateProfile.NormalV1 ? $"Daily gates block {considered - admitted}. " : "Daily gates bypassed; original failures retained. ") +
                    $"Source {source.RunId.ToString("N")[..8]}, published {PaperTradingMonitor.ToToronto(source.PublishedUtc):HH:mm}; " +
                    $"fresh evidence from {PaperTradingMonitor.ToToronto(source.EffectiveUtc):HH:mm} Toronto. " +
                    (PaperTradingMonitor.ToToronto(source.EffectiveUtc).TimeOfDay > TimeSpan.FromHours(9.5)
                        ? "Partial session; missing opening observations remain missing. " : "");
            }
            explanation += "Purchases still require the portfolio's entry conditions, usable data, available cash and risk limits. " +
                "Gate settings cannot be changed on this account.";
            rows[i] = rows[i] with
            {
                StableCode = rows[i].IsDelphiLive ? rows[i].StableCode : $"Shadow:{account.AccountId:N}",
                DisplayName = account.DisplayName, ComparisonPairId = account.PairId, GateProfile = account.Profile, GateStrategyName = account.StrategyCode,
                StrategyName = $"{account.StrategyCode} · {account.ProfileName} (fixed)", Explanation = explanation,
                Status = now < account.EffectiveUtc ? "Queued" : rows[i].Status
            };
        }
    }
}
