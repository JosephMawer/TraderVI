using Core.WSTrade.Authentication;

// An isolated auth tool: does not bootstrap TraderVI, SQL Server, market data, or order routing.
if (args.Length < 2 || args[0] != "--har" || args.Length > 3 ||
    (args.Length == 3 && args[2] is not ("--refresh" or "--interactive")))
{
    Console.WriteLine("Wealthsimple auth: --har <local-capture.har> [--refresh | --interactive]");
    Console.WriteLine("Default: verify the captured bearer session once. No credentials or tokens are printed or saved.");
    Console.WriteLine("--refresh explicitly rotates the captured session; --interactive retains it in memory for auth commands.");
    return 2;
}

try
{
    using var capture = File.OpenRead(args[1]);
    using var client = WealthsimpleAuthClient.FromBrowserHar(capture);
    var result = args.Length == 3 && args[2] == "--refresh"
        ? await client.RefreshAsync()
        : await client.VerifyAsync();
    Print(result);
    if (args.Length != 3 || args[2] != "--interactive")
        return result.Status == WealthsimpleAuthStatus.Authenticated ? 0 : 1;

    Console.WriteLine("Commands: verify, refresh, forget, exit. Session remains in this process only.");
    while (true)
    {
        Console.Write("auth> ");
        switch (Console.ReadLine()?.Trim().ToLowerInvariant())
        {
            case "verify": Print(await client.VerifyAsync()); break;
            case "refresh": Print(await client.RefreshAsync()); break;
            case "forget": Print(await client.ForgetAsync()); break;
            case "exit": case null: return 0;
            default: Console.WriteLine("Use verify, refresh, forget or exit."); break;
        }
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine("Unable to use this local capture. A complete recent Wealthsimple bearer login is required. No capture contents were logged.");
    return 1;
}

static void Print(WealthsimpleAuthResult result)
{
    Console.WriteLine($"Authentication: {result.Status}; HTTP: {result.HttpStatus?.ToString() ?? "none"}.");
    if (result.Status == WealthsimpleAuthStatus.Authenticated)
        Console.WriteLine($"Token verified directly with Wealthsimple; expires UTC {result.ExpiresAt:O}; refresh available: {result.CanRefresh}.");
}
