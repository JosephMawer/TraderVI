using System.Data;
using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using Microsoft.Data.SqlClient;

namespace Tools.Backfill.MissingXiu;

/// <summary>
/// Probe + (optional) backfill for the 5 XIU dual-class / unit symbols that
/// were never added to dbo.Symbols and therefore have no DailyBars history.
///
/// Phase 1 (default): probe TMX for several candidate ticker formats and
/// report which one (if any) returns data. No DB writes.
///
/// Phase 2 (--commit): for each probed symbol whose TMX format was confirmed,
/// upsert into dbo.Symbols (SecurityType='Stock', IsActive=1) and call
/// QuoteRepository.InsertDailyBarsAsync over the full backfill window so the
/// Weighting calibration tool can include them on the next run.
///
/// The probed formats list is intentionally short — we only try plausible
/// variants of how TMX exposes Canadian dual-class shares.
/// </summary>
internal static class Program
{
    private const string BackfillStart = "2020-01-01";
    private const string ProbeStart = "2025-01-01"; // small window for the probe

    // Display ticker (canonical, matches Xiu60Constituents) → ordered list of
    // candidate formats to try against the TMX GraphQL API.
    private static readonly Dictionary<string, string[]> Candidates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Teck Resources Class B
        ["TECK.B"]  = new[] { "TECK.B", "TECK-B", "TECK.B.TO", "TECK/B", "TECKB" },
        // Bombardier Class B
        ["BBD.B"]   = new[] { "BBD.B", "BBD-B", "BBD.B.TO", "BBD/B", "BBDB" },
        // Rogers Communications Class B
        ["RCI.B"]   = new[] { "RCI.B", "RCI-B", "RCI.B.TO", "RCI/B", "RCIB" },
        // CGI Class A subordinate voting
        ["GIB.A"]   = new[] { "GIB.A", "GIB-A", "GIB.A.TO", "GIB/A", "GIBA" },
        // RioCan REIT trust units
        ["REI.UN"]  = new[] { "REI.UN", "REI-UN", "REI.UN.TO", "REI.U", "REIUN", "REIT" },
    };

    private static async Task<int> Main(string[] args)
    {
        bool commit = args.Any(a => a.Equals("--commit", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"=== Backfill.MissingXiu  (mode: {(commit ? "COMMIT" : "PROBE-ONLY")}) ===\n");

        var tmx = new TmxClient();
        var quotes = new QuoteRepository();
        var endProbe = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // canonical → tmx symbol

        foreach (var (canonical, formats) in Candidates)
        {
            Console.WriteLine($"── {canonical} ──");
            string? winner = null;
            int winnerCount = 0;

            foreach (var fmt in formats)
            {
                try
                {
                    var bars = await tmx.GetHistoricalTimeSeriesAsync(fmt, "day", ProbeStart, endProbe);
                    Console.WriteLine($"  try {fmt,-12}  → {bars.Count,5} bars");
                    if (bars.Count > 0 && winner is null)
                    {
                        winner = fmt;
                        winnerCount = bars.Count;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  try {fmt,-12}  → ERROR: {Truncate(ex.Message, 80)}");
                }

                await Task.Delay(400); // be polite to TMX
            }

            if (winner is null)
            {
                Console.WriteLine($"  ✗ NO format returned data for {canonical}\n");
            }
            else
            {
                Console.WriteLine($"  ✓ resolved {canonical} → \"{winner}\" ({winnerCount} probe bars)\n");
                resolved[canonical] = winner;
            }
        }

        Console.WriteLine($"\nProbe summary: resolved {resolved.Count}/{Candidates.Count}");
        foreach (var (canonical, fmt) in resolved)
            Console.WriteLine($"  {canonical,-8} → {fmt}");

        if (!commit)
        {
            Console.WriteLine("\nProbe-only run complete. Re-run with --commit to insert into dbo.Symbols + dbo.DailyBars.");
            return resolved.Count == Candidates.Count ? 0 : 2;
        }

        if (resolved.Count == 0)
        {
            Console.Error.WriteLine("\nNothing resolved; aborting commit.");
            return 3;
        }

        // ── Phase 2: insert into Symbols and backfill DailyBars ─────────────
        Console.WriteLine($"\n=== Committing backfill ({BackfillStart} → {endProbe}) ===\n");

        foreach (var (canonical, tmxFmt) in resolved)
        {
            try
            {
                Console.Write($"{canonical,-8} ({tmxFmt,-12}) ");
                var bars = await tmx.GetHistoricalTimeSeriesAsync(tmxFmt, "day", BackfillStart, endProbe);
                if (bars.Count == 0)
                {
                    Console.WriteLine("⚠️  zero bars on full window; skipping.");
                    continue;
                }

                // Upsert symbol row using canonical (dot-form) symbol so downstream
                // code (Xiu60Constituents, calibration, Granville indicators) works
                // without a translation layer.
                await UpsertSymbolAsync(canonical);

                await quotes.InsertDailyBarsAsync(canonical, bars);
                Console.WriteLine($"✓ {bars.Count,4} bars inserted as \"{canonical}\".");

                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {ex.Message}");
            }
        }

        Console.WriteLine("\n=== Done ===");
        return 0;
    }

    /// <summary>
    /// Inserts a row into dbo.Symbols if not present. Uses canonical (display)
    /// symbol as the storage key so the rest of the system doesn't need a
    /// translation layer — only the TMX request-side needs the resolved format,
    /// and that mapping is currently embedded here.
    /// </summary>
    private static async Task UpsertSymbolAsync(string canonical)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Symbols WHERE Symbol = @sym)
    INSERT INTO dbo.Symbols (Symbol, LongName, SecurityType, IsActive, CreatedUtc)
    VALUES (@sym, @sym, 'Stock', 1, SYSUTCDATETIME());
ELSE
    UPDATE dbo.Symbols
       SET IsActive = 1,
           SecurityType = COALESCE(NULLIF(SecurityType, ''), 'Stock')
     WHERE Symbol = @sym;
";

        await using var cn = new SqlConnection(SQLBase.Database);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add(new SqlParameter("@sym", SqlDbType.NVarChar, 20) { Value = canonical });
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "…";
}
