#nullable enable
using Core.TMX;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxIntradayTimestampInspectionTests
{
    private static readonly DateTime Start = new(2026, 10, 9, 17, 50, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-09 1:54:00 PM", 54, false)]
    [InlineData("2026-10-09 1:50:00 PM", 50, true)]
    public async Task InspectsOriginalAndProductionMappedTimeWithoutBypassingValidation(string source, int minute, bool valid)
    {
        using var handler = new Handler(Response(source));
        using var client = new TmxClient(handler);
        TmxIntradayTimestampInspection? sample = null;
        var read = client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 5, Start, Start.AddMinutes(5),
            inspectTimestamps: x => sample = x);
        if (valid) Assert.Single((await read).Batch.Bars);
        else
        {
            var result = await read;
            Assert.Empty(result.Batch.Bars);
            Assert.Single(result.Batch.ExcludedSupplementaryTimestampsUtc);
        }
        Assert.NotNull(sample);
        Assert.Equal(1, sample!.SourcePointCount);
        Assert.Equal(1, sample.AttemptCount);
        var point = Assert.Single(sample.Points);
        Assert.Equal(source, point.SourceText);
        Assert.Equal(new DateTime(2026, 10, 9, 17, minute, 0, DateTimeKind.Utc), point.MappedUtc);
        Assert.True(point.Complete);
        Assert.Null(point.MappingError);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("987.654", sample.ToString());
    }

    [Fact]
    public async Task OrdinaryCallerCannotUseSupplementaryPointAsAnAlignedBar()
    {
        using var handler = new Handler(Response("2026-10-09 1:52:00 PM"));
        using var client = new TmxClient(handler);
        var batch = await client.GetIntradayTimeSeriesBatchAsync("TEST", 5, Start, Start.AddMinutes(5));
        Assert.Empty(batch.Bars);
        Assert.Single(batch.ExcludedSupplementaryTimestampsUtc);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MalformedTextIsRedactedAndStillFailsMapping()
    {
        using var client = new TmxClient(new Handler(Response("SECRET-provider-message")));
        TmxIntradayTimestampInspection? sample = null;
        await Assert.ThrowsAsync<FormatException>(() => client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 5,
            Start, Start.AddMinutes(5), inspectTimestamps: x => sample = x));
        var point = Assert.Single(sample!.Points);
        Assert.Equal("[unrecognized timestamp redacted]", point.SourceText);
        Assert.Null(point.MappedUtc);
        Assert.Equal("FormatException", point.MappingError);
        Assert.DoesNotContain("SECRET", sample.ToString());
    }

    [Fact]
    public async Task IncompleteRowsCannotBecomeMappedBars()
    {
        using var client = new TmxClient(new Handler("""{"data":{"getTimeSeriesData":[{"dateTime":"2026-10-09 1:54:00 PM","open":null}]}}"""));
        TmxIntradayTimestampInspection? sample = null;
        var result = await client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 5, Start, Start.AddMinutes(5), inspectTimestamps: x => sample = x);
        Assert.Empty(result.Batch.Bars);
        Assert.False(Assert.Single(sample!.Points).Complete);
        Assert.Null(sample.Points[0].MappedUtc);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("2026-10-09 1:54:00 PM\n")]
    [InlineData("2026-10-09 1:54:00 PM password")]
    public void OnlyTimestampSyntaxCanBePrinted(string value) =>
        Assert.Equal("[unrecognized timestamp redacted]", TmxIntradayTimestampInspection.SafeSourceText(value));

    [Fact]
    public void InspectionIsBoundedAndDoesNotModifySource()
    {
        var points = new global::Core.TMX.Models.Dto.TmxTimeSeriesPointDto[100];
        for (int i = 0; i < points.Length; i++) points[i] = new() { dateTime = "2026-10-09 1:54:00 PM" };
        var result = TmxIntradayTimestampInspection.Create(points, 2, Start);
        Assert.Equal(100, result.SourcePointCount);
        Assert.Equal(32, result.Points.Length);
        points[0].dateTime = "changed";
        Assert.Equal("2026-10-09 1:54:00 PM", result.Points[0].SourceText);
    }

    private static string Response(string source) =>
        "{\"data\":{\"getTimeSeriesData\":[{\"dateTime\":\"" + source + "\",\"open\":987.654,\"high\":987.654,\"low\":987.654,\"close\":987.654,\"volume\":1}]}}";

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("\"interval\":5", body);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
