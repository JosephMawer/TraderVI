using Core.Runtime;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineVerificationTests
{
    [Theory]
    [InlineData(EngineOperation.SubmitBuy)]
    [InlineData(EngineOperation.SellAll)]
    [InlineData(EngineOperation.EnableExecution)]
    [InlineData(EngineOperation.SignIn)]
    [InlineData(EngineOperation.ConnectProtectedSession)]
    [InlineData(EngineOperation.AccountSnapshot)]
    [InlineData((EngineOperation)999)]
    public void VerificationRejectsOrdersActivationAndExternalAccess(EngineOperation operation) =>
        Assert.Throws<InvalidOperationException>(() => EngineVerification.RequireLocalBrokerRead(operation));

    [Fact]
    public void LocalStatusAndExplicitDisableRemainAvailable()
    {
        EngineVerification.RequireLocalBrokerRead(EngineOperation.Connection);
        EngineVerification.RequireLocalBrokerRead(EngineOperation.ExecutionStatus);
        EngineVerification.RequireLocalBrokerRead(EngineOperation.DisableExecution);
    }

    [Fact]
    public async Task MaintenanceClientRejectsFinancialCommandsBeforeCreatingAReceiptOrConnecting()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await Assert.ThrowsAsync<ArgumentException>(() => EngineControlCommand.RunAsync("EnableExecution", path));
        await Assert.ThrowsAsync<ArgumentException>(() => EngineControlCommand.RunAsync("ClearIntegrityHolds", path));
        await Assert.ThrowsAsync<ArgumentException>(() => EngineControlCommand.RunAsync("ResumePaperPendingRecovery", path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TemporaryPaperCommandUsesExplicitTypedRequestAndCannotReplayItsReceipt()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        string input = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var request = new EngineResumePaperPendingRecovery(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], DateTime.UtcNow.AddHours(1), "Paper only");
        var client = new PaperClient(request);
        try
        {
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(request));
            await EngineControlCommand.RunAsync("ResumePaperPendingRecovery", output, input, client);
            Assert.Equal(1, client.Calls);
            await Assert.ThrowsAsync<IOException>(() => EngineControlCommand.RunAsync("ResumePaperPendingRecovery", output, input, client));
            Assert.Equal(1, client.Calls);
            Assert.Throws<InvalidOperationException>(() => EngineRecoveryMaintenance.RequireOperation(EngineOperation.ResumePaperPendingRecovery));
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    private sealed class PaperClient(EngineResumePaperPendingRecovery expected) : IEngineRpc
    {
        public int Calls;
        public Task<T> CallAsync<T>(EngineOperation operation, object payload = null, CancellationToken ct = default)
        {
            Assert.Equal(EngineOperation.ResumePaperPendingRecovery, operation);
            var request = Assert.IsType<EngineResumePaperPendingRecovery>(payload);
            Assert.Equal(expected.RecoveryRunId, request.RecoveryRunId);
            Assert.Equal<Guid>(expected.Incidents, request.Incidents);
            Assert.Equal(expected.ExpiresUtc, request.ExpiresUtc);
            Calls++;
            return Task.FromResult((T)(object)JsonSerializer.SerializeToElement(true));
        }
    }

    [Theory]
    [InlineData("Status")][InlineData("Connection")][InlineData("ExecutionStatus")]
    public async Task ReceiptIsReservedBeforeDispatchAndCannotReplayACommand(string operation)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var client = new LocalClient { Expected = Enum.Parse<EngineOperation>(operation) };
        try
        {
            await EngineControlCommand.RunAsync(operation, path, null, client);
            Assert.Equal(1, client.Calls);
            Assert.True(JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.GetBoolean());
            await Assert.ThrowsAsync<IOException>(() => EngineControlCommand.RunAsync("Stop", path, null, client));
            Assert.Equal(1, client.Calls);
        }
        finally { File.Delete(path); }
    }

    private sealed class LocalClient : IEngineRpc
    {
        public int Calls;
        public EngineOperation Expected;
        public Task<T> CallAsync<T>(EngineOperation operation, object payload = null, CancellationToken ct = default)
        {
            Assert.Equal(Expected, operation); Assert.Null(payload); Calls++;
            return Task.FromResult((T)(object)JsonSerializer.SerializeToElement(true));
        }
    }
}
