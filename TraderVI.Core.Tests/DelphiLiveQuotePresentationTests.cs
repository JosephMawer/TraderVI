using Core.Trader.DelphiLive;
using System;
using System.Linq;
using System.Text.Json;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class DelphiLiveQuotePresentationTests
{
    [Fact]
    public void QuoteHistoryKeepsOriginalObservationsWithoutCopyingReportsForEachDisplayRow()
    {
        var quote = Quote(new string('x', 20_000));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var rows = Enumerable.Range(0, 3_000).Select(_ => Row(quote)).ToArray();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.All(rows, row => Assert.Same(quote, row.Quote));
        // Eager JSON formatting would allocate over 100 MB just for these report strings.
        Assert.True(allocated < 2_000_000, $"Quote rows allocated {allocated:N0} bytes before evidence was opened.");
    }

    [Fact]
    public void OpeningQuoteEvidencePreservesEverySavedFieldAndExactReportText()
    {
        var quote = Quote("{\"message\": \"é / \\\"quoted\\\"\\nline\"}");
        using var expected = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(quote));
        using var actual = JsonDocument.Parse(Row(quote).EvidenceJson);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(quote.DossierJson, actual.RootElement.GetProperty("dossierJson").GetString());
    }

    private static DelphiLiveQuoteRow Row(DelphiLiveLedgerQuote quote) =>
        new("ABC", "Protection", 10m, 10.1m, 10.05m, "Observed", "09:31:00", "09:31:01", quote);

    private static DelphiLiveLedgerQuote Quote(string report)
    {
        var now = new DateTime(2026, 9, 17, 13, 31, 0, DateTimeKind.Utc);
        return new(Guid.NewGuid(), null, null, "Protection", Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), "ABC", 1, now, now.AddSeconds(1), 10.05m, 10m, 10.1m, "Fixture"),
            "Observed", report);
    }
}
