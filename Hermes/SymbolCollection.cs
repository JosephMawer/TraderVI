using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes;

/// <summary>Only an individual source read may degrade; storage and processing failures still escape.</summary>
internal static class SymbolCollection
{
    public static async Task<bool> CollectAsync<T>(string kind, string symbol, Func<Task<T>> read,
        Func<T, bool> usable, Func<T, Task> accept, Action<string> report,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        T value;
        try { value = await read(); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            report($"HermesSymbolUnavailable: {kind} {symbol}: {error.Message}");
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!usable(value))
        {
            report($"HermesSymbolUnavailable: {kind} {symbol}: source returned no usable data.");
            return false;
        }
        // Never label a database/write/processing failure as an unavailable ticker.
        await accept(value);
        return true;
    }
}
