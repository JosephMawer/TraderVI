#nullable enable
using Core.WSTrade.Authentication;
using Core.WSTrade.Accounts;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public interface IWealthsimpleSessionStore : IDisposable
{
    void Open();
    WealthsimpleAuthClient? Load();
    Task SaveAsync(WealthsimpleAuthClient client, CancellationToken ct);
    void Clear();
}

/// <summary>Owns the desktop session independently of SQL, strategies and order execution.
/// Call from the UI context. All asynchronous state changes are serialized.</summary>
public sealed partial class WealthsimpleConnection(IWealthsimpleSessionStore store, TimeProvider? time = null,
    Func<WealthsimpleAuthClient>? createClient = null) : INotifyPropertyChanged, IWealthsimpleAccountSource
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly Func<WealthsimpleAuthClient> createClient = createClient ?? WealthsimpleAuthClient.CreateForDesktop;
    private readonly SemaphoreSlim gate = new(1, 1);
    private WealthsimpleAuthClient? client;
    private DateTimeOffset? expires, verified;
    private bool canRefresh, started, stopped, storageReady;
    private DateTimeOffset? otpDeadline;
    private string state = "Disconnected", detail = "Sign in to connect your Wealthsimple account.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool RememberSession { get; private set; }
    public bool IsBusy { get; private set; }
    public bool AwaitingOtp { get; private set; }
    public bool BrowserVerificationRequired { get; private set; }
    public bool HasSession => client != null && verified.HasValue && !stopped;
    public long SessionRevision { get; private set; }
    public bool IsConnected => state == "Connected" && expires > clock.GetUtcNow() &&
        verified > clock.GetUtcNow().AddMinutes(-6);
    public string Status => state == "Connected" && !IsConnected ? "Checking connection" : state;
    public string IndicatorColor => IsConnected ? "#55D68B" : Status is "Disconnected" ? "#FF747D" : "#F5B94C";
    public string Detail => detail;
    public string VerificationText => verified.HasValue
        ? $"Last verified {verified.Value.ToLocalTime():HH:mm:ss} · session expires {expires?.ToLocalTime():HH:mm}"
        : "Connection has not been verified.";
    public string AccessibleStatus => $"Wealthsimple: {Status}. {detail}";

    public async Task SignInAsync(string email, string password, bool remember, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (stopped) return;
            Reset("Signing in", "Sending your sign-in request to Wealthsimple.");
            IsBusy = true; RememberSession = remember; Notify();
            if (!storageReady) { store.Open(); storageReady = true; }
            store.Clear();
            client = createClient();
            var operation = client.BeginLoginAsync(email.Trim(), password,
                new WealthsimpleLoginContext(null, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()), ct);
            // The auth boundary alone retains credentials while an explicit 2FA response is pending.
            email = password = "";
            await ApplySignInAsync(await operation, ct);
        }
        catch (OperationCanceledException) { Reset("Disconnected", "Sign-in cancelled."); }
        catch { Reset("Disconnected", "Sign-in could not finish. Check local session storage and try again."); }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }

    public async Task SubmitOtpAsync(string code, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (stopped || !AwaitingOtp || client == null) return;
            Set("Verifying code", "Submitting your verification code to Wealthsimple.", true);
            var operation = client.CompleteOtpAsync(code.Trim(), ct); code = "";
            await ApplySignInAsync(await operation, ct);
        }
        catch (ArgumentException) { Set("Verification required", "Enter only the verification code, then try again."); }
        catch (OperationCanceledException) { Reset("Disconnected", "Sign-in cancelled."); }
        catch { Reset("Disconnected", "Verification could not finish. Sign in again."); }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }

    private async Task ApplySignInAsync(WealthsimpleAuthResult result, CancellationToken ct)
    {
        if (result.Status == WealthsimpleAuthStatus.Authenticated)
        { AwaitingOtp = false; otpDeadline = null; await ConnectedAsync(result, ct); return; }
        if (result.Status is WealthsimpleAuthStatus.OtpRequired or WealthsimpleAuthStatus.InvalidOtp)
        {
            AwaitingOtp = true; otpDeadline ??= clock.GetUtcNow().AddMinutes(10);
            var instruction = result.OtpMethod switch
            {
                "sms" or "recovery_sms" => "Enter the verification code sent by text message.",
                "email" => "Enter the verification code sent by email.",
                _ => "Enter the verification code from your authenticator or Wealthsimple."
            };
            Set("Verification required", result.Status == WealthsimpleAuthStatus.InvalidOtp ? "That code was not accepted. " + instruction : instruction);
            return;
        }
        var message = result.Status switch
        {
            WealthsimpleAuthStatus.BrowserInteractionRequired => "Wealthsimple requires a browser security check for this sign-in. Use browser verification to continue.",
            WealthsimpleAuthStatus.Rejected => "Wealthsimple rejected the sign-in. Check your email and password, then try again.",
            WealthsimpleAuthStatus.RateLimited => "Wealthsimple has limited sign-in attempts. Wait before trying again.",
            WealthsimpleAuthStatus.TransportFailure or WealthsimpleAuthStatus.ServiceUnavailable => "Wealthsimple could not be reached. Try again when connectivity returns.",
            WealthsimpleAuthStatus.NotAuthenticated => "The sign-in attempt expired. Enter your email and password again.",
            _ => "Wealthsimple returned a sign-in response TraderVI does not yet support."
        };
        Reset("Disconnected", message + (result.HttpStatus.HasValue ? $" (HTTP {result.HttpStatus})" : ""));
        BrowserVerificationRequired = result.Status == WealthsimpleAuthStatus.BrowserInteractionRequired;
        Notify();
    }

    public async Task CancelSignInAsync()
    {
        await gate.WaitAsync();
        try { if (AwaitingOtp) Reset("Disconnected", "Sign-in cancelled. Your password has been forgotten."); }
        finally { gate.Release(); }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (started || stopped) return;
            started = true;
            Set("Checking connection", "Looking for a saved session.", true);
            store.Open(); storageReady = true;
            client = store.Load();
            RememberSession = client != null;
            if (client == null) Set("Disconnected", "Sign in to connect your Wealthsimple account.");
            else await CheckCoreAsync(false, ct);
        }
        catch (OperationCanceledException) { Reset("Disconnected", "Connection check cancelled."); }
        catch { Reset("Disconnected", "Saved session unavailable. Close other TraderVI windows or sign in again."); }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }

    // Ownership transfers immediately, including when cancellation or verification fails.
    public async Task ConnectAsync(WealthsimpleAuthClient candidate, bool remember, CancellationToken ct = default)
    {
        try { await gate.WaitAsync(ct); }
        catch { candidate.Dispose(); throw; }
        try
        {
            if (stopped) { candidate.Dispose(); return; }
            AwaitingOtp = BrowserVerificationRequired = false; otpDeadline = null;
            client?.Dispose(); client = candidate;
            SessionRevision++;
            RememberSession = remember;
            if (!storageReady) { store.Open(); storageReady = true; }
            await CheckCoreAsync(false, ct);
        }
        catch (OperationCanceledException) { Reset("Disconnected", "Sign-in cancelled."); }
        catch { Reset("Disconnected", "Could not secure the session. Sign in again."); }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }

    public async Task CheckAsync(bool force = false, CancellationToken ct = default)
    {
        // Timer ticks never queue up behind an active request.
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            Notify(); // Immediately retires green after expiry or a long workstation sleep.
            if (AwaitingOtp)
            {
                if (otpDeadline <= clock.GetUtcNow()) Reset("Disconnected", "The sign-in attempt expired. Sign in again.");
                return;
            }
            if (stopped || client == null) return;
            bool refresh = canRefresh && expires <= clock.GetUtcNow().AddSeconds(90);
            if (!force && !refresh && verified > clock.GetUtcNow().AddMinutes(-5)) return;
            await CheckCoreAsync(refresh, ct);
        }
        catch (OperationCanceledException) { Reset("Disconnected", "Connection check cancelled."); }
        catch { Reset("Disconnected", "Connection unavailable. Sign in again."); }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }

    private async Task CheckCoreAsync(bool refresh, CancellationToken ct)
    {
        Set(refresh ? "Reconnecting" : "Checking connection", "Verifying the session with Wealthsimple.", true);
        // Remove the durable copy BEFORE any operation that could consume/invalidate it.
        // A crash or ambiguous rotation can therefore never revive an obsolete refresh token.
        store.Clear();
        var result = refresh ? await client!.RefreshAsync(ct) : await client!.VerifyAsync(ct);
        if (!refresh && result.Status == WealthsimpleAuthStatus.NotAuthenticated && result.CanRefresh)
            result = await client!.RefreshAsync(ct);
        if (result.Status != WealthsimpleAuthStatus.Authenticated)
        {
            var unavailable = result.Status is WealthsimpleAuthStatus.TransportFailure or WealthsimpleAuthStatus.ServiceUnavailable or WealthsimpleAuthStatus.RateLimited;
            Reset(unavailable ? "Connection unavailable" : "Disconnected",
                unavailable ? "Could not verify your session. Sign in again when connectivity returns."
                : "Your session ended or needs verification. Sign in again.");
            return;
        }
        await ConnectedAsync(result, ct);
    }

    private async Task ConnectedAsync(WealthsimpleAuthResult result, CancellationToken ct)
    {
        expires = result.ExpiresAt; canRefresh = result.CanRefresh; verified = clock.GetUtcNow();
        if (RememberSession) await store.SaveAsync(client!, ct);
        Set("Connected", RememberSession ? "Signed in. Your session is encrypted for this Windows account."
            : "Signed in for this app session. Close TraderVI to forget it.");
    }

    public async Task SetRememberAsync(bool remember, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        var previous = RememberSession;
        var removed = false;
        try
        {
            RememberSession = remember;
            if (storageReady) store.Clear();
            removed = true;
            if (remember && IsConnected && client != null) await store.SaveAsync(client, ct);
            if (IsConnected) detail = remember ? "Signed in. Your session is encrypted for this Windows account."
                : "Signed in for this app session. Close TraderVI to forget it.";
        }
        catch { RememberSession = removed ? false : previous; detail = "Could not update saved sign-in. Disconnect and check local storage."; }
        finally { Notify(); gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Reset("Disconnected", "Disconnected from TraderVI. Your other Wealthsimple sessions are unchanged.");
            RememberSession = false;
            if (storageReady) store.Clear();
        }
        catch { detail = "Disconnected in memory, but the saved session could not be removed. Check local storage."; }
        finally { Notify(); gate.Release(); }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { stopped = true; SessionRevision++; client?.Dispose(); client = null; store.Dispose(); Notify(); }
        finally { gate.Release(); }
    }

    private void Reset(string status, string message)
    {
        SessionRevision++;
        client?.Dispose(); client = null; expires = verified = null; canRefresh = false;
        AwaitingOtp = BrowserVerificationRequired = false; otpDeadline = null;
        Set(status, message);
    }
    private void Set(string status, string message, bool busy = false)
    { state = status; detail = message; IsBusy = busy; Notify(); }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct = default) =>
        ReadAccountDataAsync(c => c.ReadTfsaAccountsAsync(ct), ct);
    public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        ReadAccountDataAsync(c => c.ReadTfsaSnapshotAsync(account, ct), ct);
    public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        ReadAccountDataAsync(c => c.ReadTfsaPendingOrdersAsync(account, ct), ct);
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleCanadianSecurity>>> FindCanadianSecuritiesAsync(string symbol, CancellationToken ct = default) =>
        ReadAccountDataAsync(c => c.FindCanadianSecuritiesAsync(symbol, ct), ct);
    private async Task<WealthsimpleReadResult<T>> ReadAccountDataAsync<T>(Func<WealthsimpleAuthClient, Task<WealthsimpleReadResult<T>>> read, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        bool checking = false;
        try
        {
            if (stopped || client == null || !verified.HasValue) return new(WealthsimpleReadStatus.NotAuthenticated);
            if (expires <= clock.GetUtcNow().AddSeconds(90) || verified <= clock.GetUtcNow().AddMinutes(-5))
            {
                checking = true;
                await CheckCoreAsync(canRefresh && expires <= clock.GetUtcNow().AddSeconds(90), ct);
                checking = false;
            }
            if (client == null || !IsConnected) return new(WealthsimpleReadStatus.NotAuthenticated);
            var result = await read(client);
            if (result.Status == WealthsimpleReadStatus.NotAuthenticated)
            {
                Reset("Disconnected", "Your Wealthsimple session ended. Sign in again.");
                if (storageReady) store.Clear();
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            if (checking) Reset("Disconnected", "Session verification cancelled. Sign in again.");
            throw;
        }
        catch
        {
            if (checking) Reset("Disconnected", "Could not verify the saved session. Sign in again.");
            return new(WealthsimpleReadStatus.Unavailable);
        }
        finally { IsBusy = false; Notify(); gate.Release(); }
    }
}
