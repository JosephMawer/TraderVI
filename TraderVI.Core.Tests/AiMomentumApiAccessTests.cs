#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle.Llm;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiMomentumApiAccessTests
{
    [Theory]
    [InlineData(200, "{\"id\":\"gpt-6-astra\",\"object\":\"model\"}", true)]
    [InlineData(200, "{\"id\":\"other-model\",\"object\":\"model\"}", false)]
    [InlineData(200, "{\"id\":\"gpt-6-astra\",\"object\":\"response\"}", false)]
    [InlineData(401, "sensitive provider error", false)]
    [InlineData(403, "sensitive provider error", false)]
    [InlineData(404, "sensitive provider error", false)]
    [InlineData(429, "sensitive provider error", false)]
    [InlineData(302, "redirect", false)]
    public async Task OneMetadataReadWithExactCredentialAndNoRetry(int status, string body, bool visible)
    {
        using var handler = new Handler(status, body);
        using var http = new HttpClient(handler);
        var result = await AiMomentumApiAccess.CheckAsync(Configuration(), http);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(status, result.HttpStatus);
        Assert.Equal(visible, result.RequestedModelVisible);
        Assert.DoesNotContain("sensitive", result.ToString());
        Assert.DoesNotContain("synthetic-key", result.ToString());
    }

    [Fact]
    public async Task CancellationBeforeAdmissionMakesNoRequest()
    {
        using var handler = new Handler(200, "{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AiMomentumApiAccess.CheckAsync(Configuration(), http, new CancellationToken(true)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizeMetadataIsRejected()
    {
        using var handler = new Handler(200, new string('x', 65_537));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => AiMomentumApiAccess.CheckAsync(Configuration(), http));
        Assert.Equal(1, handler.Calls);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Oracle:OpenAi:ApiKey"] = "synthetic-key" }).Build();

    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.openai.com/v1/models/gpt-6-astra", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-key", request.Headers.Authorization.Parameter);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
