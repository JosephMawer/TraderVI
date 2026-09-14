using Core.WSTrade.Authentication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleConnectionTests
{
    private static HttpResponseMessage OtpResponse(bool invalid = false)
    {
        var result = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
        result.Headers.Add("x-wealthsimple-otp", (invalid ? "invalid" : "required") + "; method=app; digits=6");
        result.Headers.Add("x-wealthsimple-otp-authenticated-claim", "fixture-otp-claim");
        return result;
    }
    private static HttpResponseMessage TokenResponse(Clock clock) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { token_type = "Bearer", access_token = "native-access", refresh_token = "native-refresh", created_at = clock.Now.ToUnixTimeSeconds(), expires_in = 1800 })) };

    [Fact]
    public async Task NativePasswordAndOtpVerifyBeforeGreenAndNeverPersistPendingCredentials()
    {
        var clock = new Clock(); var store = new Store(); var wire = new Wire();
        var sentCodes = new List<string>();
        wire.Reply = async request =>
        {
            if (request.Method == HttpMethod.Get) return Info();
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert.Equal("password", json.RootElement.GetProperty("grant_type").GetString());
            Assert.Equal("native-password", json.RootElement.GetProperty("password").GetString());
            if (!request.Headers.Contains("x-wealthsimple-otp")) return OtpResponse();
            var code = string.Join("", request.Headers.GetValues("x-wealthsimple-otp")); sentCodes.Add(code);
            Assert.Contains("fixture-otp-claim", request.Headers.GetValues("x-wealthsimple-otp-authenticated-claim"));
            return code == "111111" ? OtpResponse(true) : TokenResponse(clock);
        };
        var vm = new WealthsimpleConnection(store, clock, () => new WealthsimpleAuthClient("fixture-client", wire, clock));
        await vm.StartAsync();
        await vm.SignInAsync("native@example.invalid", "native-password", true);
        Assert.True(vm.AwaitingOtp); Assert.False(vm.IsConnected); Assert.Equal(0, store.Saves);
        await vm.CheckAsync(); await vm.CheckAsync(true);
        Assert.Equal(1, wire.Calls); Assert.True(vm.AwaitingOtp);
        await vm.SubmitOtpAsync("111111");
        Assert.True(vm.AwaitingOtp); Assert.False(vm.IsConnected); Assert.Equal(0, store.Saves);
        await vm.SubmitOtpAsync("222222");
        Assert.False(vm.AwaitingOtp); Assert.True(vm.IsConnected); Assert.Equal(1, store.Saves);
        Assert.Equal(new[] { "111111", "222222" }, sentCodes);
        Assert.Equal(4, wire.Calls); await vm.StopAsync();
    }

    [Fact]
    public async Task PasswordOnlySuccessDoesNotNeedOtpOrBrowser()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        wire.Reply = request => Task.FromResult(request.Method == HttpMethod.Post ? TokenResponse(clock) : Info());
        var vm = new WealthsimpleConnection(store, clock, () => new WealthsimpleAuthClient("fixture-client", wire, clock));
        await vm.StartAsync(); await vm.SignInAsync("native@example.invalid", "native-password", false);
        Assert.True(vm.IsConnected); Assert.False(vm.AwaitingOtp); Assert.False(vm.BrowserVerificationRequired);
        Assert.Equal(2, wire.Calls); Assert.Equal(0, store.Saves); await vm.StopAsync();
    }

    [Theory]
    [InlineData("turnstile_required", true)]
    [InlineData("invalid_grant", false)]
    public async Task BrowserFallbackRequiresExplicitServerChallenge(string error, bool expected)
    {
        var clock = new Clock(); var wire = new Wire();
        wire.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent(JsonSerializer.Serialize(new { error })) });
        var vm = new WealthsimpleConnection(new Store(), clock, () => new WealthsimpleAuthClient("fixture-client", wire, clock));
        await vm.StartAsync(); await vm.SignInAsync("native@example.invalid", "native-password", false);
        Assert.Equal(expected, vm.BrowserVerificationRequired); Assert.False(vm.AwaitingOtp); Assert.False(vm.IsConnected);
        await vm.CheckAsync(); Assert.Equal(1, wire.Calls); await vm.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelOrTimeoutForgetsPendingOtpInsteadOfSendingIt(bool cancel)
    {
        var clock = new Clock(); var wire = new Wire(); wire.Reply = _ => Task.FromResult(OtpResponse());
        var vm = new WealthsimpleConnection(new Store(), clock, () => new WealthsimpleAuthClient("fixture-client", wire, clock));
        await vm.StartAsync(); await vm.SignInAsync("native@example.invalid", "native-password", false);
        if (cancel) await vm.CancelSignInAsync();
        else { clock.Now = clock.Now.AddMinutes(11); await vm.CheckAsync(); }
        await vm.SubmitOtpAsync("222222");
        Assert.False(vm.AwaitingOtp); Assert.False(vm.IsConnected); Assert.Equal(1, wire.Calls); await vm.StopAsync();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Info());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Reply(request); }
    }
    private sealed class Store : IWealthsimpleSessionStore
    {
        public WealthsimpleAuthClient Saved;
        public int Saves, Clears;
        public bool HasFile, FailClear;
        public void Open() { }
        public WealthsimpleAuthClient Load() => Saved;
        public Task SaveAsync(WealthsimpleAuthClient client, CancellationToken ct) { Saves++; HasFile = true; return Task.CompletedTask; }
        public void Clear() { if (FailClear) throw new IOException(); Clears++; HasFile = false; }
        public void Dispose() { }
    }
    private sealed class Protector : IWealthsimpleSessionProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public byte[] Input;
        public string Fields;
        public byte[] Protect(byte[] plaintext)
        {
            Input = plaintext; Fields = Encoding.UTF8.GetString(plaintext);
            using var aes = new AesGcm(key, 16);
            var output = new byte[12 + 16 + plaintext.Length];
            RandomNumberGenerator.Fill(output.AsSpan(0, 12));
            aes.Encrypt(output.AsSpan(0, 12), plaintext, output.AsSpan(28), output.AsSpan(12, 16));
            return output;
        }
        public byte[] Unprotect(byte[] ciphertext)
        {
            using var aes = new AesGcm(key, 16);
            var plain = new byte[ciphertext.Length - 28];
            aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), plain);
            return plain;
        }
    }
    private static HttpResponseMessage Info() => new(HttpStatusCode.OK) { Content = new StringContent("{\"profiles\":{\"trade\":{}}}") };
    private static MemoryStream Body(object value) => new(JsonSerializer.SerializeToUtf8Bytes(value));
    private static WealthsimpleAuthClient Client(Wire wire, Clock clock, int seconds = 1800)
    {
        using var har = Body(new { log = new { entries = new[] { new
        {
            startedDateTime = clock.Now,
            request = new { method = "POST", url = WealthsimpleAuthClient.TokenUri.ToString(), postData = new { text = JsonSerializer.Serialize(new
            { client_id = "test-client", grant_type = "password", username = "secret-email", password = "secret-password" }) } },
            response = new { status = 200, content = new { text = JsonSerializer.Serialize(new
            { token_type = "Bearer", access_token = "test-access", refresh_token = "test-refresh", created_at = clock.Now.AddSeconds(seconds < 0 ? -1801 : 0).ToUnixTimeSeconds(), expires_in = seconds < 0 ? 1800 : seconds }) } }
        } } } });
        return WealthsimpleAuthClient.FromBrowserHar(har, wire, clock);
    }

    [Fact]
    public async Task FreshStartIsRedAndMakesNoRequest()
    {
        var vm = new WealthsimpleConnection(new Store());
        await vm.StartAsync();
        Assert.Equal("Disconnected", vm.Status); Assert.Equal("#FF747D", vm.IndicatorColor);
        Assert.False(vm.RememberSession); Assert.False(vm.IsConnected);
        await vm.StopAsync();
    }

    [Fact]
    public async Task SavedSessionIsAmberUntilServerVerifiesIt()
    {
        var clock = new Clock(); var wire = new Wire();
        var waiting = new TaskCompletionSource<HttpResponseMessage>();
        wire.Reply = _ => waiting.Task;
        var store = new Store { Saved = Client(wire, clock), HasFile = true };
        var vm = new WealthsimpleConnection(store, clock);
        var start = vm.StartAsync();
        Assert.False(vm.IsConnected); Assert.Equal("#F5B94C", vm.IndicatorColor);
        Assert.False(store.HasFile);
        waiting.SetResult(Info()); await start;
        Assert.True(vm.IsConnected); Assert.Equal("#55D68B", vm.IndicatorColor);
        Assert.True(store.HasFile); Assert.True(vm.RememberSession);
        await vm.StopAsync();
    }

    [Fact]
    public async Task MemoryOnlySignInDoesNotPersistAndDisconnectClearsIt()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock), false);
        Assert.True(vm.IsConnected); Assert.Equal(0, store.Saves);
        await vm.DisconnectAsync();
        Assert.False(vm.IsConnected); Assert.False(store.HasFile);
        await vm.CheckAsync(true); Assert.Equal(1, wire.Calls);
        await vm.StopAsync();
    }

    [Fact]
    public async Task ExpiryOrStaleVerificationRetiresGreenEvenBeforeTimerRuns()
    {
        var clock = new Clock(); var wire = new Wire();
        var vm = new WealthsimpleConnection(new Store(), clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock), false);
        clock.Now = clock.Now.AddMinutes(7);
        Assert.False(vm.IsConnected); Assert.Equal("#F5B94C", vm.IndicatorColor);
        await vm.CheckAsync(); Assert.True(vm.IsConnected); Assert.Equal(2, wire.Calls);
        clock.Now = clock.Now.AddHours(1);
        Assert.False(vm.IsConnected);
        await vm.StopAsync();
    }

    [Fact]
    public async Task AmbiguousRefreshClearsSavedCopyAndIsNeverRetriedByTimer()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock, 120), true);
        wire.Reply = request =>
        {
            Assert.False(store.HasFile); Assert.Equal(WealthsimpleAuthClient.TokenUri, request.RequestUri);
            throw new HttpRequestException("test-secret-server-detail");
        };
        clock.Now = clock.Now.AddSeconds(40);
        await vm.CheckAsync(); await vm.CheckAsync(); await vm.CheckAsync(true);
        Assert.Equal(2, wire.Calls); Assert.False(vm.IsConnected); Assert.False(store.HasFile);
        Assert.Equal("Connection unavailable", vm.Status); Assert.DoesNotContain("test-secret", vm.Detail);
        await vm.StopAsync();
    }

    [Fact]
    public async Task RejectedVerificationTurnsRedAndRemovesSavedSession()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock), true);
        wire.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        await vm.CheckAsync(true);
        Assert.Equal("Disconnected", vm.Status); Assert.False(store.HasFile);
        await vm.StopAsync();
    }

    [Fact]
    public async Task ConcurrentTicksDoNotDuplicateRefreshAndNewSessionIsSavedAfterVerification()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock, 120), true);
        var waiting = new TaskCompletionSource<HttpResponseMessage>();
        wire.Reply = request => request.Method == HttpMethod.Post ? waiting.Task : Task.FromResult(Info());
        clock.Now = clock.Now.AddSeconds(40);
        var refresh = vm.CheckAsync(); await vm.CheckAsync(); await vm.CheckAsync();
        Assert.Equal(2, wire.Calls); Assert.False(store.HasFile); Assert.False(vm.IsConnected);
        waiting.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        { token_type = "Bearer", access_token = "new-access", refresh_token = "rotated-refresh", expires_in = 1800, created_at = clock.Now.ToUnixTimeSeconds() })) });
        await refresh;
        Assert.Equal(3, wire.Calls); Assert.True(vm.IsConnected); Assert.True(store.HasFile); Assert.Equal(2, store.Saves);
        await vm.StopAsync();
    }

    [Fact]
    public async Task FailedDurableClearPreventsAnyRefresh()
    {
        var clock = new Clock(); var wire = new Wire(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(wire, clock, 120), true);
        store.FailClear = true; clock.Now = clock.Now.AddSeconds(40);
        await vm.CheckAsync(); Assert.Equal(1, wire.Calls); Assert.False(vm.IsConnected);
        await vm.StopAsync();
    }

    [Fact]
    public async Task ProtectedSnapshotRoundTripsWithoutPasswordAndWipesPlaintextBuffer()
    {
        var clock = new Clock(); using var client = Client(new Wire(), clock);
        var protector = new Protector();
        var encrypted = await client.ProtectSessionAsync(protector);
        Assert.All(protector.Input, value => Assert.Equal((byte)0, value));
        Assert.DoesNotContain("secret-email", protector.Fields); Assert.DoesNotContain("secret-password", protector.Fields);
        Assert.DoesNotContain("test-access", Encoding.UTF8.GetString(encrypted));
        using var restored = WealthsimpleAuthClient.FromProtectedSession(encrypted, protector);
        Assert.Equal("{}", JsonSerializer.Serialize(restored));
        var again = await restored.ProtectSessionAsync(protector);
        Assert.Contains("test-refresh", protector.Fields);
        encrypted[30] ^= 1;
        var error = Assert.Throws<InvalidDataException>(() => WealthsimpleAuthClient.FromProtectedSession(encrypted, protector));
        Assert.Null(error.InnerException); Assert.DoesNotContain("test-refresh", error.ToString());
    }

    [Fact]
    public async Task FailedRemovalDoesNotClaimThatRememberedSessionWasDisabled()
    {
        var clock = new Clock(); var store = new Store();
        var vm = new WealthsimpleConnection(store, clock); await vm.StartAsync();
        await vm.ConnectAsync(Client(new Wire(), clock), true);
        store.FailClear = true;
        await vm.SetRememberAsync(false);
        Assert.True(vm.RememberSession); Assert.True(store.HasFile);
        Assert.Contains("Could not update", vm.Detail);
        await vm.StopAsync();
    }

    [Theory]
    [InlineData("https://attacker.invalid/v1/oauth/v2/token", "password", "Bearer")]
    [InlineData("https://api.production.wealthsimple.com/v1/oauth/v2/token", "client_credentials", "Bearer")]
    [InlineData("https://api.production.wealthsimple.com/v1/oauth/v2/token", "password", "DPoP")]
    public void BrowserHandoffRejectsWrongOriginAppTokensAndUnownedSigningKeys(string uri, string grant, string type)
    {
        using var request = Body(new { grant_type = grant, client_id = "test-client", password = "hidden-password" });
        using var response = Body(new { token_type = type, access_token = "hidden-access", refresh_token = "hidden-refresh", created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), expires_in = 1800 });
        var error = Assert.Throws<InvalidDataException>(() => WealthsimpleAuthClient.FromBrowserExchange(new Uri(uri), "POST", 200, request, response));
        Assert.DoesNotContain("hidden-", error.ToString());
    }

    [Fact]
    public async Task BrowserHandoffAcceptsBearerWithoutKeepingCredentialFields()
    {
        using var request = Body(new { grant_type = "password", client_id = "test-client", password = "hidden-password" });
        using var response = Body(new { token_type = "Bearer", access_token = "test-access", refresh_token = "test-refresh", created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), expires_in = 1800 });
        using var client = WealthsimpleAuthClient.FromBrowserExchange(WealthsimpleAuthClient.TokenUri, "POST", 200, request, response);
        var protector = new Protector(); await client.ProtectSessionAsync(protector);
        Assert.DoesNotContain("hidden-password", protector.Fields);
    }
}
