#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace TraderVI.WPF.Viewmodels;

public static class DelphiLiveWatchlistScope
{
    public static IReadOnlyList<DelphiLiveObservationRow> ForAccount(IEnumerable<DelphiLiveObservationRow> observations, Guid? account) =>
        account is null ? [] : observations.Where(r => r.PortfolioId == account).OrderBy(r => r.Rank)
            .ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed record DelphiLiveObservationRow(string SelectionKey, Guid PolicyVersionId, int Rank, string Symbol,
    string DailySource, decimal? DailyComposite, string Momentum, string Confidence, string Lifecycle, string Activity,
    string Persistence, string PriceMovement, string VolumeSupport, string PriceStructure, int? PersistenceScore,
    string Checkpoint, bool IsHeld, bool IsExitPending, string EvidenceJson, string Reason)
{
    public string Role { get; init; } = "";
    public Guid? PortfolioId { get; init; }
    public decimal? Price { get; init; }
    public decimal? ChangeFromPreviousClose { get; init; }
}
