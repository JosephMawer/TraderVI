#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Allow SQL's clock to catch up to an unchanged receipt, never accept a future timestamp.</summary>
internal static class CollectorReceiptClockRetry
{
    internal static bool CanRetry(IReadOnlyList<int> errors) => errors.Contains(54561) &&
        errors.All(number => number is 54561 or 3621);

    internal static Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> append, CancellationToken ct) =>
        RunAsync(append, error => error is SqlException sql
            ? sql.Errors.Cast<SqlError>().Select(e => e.Number).ToArray() : [], Task.Delay, ct);

    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> append,
        Func<Exception, IReadOnlyList<int>> errors, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await append(ct); }
            catch (Exception error) when (error is not OperationCanceledException && attempt < 2 &&
                !ct.IsCancellationRequested && CanRetry(errors(error)))
            {
                // AppendOnceAsync has disposed/rolled back its transaction before this delay.
                // No refetch, timestamp change, guard tolerance, or retry of uncertain SQL outcomes.
                await delay(TimeSpan.FromMilliseconds(25), ct);
            }
        }
    }
}
