using Core.Practice;
using TraderVI.Practice;

if (!(args.Length == 2 && args[0] == "capture-current-universe") &&
    !(args.Length == 3 && args[0] is "pack" or "run") &&
    !(args.Length == 4 && args[0] is "export-daily" or "acquire-intraday") &&
    !(args.Length == 5 && args[0] == "acquire-selected"))
{
    Console.WriteLine("TraderVI Practice / Exploratory — local files only");
    Console.WriteLine("  pack <input.json> <new-package-directory>");
    Console.WriteLine("  run  <package-directory> <new-run-directory>");
    Console.WriteLine("Explicit acquisition (never called by run):");
    Console.WriteLine("  capture-current-universe <new-membership.json>  [SQL reads; explicit exploratory assumption]");
    Console.WriteLine("  export-daily <dated-membership.json> <calendar.json> <new-input.json>  [SQL reads]");
    Console.WriteLine("  acquire-intraday <input.json> <new-input.json> <logical-request-budget>  [TMX calls]");
    Console.WriteLine("  acquire-selected <package> <completed-run> <new-input.json> <logical-request-budget>  [TMX calls]");
    Console.WriteLine("See Docs/concepts/historical-practice-implementation.md for input and acquisition contracts.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args[0] == "capture-current-universe")
    {
        await Preparation.CaptureCurrentUniverseAsync(args[1], cancellation.Token);
    }
    else if (args[0] == "export-daily")
    {
        await Preparation.ExportDailyAsync(args[1], args[2], args[3], cancellation.Token);
        Console.WriteLine("Stored daily inputs exported. No intraday market requests or replay ran.");
    }
    else if (args[0] == "acquire-selected")
    {
        await Preparation.AcquireSelectedAsync(args[1], args[2], args[3], int.Parse(args[4]), cancellation.Token);
        Console.WriteLine("Selected-candidate acquisition saved; the full daily universe is unchanged.");
    }
    else if (args[0] == "acquire-intraday")
    {
        await Preparation.AcquireIntradayAsync(args[1], args[2], int.Parse(args[3]), cancellation.Token);
        Console.WriteLine("Bounded TMX acquisition finished. Saved observations and acquisition report; gaps remain.");
    }
    else if (args[0] == "pack")
    {
        PracticeDataset.Pack(args[1], args[2]);
        Console.WriteLine("Practice package prepared and verified. No replay was started.");
    }
    else
    {
        var dataset = PracticeDataset.Load(args[1]);
        Console.WriteLine($"Practice / Exploratory: {dataset.Sessions.Count} sessions; four independent CAD 1,000 accounts.");
        var session = new PracticeSession(dataset);
        await session.RunToDirectoryAsync(args[2], cancellation.Token);
        Console.WriteLine($"Completed {session.DailyResults.Count} sessions. Saved playback and diagnostic files.");
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Practice cancelled. Retained completed traces; a new run requires a new output directory.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Practice failed ({ex.GetType().Name}): {ex.Message}");
    return 1;
}
