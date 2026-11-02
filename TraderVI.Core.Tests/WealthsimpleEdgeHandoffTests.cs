using Core.WSTrade.Authentication;
using Shouldly;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleEdgeHandoffTests
{
    private static readonly string Pairing = new('a', 64);
    private static byte[] Message(string pairing = null, string url = null, string method = "POST", int status = 200,
        string grant = "webauthn", string type = "Bearer", bool credentials = false) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        pairing = pairing ?? Pairing, url = url ?? "https://api.production.wealthsimple.com/v1/oauth/v2/token", method, status,
        request = credentials ? (object)new { grant_type = grant, client_id = "synthetic-client", password = "synthetic-secret" }
            : new { grant_type = grant, client_id = "synthetic-client" },
        response = new { access_token = "synthetic-access", refresh_token = "synthetic-refresh", token_type = type,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), expires_in = 1800 }, deviceId = "synthetic-device", sessionId = "synthetic-session"
    });

    [Fact]
    public void CompletedPasskeyLoginCanBeParsedWithoutNetworkAccess()
    {
        using var candidate = WealthsimpleEdgeHandoff.Parse(Message(), Pairing);
        candidate.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://attacker.invalid/v1/oauth/v2/token", "POST", 200)]
    [InlineData("https://api.production.wealthsimple.com/v1/oauth/v2/token?extra=1", "POST", 200)]
    [InlineData("https://api.production.wealthsimple.com/v1/oauth/v2/token", "GET", 200)]
    [InlineData("https://api.production.wealthsimple.com/v1/oauth/v2/token", "POST", 401)]
    public void OnlyExactSuccessfulTokenExchangeIsAccepted(string url, string method, int status) =>
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Message(url: url, method: method, status: status), Pairing));

    [Fact]
    public void WrongPairingAndCredentialMetadataAreRejected()
    {
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Message(pairing: new('b', 64)), Pairing));
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Message(credentials: true), Pairing));
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Message(type: "DPoP"), Pairing));
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Message(grant: "client_credentials"), Pairing));
    }

    [Fact]
    public void MalformedTransferDoesNotExposePayloadInException()
    {
        var error = Should.Throw<InvalidDataException>(() => WealthsimpleEdgeHandoff.Parse(Encoding.UTF8.GetBytes("{\"synthetic-secret\""), Pairing));
        error.ToString().ShouldNotContain("synthetic-secret");
        error.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(131073)]
    public async Task InvalidFrameSizeIsRejectedBeforeReadingBody(int length)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(length));
        await Should.ThrowAsync<InvalidDataException>(() => WealthsimpleEdgeBridgeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedFrameIsRejected()
    {
        using var stream = new MemoryStream(new byte[] { 8, 0, 0, 0, 1, 2 });
        await Should.ThrowAsync<EndOfStreamException>(() => WealthsimpleEdgeBridgeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData("../other")] [InlineData("abc")] [InlineData("")]
    public void PairingCannotChooseAnArbitraryPipe(string value) =>
        Should.Throw<InvalidDataException>(() => WealthsimpleEdgeBridgeProtocol.PipeName(value));

    [Fact]
    public async Task PairedLocalHandoffWaitsForNativeVerificationReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handoff = new WealthsimpleEdgeHandoff();
        var invoked = false;
        var receiving = handoff.ReceiveAsync(candidate =>
        {
            candidate.Dispose(); invoked = true;
            return Task.FromResult(false); // A captured token alone must not report Connected.
        }, timeout.Token);
        using var sender = new NamedPipeClientStream(".", WealthsimpleEdgeBridgeProtocol.PipeName(handoff.PairingId),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await sender.ConnectAsync(timeout.Token);
        await WealthsimpleEdgeBridgeProtocol.WriteAsync(sender, Message(pairing: handoff.PairingId), timeout.Token);
        var receipt = await WealthsimpleEdgeBridgeProtocol.ReadAsync(sender, timeout.Token);
        using var result = JsonDocument.Parse(receipt);
        result.RootElement.GetProperty("connected").GetBoolean().ShouldBeFalse();
        (await receiving).ShouldBeFalse(); invoked.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelledAttemptDoesNotInvokeVerification()
    {
        using var handoff = new WealthsimpleEdgeHandoff();
        using var cancellation = new CancellationTokenSource();
        var receiving = handoff.ReceiveAsync(_ => throw new Exception("Must not connect"), cancellation.Token);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => receiving);
    }
}
