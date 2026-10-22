#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Explicit local operational controls. Receipts remain in the caller's private output file.</summary>
public static class EngineControlCommand
{
    public static Task RunAsync(string operation, string output, string? input = null) =>
        RunAsync(operation, output, input, new EngineRpcClient(EnginePaths.PipeName));

    internal static async Task RunAsync(string operation, string output, string? input, IEngineRpc client)
    {
        if (!Enum.TryParse<EngineOperation>(operation, out var selected) || selected is not
            (EngineOperation.Status or EngineOperation.Stop or EngineOperation.IntegrityStatus or
             EngineOperation.VerifyRecovery or EngineOperation.VerifyRealRecovery or EngineOperation.VerifyPaperRecovery or EngineOperation.ClearIntegrityHolds))
            throw new ArgumentException("Unsupported maintenance control.");
        if ((selected == EngineOperation.ClearIntegrityHolds) != (input is not null))
            throw new ArgumentException("Only explicit hold resolution requires an input file.");
        // Reserve the receipt before admitting work. An existing output cannot trigger another command.
        await using var receipt = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        object? payload = input is null ? null : (JsonSerializer.Deserialize<EngineClearIntegrityHolds>(await File.ReadAllTextAsync(input))
            ?? throw new InvalidDataException("Missing explicit recovery resolution."));
        var result = await client.CallAsync<JsonElement>(selected, payload);
        await JsonSerializer.SerializeAsync(receipt, result);
        await receipt.FlushAsync(); receipt.Flush(flushToDisk: true);
    }
}
