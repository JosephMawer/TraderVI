#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>One replay of an immutable, idempotent evaluation write; never an order retry.</summary>
internal static class DelphiLiveEvaluationWriteRecovery
{
    internal static bool CanRetry(IReadOnlyList<int> errors) => errors.Count > 0 &&
        errors.Any(n => n is -2 or 1205 or 1222) && errors.All(n => n is -2 or 1205 or 1222 or 3621);

    internal static Task RunAsync(Func<int, CancellationToken, Task> attempt,
        Func<int, Exception, bool, Task> report, CancellationToken token) =>
        RunAsync(attempt, e => e is SqlException sql
            ? sql.Errors.Cast<SqlError>().Select(x => x.Number).ToArray() : [], report, Task.Delay, token);

    internal static async Task RunAsync(Func<int, CancellationToken, Task> attempt,
        Func<Exception, IReadOnlyList<int>> errors, Func<int, Exception, bool, Task> report,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
    {
        for (int number = 1; ; number++)
        {
            token.ThrowIfCancellationRequested();
            try { await attempt(number, token); return; }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                bool retry = number == 1 && !token.IsCancellationRequested && CanRetry(errors(error));
                try { await report(number, error, retry); } catch { /* Diagnosis cannot change recovery. */ }
                if (!retry) throw;
                await delay(TimeSpan.FromMilliseconds(250), token);
            }
        }
    }
}
