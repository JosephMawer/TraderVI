#nullable enable
using Core.Trader.DelphiLive;
using System.Text.Json;

namespace TraderVI.WPF.Viewmodels;

// Retain the original observation. Formatting thousands of complete reports at every
// refresh blocks the dispatcher and duplicates the report strings in memory.
public sealed record DelphiLiveQuoteRow(string Symbol, string Purpose, decimal? Bid, decimal? Ask, decimal? Price,
    string Disposition, string Requested, string Received, DelphiLiveLedgerQuote Quote)
{
    private static readonly JsonSerializerOptions EvidenceOptions = new(DelphiLiveLedgerJson.Options) { WriteIndented = true };
    public string EvidenceJson => JsonSerializer.Serialize(Quote, EvidenceOptions);
}
