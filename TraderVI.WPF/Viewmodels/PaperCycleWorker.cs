#nullable enable
using Core.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Invoke the entire operation on a worker, including work before its first await.</summary>
public static class PaperCycleWorker
{
    public static Task<T> RunAsync<T>(PaperCycleStage stage, Func<Task<T>> operation, CancellationToken ct) =>
        Task.Run(async () =>
        {
            using var timing = PaperCycleTimings.Start(stage);
            return await operation().ConfigureAwait(false);
        }, ct);
}
