#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle.Llm;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiMomentumClientTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-ai-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);
    private static AiSpendPolicy Policy(decimal limit = 90m) => new(1.6m, 1.2m, Now.AddDays(40), limit, 20_000, 1_000);
    private AiCallJournal NewJournal() => new(directory, initializeNew: true);

    [Fact]
    public async Task CountOnlyUsesOneCountRequestWithoutCreatingAnyJournal()
    {
        int calls = 0;
        using var http = new HttpClient(new FakeHandler((message, number) =>
        {
            calls++;
            Assert.EndsWith("/responses/input_tokens", message.RequestUri!.AbsolutePath);
            return Task.FromResult(Json("{\"input_tokens\":29516}"));
        }));
        Assert.Equal(29516, await OpenAiResponsesLlmClient.CountInputAsync("test-key", Request(), http));
        Assert.Equal(1, calls);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"input_tokens\":0}")]
    public async Task CountOnlyRejectsMissingOrInvalidCounts(string json)
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Json(json))));
        await Assert.ThrowsAsync<InvalidDataException>(() => OpenAiResponsesLlmClient.CountInputAsync("test-key", Request(), http));
    }

    [Fact]
    public async Task ReservesBeforeTransmissionAndPersistsExactRequestResponseAndReasoningUsage()
    {
        using var journal = NewJournal();
        var request = Request();
        var handler = new FakeHandler(async (message, number) =>
        {
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            string body = await message.Content!.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(body);
            Assert.Equal("gpt-6-astra", parsed.RootElement.GetProperty("model").GetString());
            Assert.False(parsed.RootElement.TryGetProperty("temperature", out _));
            Assert.Equal("standard", parsed.RootElement.GetProperty("reasoning").GetProperty("mode").GetString());
            Assert.Equal("medium", parsed.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
            if (number == 1)
            {
                Assert.EndsWith("/responses/input_tokens", message.RequestUri!.AbsolutePath);
                Assert.Empty(journal.ReadAll());
                return Json("{\"input_tokens\":1000}");
            }
            var saved = Assert.Single(journal.ReadAll());
            Assert.Equal(body, saved.RequestJson);
            Assert.Null(saved.ChargedCad);
            Assert.Equal(Policy().CostCad(1000, 1000), saved.ReservedCad);
            Assert.False(parsed.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("default", parsed.RootElement.GetProperty("service_tier").GetString());
            Assert.Equal("disabled", parsed.RootElement.GetProperty("truncation").GetString());
            Assert.True(parsed.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            Assert.False(parsed.RootElement.TryGetProperty("tools", out _));
            return Json(Response(outputTokens: 200));
        });
        using var http = new HttpClient(handler);
        using var client = new OpenAiResponsesLlmClient("synthetic-key", journal, Policy(), http, new Clock(Now));
        var result = await client.CompleteAsync(request);
        Assert.Equal("{\"ok\":true}", result.Text);
        Assert.Equal(200, result.Usage.OutputTokens);
        var record = Assert.Single(journal.ReadAll());
        Assert.Equal(Policy().CostCad(1000, 200), record.ChargedCad);
        Assert.Equal("ModelCompleted", record.Outcome);
        Assert.NotNull(record.ResponseJson);
        Assert.DoesNotContain("synthetic-key", File.ReadAllText(Path.Combine(directory, "ai-calls.json")));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(request));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task AggregateMonthlyLimitBlocksBeforePaidRequest()
    {
        using var journal = NewJournal();
        var policy = Policy(0.1m);
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("{\"input_tokens\":1000}")));
        using var http = new HttpClient(handler);
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, policy, http, new Clock(Now));
        await Assert.ThrowsAsync<LlmBudgetExceededException>(() => client.CompleteAsync(Request()));
        Assert.Equal(1, handler.Count);
        Assert.Empty(journal.ReadAll());
    }

    [Fact]
    public async Task LostResponseKeepsReservationAcrossRestartAndDoesNotRetry()
    {
        var request = Request();
        using (var journal = NewJournal())
        {
            var handler = new FakeHandler((_, number) => number == 1
                ? Task.FromResult(Json("{\"input_tokens\":1000}"))
                : throw new HttpRequestException("Synthetic transport failure"));
            using var http = new HttpClient(handler);
            using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
            await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(request));
            Assert.Equal(2, handler.Count);
        }
        using var reopened = new AiCallJournal(directory);
        Assert.Null(Assert.Single(reopened.ReadAll()).ChargedCad);
        Assert.Throws<InvalidOperationException>(() => reopened.CheckNewCall(request.CallId!.Value, Policy(), Now));
        Assert.Throws<LlmBudgetExceededException>(() => reopened.Reserve(Guid.NewGuid(), Policy(0.15m), Now,
            "{}", 1000, 1000));
    }

    [Fact]
    public void ReservationCarriesAcrossMonthAndPolicyCannotRefillExistingMonth()
    {
        using var journal = NewJournal();
        var id = Guid.NewGuid();
        journal.Reserve(id, Policy(0.15m), Now, "{}", 1000, 1000);
        Assert.Throws<LlmBudgetExceededException>(() => journal.Reserve(Guid.NewGuid(), Policy(), Now, "{}", 1000, 1000));
        Assert.Throws<LlmBudgetExceededException>(() => journal.Reserve(Guid.NewGuid(), Policy(0.15m), Now.AddDays(7), "{}", 1000, 1000));
        journal.RecordResponse(id, "{}");
        journal.Settle(id, 1000, 1000, "Completed", Now.AddDays(7));
        Assert.Throws<LlmBudgetExceededException>(() => journal.Reserve(Guid.NewGuid(), Policy(0.15m), Now.AddDays(7), "{}", 1000, 1000));
    }

    [Fact]
    public void ConcurrentReservationsCannotOverdrawSharedBudget()
    {
        using var journal = NewJournal();
        int admitted = 0;
        Parallel.For(0, 10, _ =>
        {
            try { journal.Reserve(Guid.NewGuid(), Policy(0.15m), Now, "{}", 1000, 1000); Interlocked.Increment(ref admitted); }
            catch (LlmBudgetExceededException) { }
        });
        Assert.Equal(1, admitted);
        Assert.Single(journal.ReadAll());
        Assert.Throws<IOException>(() => new AiCallJournal(directory));
    }

    [Fact]
    public void MissingCorruptOrReinitializedLedgerCannotResetSpending()
    {
        using (var journal = NewJournal()) journal.Reserve(Guid.NewGuid(), Policy(), Now, "{}", 1000, 1000);
        Assert.Throws<InvalidOperationException>(() => new AiCallJournal(directory, initializeNew: true));
        File.WriteAllText(Path.Combine(directory, "ai-calls.json"), "broken");
        Assert.Throws<InvalidDataException>(() => new AiCallJournal(directory));
        File.Delete(Path.Combine(directory, "ai-calls.json"));
        Assert.Throws<InvalidDataException>(() => new AiCallJournal(directory));
        Assert.Throws<InvalidOperationException>(() => new AiCallJournal(directory, initializeNew: true));
    }

    [Fact]
    public void TamperedRequestIsRejected()
    {
        using (var journal = NewJournal()) journal.Reserve(Guid.NewGuid(), Policy(), Now, "ORIGINAL", 1000, 1000);
        string path = Path.Combine(directory, "ai-calls.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("ORIGINAL", "CHANGED"));
        Assert.Throws<InvalidDataException>(() => new AiCallJournal(directory));
    }

    [Fact]
    public void InterruptedSaveBlocksSpendingAndRestartUntilReviewedRecovery()
    {
        using (var journal = NewJournal())
        {
            journal.Reserve(Guid.NewGuid(), Policy(), Now, "{}", 1000, 1000);
            File.WriteAllText(Path.Combine(directory, "ai-calls.json.pending"), "interrupted-save");
            Assert.Throws<InvalidDataException>(() => journal.CheckNewCall(Guid.NewGuid(), Policy(), Now));
        }
        Assert.Throws<InvalidDataException>(() => new AiCallJournal(directory));
    }

    [Fact]
    public void ClockCannotMoveBehindSettlementToReopenAnEarlierBudgetMonth()
    {
        using var journal = NewJournal();
        var id = Guid.NewGuid();
        journal.Reserve(id, Policy(), Now, "{}", 1000, 1000);
        journal.RecordResponse(id, "{}");
        journal.Settle(id, 1000, 200, "Completed", Now.AddDays(7));
        Assert.Throws<InvalidOperationException>(() => journal.CheckNewCall(Guid.NewGuid(), Policy(), Now));
    }

    [Fact]
    public async Task CancellationAfterReservationCannotRefundOrRetryTheCall()
    {
        using var journal = NewJournal();
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHandler((_, number) =>
        {
            if (number == 1) return Task.FromResult(Json("{\"input_tokens\":1000}"));
            Assert.Single(journal.ReadAll());
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable");
        });
        using var http = new HttpClient(handler);
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
        var request = Request();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(request, cancellation.Token));
        Assert.Null(Assert.Single(journal.ReadAll()).ChargedCad);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(request));
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("failed")]
    public async Task UnusableResponseIsChargedButNeverReturnedAsDecision(string status)
    {
        using var journal = NewJournal();
        using var http = new HttpClient(Handler(Response(status: status)));
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Request()));
        Assert.NotNull(Assert.Single(journal.ReadAll()).ChargedCad);
    }

    [Theory]
    [InlineData("{\"status\":\"completed\"}")]
    [InlineData("not json")]
    public async Task MissingUsageRetainsFullReservation(string response)
    {
        using var journal = NewJournal();
        using var http = new HttpClient(Handler(response));
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Request()));
        var saved = Assert.Single(journal.ReadAll());
        Assert.Null(saved.ChargedCad);
        Assert.Equal(response, saved.ResponseJson);
    }

    [Theory]
    [InlineData("gpt-other", "default")]
    [InlineData("gpt-6-astra", "priority")]
    public async Task UnexpectedModelOrTierBlocksFutureCalls(string model, string tier)
    {
        using var journal = NewJournal();
        using var http = new HttpClient(Handler(Response(model: model, tier: tier)));
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Request()));
        Assert.Null(Assert.Single(journal.ReadAll()).ChargedCad);
        Assert.Throws<InvalidOperationException>(() => journal.CheckNewCall(Guid.NewGuid(), Policy(), Now));
    }

    [Fact]
    public async Task UsageOverrunIsDurablyRecordedAndBlocksFurtherCalls()
    {
        using (var journal = NewJournal())
        {
            using var http = new HttpClient(Handler(Response(outputTokens: 1001)));
            using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(Request()));
            Assert.Equal("UsageOverrun", Assert.Single(journal.ReadAll()).Outcome);
        }
        using var reopened = new AiCallJournal(directory);
        Assert.Throws<InvalidOperationException>(() => reopened.CheckNewCall(Guid.NewGuid(), Policy(), Now));
    }

    [Fact]
    public async Task RefusalDoesNotBecomeAnActionAndErrorBodiesAreNotExposed()
    {
        using var journal = NewJournal();
        using (var http = new HttpClient(Handler(Response(refusal: true))))
        using (var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now)))
            await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Request()));
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("private-error-body") }));
        using var errorHttp = new HttpClient(handler);
        using var errorClient = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), errorHttp, new Clock(Now));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => errorClient.CompleteAsync(Request()));
        Assert.DoesNotContain("private-error-body", exception.ToString());
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task OversizedInputAndExpiredPolicyNeverSendPaidRequest()
    {
        using var journal = NewJournal();
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("{\"input_tokens\":20001}")));
        using var http = new HttpClient(handler);
        using var client = new OpenAiResponsesLlmClient("synthetic", journal, Policy(), http, new Clock(Now));
        var oversized = await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Request()));
        Assert.Equal(20001, oversized.Data["AiCountedInputTokens"]);
        Assert.Equal(20000, oversized.Data["AiMaximumInputTokens"]);
        Assert.Empty(journal.ReadAll());
        using var expired = new OpenAiResponsesLlmClient("synthetic", journal, Policy() with { ValidUntilUtc = Now }, http, new Clock(Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => expired.CompleteAsync(Request()));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public void FactoryIsDisabledByDefaultAndBudgetCannotExceedUserCeiling()
    {
        using var journal = NewJournal();
        Assert.Throws<InvalidOperationException>(() => LlmClientFactory.CreateAiMomentum(new ConfigurationBuilder().Build(), journal, Policy()));
        Assert.Throws<InvalidOperationException>(() => (Policy() with { MonthlyLimitCad = 100m }).Validate(Now));
        Assert.Throws<InvalidOperationException>(() => (Policy() with { MaxInputTokens = 272001 }).Validate(Now));
        Assert.Throws<InvalidOperationException>(() => (Policy() with { OutputUsdPerMillion = 0 }).Validate(Now));
    }

    private static LlmRequest Request() => new("Synthetic framework", "Synthetic packet", CallId: Guid.NewGuid(),
        OutputSchema: new LlmJsonSchema("test", JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}},\"required\":[\"ok\"],\"additionalProperties\":false}")));
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static FakeHandler Handler(string response) => new((_, n) => Task.FromResult(Json(n == 1 ? "{\"input_tokens\":1000}" : response)));
    private static string Response(int outputTokens = 200, string status = "completed", string model = "gpt-6-astra", string tier = "default", bool refusal = false) =>
        JsonSerializer.Serialize(new
        {
            id = "resp_synthetic", model, status, service_tier = tier,
            usage = new { input_tokens = 1000, output_tokens = outputTokens, output_tokens_details = new { reasoning_tokens = 150 } },
            output = new[] { new { type = "message", role = "assistant", status = "completed",
                content = new[] { new { type = refusal ? "refusal" : "output_text", text = "{\"ok\":true}" } } } }
        });
    private sealed class FakeHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct) => send(message, ++Count);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
