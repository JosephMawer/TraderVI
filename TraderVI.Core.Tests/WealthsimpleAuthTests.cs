using Core.WSTrade.Authentication;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleAuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 3, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed record Sent(string Method, Uri Uri, string Body, Dictionary<string, string> Headers);
    private sealed class Wire(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];
        public int PauseRequest { get; set; }
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
            if (Requests.Count > responses.Length) throw new InvalidOperationException("Unexpected request in fixture.");
            if (Requests.Count == PauseRequest)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(ct);
            }
            return responses[Requests.Count - 1]();
        }
    }

    private static HttpResponseMessage Response(int status, string body, params (string, string)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
    private static string Tokens(string access = "synthetic-access", string refresh = "synthetic-refresh", string type = "Bearer",
        long? created = null, int expires = 1800) => JsonSerializer.Serialize(new
        { access_token = access, refresh_token = refresh, token_type = type, created_at = created ?? Now.ToUnixTimeSeconds(), expires_in = expires });
    private static HttpResponseMessage Info() => Response(200, "{\"profiles\":{\"trade\":{\"default\":\"synthetic-user\"}}}");

    [Fact]
    public async Task CapturedPasswordOtpFlowRequiresCodeThenTokenVerification()
    {
        using var wire = new Wire(
            () => Response(401, "{\"error\":\"invalid_grant\"}", ("x-wealthsimple-otp", "required; method=app; digits=6"),
                ("x-wealthsimple-otp-authenticated-claim", "synthetic-claim")),
            () => Response(200, Tokens()), Info);
        using var client = new WealthsimpleAuthClient("synthetic-client", wire, new Clock());
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password",
            new WealthsimpleLoginContext("synthetic-turnstile", "synthetic-device", "synthetic-session")))
            .Status.ShouldBe(WealthsimpleAuthStatus.OtpRequired);
        wire.Requests.Count.ShouldBe(1);
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.Authenticated);
        using var first = JsonDocument.Parse(wire.Requests[0].Body);
        first.RootElement.GetProperty("username").GetString().ShouldBe("synthetic@example.invalid");
        first.RootElement.GetProperty("grant_type").GetString().ShouldBe("password");
        first.RootElement.GetProperty("skip_provision").GetBoolean().ShouldBeTrue();
        first.RootElement.TryGetProperty("email", out _).ShouldBeFalse();
        first.RootElement.TryGetProperty("otp", out _).ShouldBeFalse();
        wire.Requests[1].Headers["x-wealthsimple-otp"].ShouldBe("123456");
        wire.Requests[1].Headers["x-wealthsimple-otp-authenticated-claim"].ShouldBe("synthetic-claim");
        wire.Requests[1].Headers["x-wealthsimple-turnstile"].ShouldBe("synthetic-turnstile");
        wire.Requests[2].Uri.ShouldBe(WealthsimpleAuthClient.InfoUri);
        wire.Requests[2].Headers["Authorization"].ShouldBe("Bearer synthetic-access");
        wire.Requests.All(r => !r.Headers.ContainsKey("DPoP")).ShouldBeTrue();
        wire.Requests[2].Headers.ContainsKey("x-wealthsimple-otp").ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidCodeCanBeRetriedOnlyByAnExplicitCall()
    {
        using var wire = new Wire(() => Response(401, "{}", ("x-wealthsimple-otp", "required; method=sms")),
            () => Response(401, "{}", ("x-wealthsimple-otp", "invalid")), () => Response(200, Tokens()), Info);
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password");
        (await client.CompleteOtpAsync("000000")).Status.ShouldBe(WealthsimpleAuthStatus.InvalidOtp);
        wire.Requests.Count.ShouldBe(2);
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.Authenticated);
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        wire.Requests.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("123456;remember=true")]
    [InlineData("123456\r\nInjected: yes")]
    public void OtpCannotInjectHeaderFlags(string code)
    {
        using var client = new WealthsimpleAuthClient("synthetic", new Wire(), new Clock());
        Should.Throw<ArgumentException>(() => client.CompleteOtpAsync(code));
    }

    [Theory]
    [InlineData(429, WealthsimpleAuthStatus.RateLimited)]
    [InlineData(503, WealthsimpleAuthStatus.ServiceUnavailable)]
    [InlineData(302, WealthsimpleAuthStatus.Rejected)]
    [InlineData(401, WealthsimpleAuthStatus.Rejected)]
    public async Task FailuresDoNotRetryOrExposeResponseText(int status, WealthsimpleAuthStatus expected)
    {
        using var wire = new Wire(() => Response(status, "{\"error\":\"synthetic-password-and-token\"}"));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        var result = await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password");
        result.Status.ShouldBe(expected);
        JsonSerializer.Serialize(result).ShouldNotContain("synthetic");
        wire.Requests.Count.ShouldBe(1);
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
    }

    [Theory]
    [InlineData("{\"error\":\"login_challenge_required\"}", "x-wealthsimple-login-challenge-id")]
    [InlineData("{}", "x-wealthsimple-password-authenticated-claim")]
    public async Task UnsupportedChallengesDoNotFallbackToOtp(string body, string header)
    {
        using var wire = new Wire(() => Response(401, body, (header, "synthetic-claim"), ("x-wealthsimple-otp", "required")));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password")).Status
            .ShouldBe(WealthsimpleAuthStatus.BrowserInteractionRequired);
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
    }

    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("[]")] [InlineData("<html>synthetic-secret</html>")]
    [InlineData("{\"access_token\":\"synthetic\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("{\"access_token\":\"synthetic\",\"token_type\":\"Bearer\",\"expires_in\":\"1800\"}")]
    [InlineData("{\"access_token\":\"synthetic\",\"token_type\":\"Bearer\",\"expires_in\":1800,\"created_at\":\"invalid\"}")]
    public async Task MalformedSuccessNeverEstablishesAuthentication(string body)
    {
        using var wire = new Wire(() => Response(200, body));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password")).Status.ShouldBe(WealthsimpleAuthStatus.InvalidResponse);
        (await client.VerifyAsync()).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        wire.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EmptyProfilesDoNotVerifyAUser()
    {
        using var wire = new Wire(() => Response(200, Tokens()), () => Response(200, "{\"profiles\":{}}"));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password")).Status.ShouldBe(WealthsimpleAuthStatus.InvalidResponse);
    }

    [Fact]
    public async Task RefreshUsesRotatedTokenAndNeverReplaysPassword()
    {
        using var wire = new Wire(() => Response(200, Tokens()), Info,
            () => Response(200, Tokens("synthetic-new-access", "synthetic-new-refresh")), Info,
            () => Response(200, Tokens("synthetic-last-access", "synthetic-last-refresh")), Info);
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password");
        wire.PauseRequest = 3;
        var refreshOne = client.RefreshAsync();
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refreshTwo = client.RefreshAsync();
        wire.Requests.Count.ShouldBe(3);
        refreshTwo.IsCompleted.ShouldBeFalse();
        wire.Release.TrySetResult(true);
        await Task.WhenAll(refreshOne, refreshTwo);
        using var firstRefresh = JsonDocument.Parse(wire.Requests[2].Body);
        using var secondRefresh = JsonDocument.Parse(wire.Requests[4].Body);
        firstRefresh.RootElement.GetProperty("refresh_token").GetString().ShouldBe("synthetic-refresh");
        secondRefresh.RootElement.GetProperty("refresh_token").GetString().ShouldBe("synthetic-new-refresh");
        firstRefresh.RootElement.TryGetProperty("password", out _).ShouldBeFalse();
        wire.Requests[5].Headers["Authorization"].ShouldBe("Bearer synthetic-last-access");
    }

    [Fact]
    public async Task UncertainRefreshInvalidatesSessionAndDoesNotRetry()
    {
        using var wire = new Wire(() => Response(200, Tokens()), Info,
            () => throw new HttpRequestException("synthetic-password leaked by an upstream transport"));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password");
        var result = await client.RefreshAsync();
        result.Status.ShouldBe(WealthsimpleAuthStatus.TransportFailure);
        result.ToString().ShouldNotContain("synthetic");
        (await client.RefreshAsync()).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        (await client.VerifyAsync()).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        wire.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CancellationClearsPendingCredentialsAndDoesNotExposeTransportMessage()
    {
        using var cancellation = new CancellationTokenSource();
        using var wire = new Wire(() =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException("synthetic-password-secret", cancellation.Token);
        });
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password", cancellation.Token));
        error.ToString().ShouldNotContain("synthetic-password-secret");
        error.InnerException.ShouldBeNull();
        (await client.CompleteOtpAsync("123456")).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        wire.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DpopTokenCannotSilentlyBecomeABearerToken()
    {
        using var wire = new Wire(() => Response(200, Tokens(type: "DPoP")));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock());
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password")).Status.ShouldBe(WealthsimpleAuthStatus.InvalidResponse);
        wire.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void DpopSignatureVerifiesAndBindsMethodPathTokenAndNonce()
    {
        using var key = new WealthsimpleDpopKey();
        var uri = new Uri("https://example.invalid/v1/token/info?ignored=1#fragment");
        var proof = key.CreateProof("get", uri, Now, "synthetic-access", "synthetic-nonce");
        var parts = proof.Split('.');
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        var jwk = header.RootElement.GetProperty("jwk");
        jwk.TryGetProperty("d", out _).ShouldBeFalse();
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Decode(jwk.GetProperty("x").GetString()), Y = Decode(jwk.GetProperty("y").GetString()) }
        });
        verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).ShouldBeTrue();
        payload.RootElement.GetProperty("htm").GetString().ShouldBe("GET");
        payload.RootElement.GetProperty("htu").GetString().ShouldBe("https://example.invalid/v1/token/info");
        payload.RootElement.GetProperty("iat").GetInt64().ShouldBe(Now.ToUnixTimeSeconds());
        Decode(payload.RootElement.GetProperty("ath").GetString()).ShouldBe(SHA256.HashData(Encoding.ASCII.GetBytes("synthetic-access")));
        payload.RootElement.GetProperty("nonce").GetString().ShouldBe("synthetic-nonce");
        key.CreateProof("get", uri, Now, "synthetic-access", "synthetic-nonce").ShouldNotBe(proof);
    }

    [Fact]
    public async Task DpopNonceChallengeRetriesOnceWithNewProofThenStops()
    {
        using var wire = new Wire(
            () => Response(400, "{\"error\":\"use_dpop_nonce\"}", ("DPoP-Nonce", "synthetic-nonce")),
            () => Response(400, "{\"error\":\"use_dpop_nonce\"}", ("DPoP-Nonce", "synthetic-nonce-2")));
        using var client = new WealthsimpleAuthClient("synthetic", wire, new Clock(), requestDpop: true);
        (await client.BeginLoginAsync("synthetic@example.invalid", "synthetic-password")).Status.ShouldBe(WealthsimpleAuthStatus.Rejected);
        wire.Requests.Count.ShouldBe(2);
        using var proof = JsonDocument.Parse(Decode(wire.Requests[1].Headers["DPoP"].Split('.')[1]));
        proof.RootElement.GetProperty("nonce").GetString().ShouldBe("synthetic-nonce");
        proof.RootElement.TryGetProperty("ath", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task BrowserHandoffUsesOnlyBearerSessionAndNeverReplaysCapture()
    {
        using var har = Har(Entry(), Entry(host: "https://attacker.invalid/v1/oauth/v2/token", access: "wrong-token", minutes: 1),
            Entry(grant: "client_credentials", access: "app-only-token", minutes: 2));
        using var wire = new Wire(Info);
        using var client = WealthsimpleAuthClient.FromBrowserHar(har, wire, new Clock());
        JsonSerializer.Serialize(client).ShouldBe("{}");
        (await client.VerifyAsync()).Status.ShouldBe(WealthsimpleAuthStatus.Authenticated);
        var sent = wire.Requests.Single();
        sent.Method.ShouldBe("GET");
        sent.Body.ShouldBeNull();
        sent.Uri.ShouldBe(WealthsimpleAuthClient.InfoUri);
        sent.Headers["Authorization"].ShouldBe("Bearer synthetic-access");
        sent.Headers.ContainsKey("Cookie").ShouldBeFalse();
        sent.Headers.ContainsKey("x-wealthsimple-turnstile").ShouldBeFalse();
        sent.Headers.ContainsKey("x-wealthsimple-otp").ShouldBeFalse();
    }

    [Fact]
    public void LatestDpopCaptureCannotFallBackToEarlierBearer()
    {
        using var har = Har(Entry(), Entry(type: "DPoP", minutes: 1));
        Should.Throw<InvalidDataException>(() => WealthsimpleAuthClient.FromBrowserHar(har, new Wire(), new Clock()));
    }

    [Fact]
    public async Task ExpiredBrowserTokenIsNotSent()
    {
        using var har = Har(Entry(created: Now.AddHours(-1).ToUnixTimeSeconds()));
        using var wire = new Wire();
        using var client = WealthsimpleAuthClient.FromBrowserHar(har, wire, new Clock());
        (await client.VerifyAsync()).Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        wire.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpiredAccessStillAllowsExplicitRefresh()
    {
        using var har = Har(Entry(created: Now.AddHours(-1).ToUnixTimeSeconds()));
        using var wire = new Wire(() => Response(200, Tokens("synthetic-new-access", "synthetic-new-refresh")), Info);
        using var client = WealthsimpleAuthClient.FromBrowserHar(har, wire, new Clock());
        var expired = await client.VerifyAsync();
        expired.Status.ShouldBe(WealthsimpleAuthStatus.NotAuthenticated);
        expired.CanRefresh.ShouldBeTrue();
        wire.Requests.ShouldBeEmpty();
        (await client.RefreshAsync()).Status.ShouldBe(WealthsimpleAuthStatus.Authenticated);
        wire.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public void MissingCaptureFieldsProduceSafeFailure()
    {
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("{\"synthetic-secret\":1}"));
        var error = Should.Throw<InvalidDataException>(() => WealthsimpleAuthClient.FromBrowserHar(malformed, new Wire(), new Clock()));
        error.ToString().ShouldNotContain("synthetic-secret");
        error.InnerException.ShouldBeNull();
    }

    [Fact]
    public void CaptureErrorsDoNotEchoContentOrInnerExceptions()
    {
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("{synthetic-password-secret}"));
        var error = Should.Throw<InvalidDataException>(() => WealthsimpleAuthClient.FromBrowserHar(malformed, new Wire(), new Clock()));
        error.ToString().ShouldNotContain("synthetic-password-secret");
        error.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task Base64ResponseContentIsAcceptedWithoutExportingIt()
    {
        using var har = Har(Entry(base64: true));
        using var wire = new Wire(Info);
        using var client = WealthsimpleAuthClient.FromBrowserHar(har, wire, new Clock());
        (await client.VerifyAsync()).Status.ShouldBe(WealthsimpleAuthStatus.Authenticated);
    }

    private static MemoryStream Har(params object[] entries) => new(JsonSerializer.SerializeToUtf8Bytes(new { log = new { entries } }));
    private static object Entry(string host = "https://api.production.wealthsimple.com/v1/oauth/v2/token",
        string grant = "password", string access = "synthetic-access", string type = "Bearer", int minutes = 0,
        long? created = null, bool base64 = false) => new
    {
        startedDateTime = Now.AddMinutes(minutes),
        request = new
        {
            method = "POST", url = host,
            headers = new[] { new { name = "x-wealthsimple-turnstile", value = "synthetic-turnstile" } },
            postData = new { text = JsonSerializer.Serialize(new
            { grant_type = grant, client_id = "synthetic-client", password = "synthetic-password", username = "synthetic@example.invalid" }) }
        },
        response = new
        {
            status = 200,
            content = new
            {
                encoding = base64 ? "base64" : null,
                text = base64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(Tokens(access, type: type, created: created)))
                    : Tokens(access, type: type, created: created)
            }
        }
    };
    private static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '='));
}
