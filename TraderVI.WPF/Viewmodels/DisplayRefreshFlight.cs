#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

/// <summary>UI-owned display requests join the current read instead of building a refresh queue.</summary>
public sealed class DisplayRefreshFlight
{
    private Task? running;
    public Task RunAsync(Func<Task> readAndApply, CancellationToken ct)
    {
        if (running is { IsCompleted: false }) return running.WaitAsync(ct);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        running = completion.Task;
        _ = CompleteAsync(readAndApply, completion);
        return completion.Task;
    }

    private static async Task CompleteAsync(Func<Task> action, TaskCompletionSource completion)
    {
        try { await action(); completion.TrySetResult(); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
