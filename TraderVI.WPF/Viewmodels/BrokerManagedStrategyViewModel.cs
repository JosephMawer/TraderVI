#nullable enable
using Core.Trader.Promotion;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Restores saved controls after a verified linked-account read. Never starts execution.</summary>
public sealed class BrokerManagedStrategyViewModel(IBrokerManagedSessionControls controls, TimeProvider? time = null)
    : INotifyPropertyChanged, IDisposable
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private CancellationTokenSource? operation;
    private BrokerManagedSession? saved;
    private Guid? binding;
    private long generation;
    private bool verified, disposed;
    private DateTimeOffset checkedAt;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string StrategyName => saved?.Strategy.Strategy.Name ?? "No approved strategy loaded";
    public string BuyingMode => saved is null ? "Refresh the linked account to restore its saved selection."
        : saved.NewBuysStopped ? "New buys stopped · existing exit rules retained" : "Buying allowed after account and risk checks";
    public string Duration => saved is null ? "" : saved.IsOngoing ? "Ongoing strategy selection" : "One trading session only";
    public string Status { get; private set; } = "Account verification and activation review are required. See the separate execution host status.";
    public bool IsBusy => operation is not null;
    private bool CanChange => !disposed && !IsBusy && verified && saved is not null && binding == saved.BindingId &&
        clock.GetUtcNow() >= checkedAt && clock.GetUtcNow() - checkedAt <= TimeSpan.FromMinutes(5);
    public bool CanStopBuying => CanChange && saved?.NewBuysStopped == false;
    public bool CanResumeBuying => CanChange && saved?.NewBuysStopped == true;
    // Read-only presentation context. This property cannot approve an assignment or start execution.
    public BrokerManagedSession? VerifiedSession => CanChange ? saved : null;

    public void Invalidate(bool clearSelection = false)
    {
        generation++; verified = false; operation?.Cancel();
        if (clearSelection) { saved = null; binding = null; }
        Status = "Refresh the linked account before changing its saved buying mode. See the separate execution host status.";
        Notify();
    }
    public async Task RestoreAsync(Guid bindingId, CancellationToken ct = default)
    {
        if (disposed) return;
        Invalidate(binding != bindingId); binding = bindingId;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operation = request; long expected = generation; Notify();
        try
        {
            var result = await controls.ReadAsync(bindingId, request.Token);
            if (!Current(expected) || request.IsCancellationRequested) return;
            if (result is not null && result.BindingId != bindingId) throw new InvalidOperationException("Saved assignment belongs to another account.");
            saved = result; verified = true; checkedAt = clock.GetUtcNow();
            Status = result is null ? "No strategy has been approved to manage this account."
                : "Saved strategy and buying mode restored. See the separate execution host status; account and risk checks still apply.";
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (Current(expected))
                Status = "Saved strategy could not be verified. Its saved mode is unchanged; refresh to retry.";
        }
        finally { if (ReferenceEquals(operation, request)) operation = null; Notify(); }
    }
    public Task StopBuyingAsync() => ChangeAsync(true);
    public Task ResumeBuyingAsync() => ChangeAsync(false);
    private async Task ChangeAsync(bool stopped)
    {
        if (!(stopped ? CanStopBuying : CanResumeBuying) || saved is not { } selected) return;
        using var request = new CancellationTokenSource(); operation = request;
        long expected = generation; Notify();
        try
        {
            var result = await controls.SetBuyingStoppedAsync(selected.BindingId, selected.SessionId, stopped, request.Token);
            if (!Current(expected) || request.IsCancellationRequested) return;
            if (result.BindingId != selected.BindingId || result.SessionId != selected.SessionId || result.NewBuysStopped != stopped)
                throw new InvalidOperationException("The saved buying control could not be confirmed.");
            saved = result;
            Status = stopped ? "Stopped new buys. Existing exit rules and outstanding orders are unchanged. See the separate execution host status."
                : "Buying mode saved. Account and risk checks still apply. See the separate execution host status.";
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (Current(expected))
            {
                verified = false;
                Status = "The save could not be confirmed. Refresh to recover the saved mode before trying again.";
            }
        }
        finally { if (ReferenceEquals(operation, request)) operation = null; Notify(); }
    }
    private bool Current(long expected) => !disposed && generation == expected;
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Invalidate(true);
    }
}
