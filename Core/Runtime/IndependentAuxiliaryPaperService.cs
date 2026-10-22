#nullable enable
using Core.Oracle;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Remaining paper workers share the engine lifetime, never a dashboard connection.</summary>
public sealed class IndependentAuxiliaryPaperService
{
    private readonly PaperTradingMonitor tracked = new();
    private readonly SystemShadowController shadow = new();
    private readonly SemaphoreSlim trackedGate = new(1, 1), shadowGate = new(1, 1);
    private readonly ReviewedTsxSessionCalendar calendar;
    private readonly IDelphiLiveNotifier notifier;
    private readonly CancellationTokenSource stopAi = new();
    private Task ai = Task.CompletedTask;
    private volatile bool closing, aiFailed;
    private DateTime? nextTracked, nextShadow;

    public IndependentAuxiliaryPaperService(ReviewedTsxSessionCalendar calendar, IDelphiLiveNotifier notifier)
    { this.calendar = calendar; this.notifier = notifier; }
    public Task StartAsync() { ai = Task.Run(RunAiAsync); return Task.CompletedTask; }
    public void BeginStop() { if (closing) return; closing = true; stopAi.Cancel(); }
    public Task CheckAiAsync() => aiFailed ? Task.FromException(new InvalidOperationException("AI research needs operator attention.")) : Task.CompletedTask;
    public async Task CloseAsync()
    {
        BeginStop();
        await trackedGate.WaitAsync().ConfigureAwait(false); trackedGate.Release();
        await shadowGate.WaitAsync().ConfigureAwait(false); shadowGate.Release();
        await ai.ConfigureAwait(false); stopAi.Dispose();
    }
    public async Task TrackedTickAsync()
    {
        if (!await trackedGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!Due(nextTracked)) return;
            await tracked.PollOnceAsync(ReadAutomaticGhostExits()).ConfigureAwait(false);
            nextTracked = PaperTradingMonitor.NextScheduledPollLocal(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
        }
        finally { trackedGate.Release(); }
    }
    public async Task ShadowTickAsync()
    {
        if (!await shadowGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!Due(nextShadow)) return;
            await shadow.PollOnceAsync().ConfigureAwait(false);
            nextShadow = PaperTradingMonitor.NextScheduledPollLocal(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
        }
        finally { shadowGate.Release(); }
    }
    public async Task ShadowProtectionAsync()
    {
        if (!closing && IsSession()) await shadow.PollProgressiveProtectionAsync().ConfigureAwait(false);
    }
    public async Task<PaperMonitorCycleResult> PollTrackedAsync()
    {
        await trackedGate.WaitAsync().ConfigureAwait(false);
        try
        {
            RequirePollWindow();
            var result = await tracked.PollOnceAsync(ReadAutomaticGhostExits()).ConfigureAwait(false);
            nextTracked = PaperTradingMonitor.NextScheduledPollLocal(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
            return result;
        }
        finally { trackedGate.Release(); }
    }
    public async Task<SystemShadowPollResult> PollShadowAsync()
    {
        await shadowGate.WaitAsync().ConfigureAwait(false);
        try
        {
            RequirePollWindow();
            var result = await shadow.PollOnceAsync().ConfigureAwait(false);
            nextShadow = PaperTradingMonitor.NextScheduledPollLocal(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
            return result;
        }
        finally { shadowGate.Release(); }
    }
    private bool IsSession() => calendar.IsRegularSession(DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow)));
    private bool Due(DateTime? next) => !closing && IsSession() && PaperTradingMonitor.IsAutomaticPollTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow)) &&
        (next is null || PaperTradingMonitor.ToToronto(DateTime.UtcNow) >= next);
    private void RequirePollWindow()
    {
        if (closing || !IsSession() || !PaperTradingMonitor.IsAutomaticPollTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow)))
            throw new InvalidOperationException("The engine is stopping or the paper monitoring window is closed.");
    }
    internal static bool ReadAutomaticGhostExits()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "settings.json");
        if (!File.Exists(path)) return true; // Same default as the existing dashboard; read the same saved operator preference.
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Paper preferences are oversized.");
        return JsonSerializer.Deserialize<PaperPreferences>(File.ReadAllText(path),
            new JsonSerializerOptions { RespectRequiredConstructorParameters = true })?.AutomaticGhostExits
            ?? throw new InvalidDataException("Paper preferences are incomplete.");
    }
    private sealed record PaperPreferences(bool AutomaticGhostExits);

    private async Task RunAiAsync()
    {
        try
        {
            var ct = stopAi.Token; ct.ThrowIfCancellationRequested();
            var settings = AiPaperReviewSettings.Load(AiPaperReviewSettings.LocalSettingsPath);
            var test = AiReviewTestSession.Parse(Environment.GetEnvironmentVariable(AiReviewTestSession.EnvironmentVariable), DateTimeOffset.UtcNow);
            if (test is not null) settings = test.EnableForThisLaunch(settings);
            if (settings?.Enabled != true) return;
            using var runtime = new AiPaperReviewRuntime(settings);
            await runtime.RunAsync(notice =>
            {
                if (notice.State == "NeedsAttention") aiFailed = true;
                string message = notice.State switch
                {
                    "Reviewed" => "AI portfolio review saved; the reviewer does not execute trades.",
                    "TestComplete" => "AI review test completed; automatic reviews remain off.",
                    "TestExpired" => "AI review test reached its time limit; automatic reviews remain off.",
                    "NeedsAttention" => "AI research stopped. Check its configuration and saved history before restarting.",
                    _ => "AI research is waiting for its next eligible scheduled review."
                };
                _ = notifier.NotifyAsync(new(aiFailed ? "Warning" : "Information", "AiResearch", message));
            }, ct, test).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopAi.IsCancellationRequested) { }
        catch
        {
            aiFailed = true;
            await notifier.NotifyAsync(new("Warning", "AiResearch", "AI research stopped. Configuration or saved history needs attention.")).ConfigureAwait(false);
        }
    }
}
