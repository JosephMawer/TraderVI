using Core.DataQuality;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Any(a => a is "--help" or "-h"))
{
    PrintHelp();
    return 0;
}

try
{
    MarketDataAuditOptions options = ParseOptions(args);
    string? configuredConnection = Environment.GetEnvironmentVariable("TRADERVI_CONNECTION_STRING");

    Console.WriteLine("=== TraderVI Full Local Data Audit ===\n");
    Console.WriteLine("Mode: read-only local SQL; no external market calls; no database writes");
    Console.WriteLine($"Freshness: warning at {options.StaleWarningSessions} sessions, error at {options.StaleErrorSessions} sessions");
    Console.WriteLine($"Sector mappings: warning after {options.SectorMappingMaxAgeDays} days\n");

    var workflow = new MarketDataAuditWorkflow(configuredConnection);
    MarketDataAuditRunResult result = await workflow.RunAsync(options);
    MarketDataAuditReport report = result.Report;

    Console.WriteLine($"Market data as of: {report.MarketDataAsOf:yyyy-MM-dd}");
    Console.WriteLine($"Symbols audited:   {report.TotalSymbols:N0}");
    Console.WriteLine($"Active symbols:    {report.ActiveSymbols:N0} ({report.ActiveStocks:N0} stocks, {report.ActiveEtfs:N0} ETFs)");
    Console.WriteLine($"Findings:          {report.ErrorCount:N0} errors, {report.WarningCount:N0} warnings\n");

    foreach (var group in report.Findings
        .GroupBy(f => new { f.Severity, f.Code })
        .OrderByDescending(g => g.Key.Severity)
        .ThenBy(g => g.Key.Code, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {group.Key.Severity.ToString().ToUpperInvariant(),-7} {group.Count(),4}  {group.Key.Code}");
    }

    if (report.Findings.Count > 0)
        Console.WriteLine();

    foreach (AuditFinding finding in report.Findings)
    {
        string symbol = finding.Symbol is null ? "" : $" [{finding.Symbol}]";
        Console.WriteLine($"{finding.Severity.ToString().ToUpperInvariant(),-7} {finding.Code}{symbol}");
        Console.WriteLine($"        {finding.Message}");
    }

    if (report.Findings.Count == 0)
        Console.WriteLine("No findings. The local universe passed all automated checks.");

    Console.WriteLine("\nClassification/listing findings are review candidates only.");
    Console.WriteLine("Verify them against official issuer or exchange sources before changing TraderDB.");

    return report.ErrorCount > 0 ? 2 : report.WarningCount > 0 ? 1 : 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Argument error: {ex.Message}\n");
    PrintHelp();
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Audit failed: {ex.Message}");
    return 3;
}

static MarketDataAuditOptions ParseOptions(string[] arguments)
{
    int warningSessions = 2;
    int errorSessions = 5;
    int mappingAgeDays = 14;

    for (int i = 0; i < arguments.Length; i++)
    {
        string argument = arguments[i];
        if (i + 1 >= arguments.Length)
            throw new ArgumentException($"Missing value after {argument}.");

        if (!int.TryParse(arguments[++i], out int value))
            throw new ArgumentException($"Value for {argument} must be an integer.");

        switch (argument)
        {
            case "--warning-sessions":
                warningSessions = value;
                break;
            case "--error-sessions":
                errorSessions = value;
                break;
            case "--mapping-age-days":
                mappingAgeDays = value;
                break;
            default:
                throw new ArgumentException($"Unknown option: {argument}.");
        }
    }

    var options = new MarketDataAuditOptions(warningSessions, errorSessions, mappingAgeDays);
    options.Validate();
    return options;
}

static void PrintHelp()
{
    Console.WriteLine("Usage: dotnet run --project DataAudit -- [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --warning-sessions N   Warn when an active symbol is N XIU sessions behind (default 2)");
    Console.WriteLine("  --error-sessions N     Error when an active symbol is N XIU sessions behind (default 5)");
    Console.WriteLine("  --mapping-age-days N   Warn when a stock-sector mapping is older than N days (default 14)");
    Console.WriteLine("  --help, -h             Show help");
    Console.WriteLine();
    Console.WriteLine("Set TRADERVI_CONNECTION_STRING to override the default local TraderDB connection.");
}
