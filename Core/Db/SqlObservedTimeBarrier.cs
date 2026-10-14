#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Wait for actual store time, never round or relax an observed-time causal guard.</summary>
internal static class SqlObservedTimeBarrier
{
    internal static async Task WaitAsync(SqlConnection connection, DateTime observedUtc, CancellationToken token)
    {
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(token);
        await WaitAsync(observedUtc, async cancellation =>
        {
            await using var command = new SqlCommand("SELECT SYSUTCDATETIME();", connection);
            var time = (DateTime)(await command.ExecuteScalarAsync(cancellation))!;
            return DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }, Task.Delay, token);
    }

    internal static async Task WaitAsync(DateTime observedUtc, Func<CancellationToken, Task<DateTime>> readClock,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
    {
        if (observedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Observed time must be UTC.", nameof(observedUtc));
        for (int attempt = 0; attempt < 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            DateTime storeUtc = await readClock(token);
            if (storeUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Store clock must be UTC.");
            TimeSpan remaining = observedUtc - storeUtc;
            if (remaining <= TimeSpan.Zero) return;
            // Small clock-resolution differences can settle; a large discrepancy
            // is a real operational problem, not permission to accept future data.
            if (remaining > TimeSpan.FromMilliseconds(250)) break;
            await delay(TimeSpan.FromMilliseconds(System.Math.Min(32, System.Math.Max(1, remaining.TotalMilliseconds + 1))), token);
        }
        throw new InvalidOperationException("Store clock has not reached the observed time; causal write remains blocked.");
    }
}
