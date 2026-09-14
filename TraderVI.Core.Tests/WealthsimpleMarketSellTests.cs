using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fixture = TraderVI.Core.Tests.WealthsimpleMarketBuyTests.Fixture;

namespace TraderVI.Core.Tests;

public class WealthsimpleMarketSellTests
{
    [Fact]
    public async Task ManualSellQuoteUsesSellabilityAndRecoveryRequiresPersistedIdentity()
    {
        using var f = new Fixture { Buyable = false }; var order = await f.PrepareSell();
        var quote = await f.Client.ReadManualSellQuoteAsync(order.Security);
        Assert.Equal(WealthsimpleReadStatus.Success, quote.Status); Assert.Equal(10.24m, quote.Data.Bid);
        f.Sellable = false;
        Assert.Equal(WealthsimpleReadStatus.Unavailable,(await f.Client.ReadManualSellQuoteAsync(order.Security)).Status);
        var intent = new ManualSellIntent(order.RequestId,"wrong-identity","owned-tfsa","cad-security","TFSA","FIX.TO","XTSE",20,
            quote.Data.RetrievedAt,quote.Data,quote.Data.RetrievedAt);
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable,(await f.Client.ReadManualSellOrderAsync(intent)).Status);
        Assert.Equal(0,f.DetailReads);
        Assert.Equal(WealthsimpleReadStatus.Success,(await f.Client.ReadManualSellOrderAsync(intent with { IdentityId="identity-fixture" })).Status);
        Assert.Equal(0,f.Mutations);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuyAndSellMatchTheIndependentHarContractAndReadSeparateFills(bool sell)
    {
        using var fixtureStream = typeof(WealthsimpleMarketSellTests).Assembly.GetManifestResourceStream(
            "TraderVI.Core.Tests.Fixtures.Wealthsimple.market-" + (sell ? "sell" : "buy") + ".json");
        using var capture = JsonDocument.Parse(fixtureStream);
        using var f = new Fixture(); var prepared = await f.Prepare();
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        f.CapturedAcknowledgement = capture.RootElement.GetProperty("acknowledgement");
        f.CapturedDetail = capture.RootElement.GetProperty("detail");
        if (sell)
        {
            var order = new WealthsimpleMarketSell(prepared.Account, prepared.Security, 20, id);
            var ack = await f.Client.SubmitMarketSellAsync(order, f.Journal);
            Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, ack.Status);
            Assert.Equal("broker-ack-fixture", ack.BrokerOrderId); Assert.Equal(0, f.DetailReads);
            var fill = await f.Client.ReadMarketSellOrderAsync(order);
            Assert.Equal(WealthsimpleReadStatus.Success, fill.Status); Assert.True(fill.Data.IsFilled);
            Assert.Equal(20m, fill.Data.FilledQuantity); Assert.Equal(10.25m, fill.Data.AverageFillPrice.Amount);
            Assert.Equal(205m, fill.Data.FilledNetValue.Amount); Assert.Equal(0m, fill.Data.Fees.Amount);
            Assert.DoesNotContain("owned-tfsa", order.ToString());
            Assert.DoesNotContain("broker-ack-fixture", ack.ToString());
            Assert.DoesNotContain("205", fill.Data.ToString());
        }
        else
        {
            var order = new WealthsimpleMarketBuy(prepared.Account, prepared.Security, 20, id);
            Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
            Assert.Equal(0, f.DetailReads);
            var fill = await f.Client.ReadMarketBuyOrderAsync(order);
            Assert.Equal(WealthsimpleReadStatus.Success, fill.Status); Assert.True(fill.Data.IsFilled);
            Assert.Equal(20m, fill.Data.FilledQuantity); Assert.Equal(10.25m, fill.Data.AverageFillPrice.Amount);
            Assert.Equal(205m, fill.Data.FilledNetValue.Amount);
        }
        var expected = capture.RootElement.GetProperty("request"); var actual = f.LastMutation.RootElement;
        Assert.Equal(expected.GetProperty("operationName").GetString(), actual.GetProperty("operationName").GetString());
        Assert.Equal(Compact(expected.GetProperty("query").GetString()), Compact(actual.GetProperty("query").GetString()));
        var expectedInput = expected.GetProperty("variables").GetProperty("input");
        var actualInput = actual.GetProperty("variables").GetProperty("input");
        Assert.Equal(expectedInput.EnumerateObject().Count(), actualInput.EnumerateObject().Count());
        foreach (var property in expectedInput.EnumerateObject())
            Assert.Equal(property.Value.ToString(), actualInput.GetProperty(property.Name).ToString());
        Assert.Equal(1, f.Mutations); Assert.Equal(1, f.DetailReads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task SellAcceptsWholeSharesWithoutTenShareRounding(int quantity)
    {
        using var f = new Fixture(); var order = await f.PrepareSell(quantity);
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(quantity, f.LastMutation.RootElement.GetProperty("variables").GetProperty("input").GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.125)]
    [InlineData(20.5)]
    [InlineData(2147483648)]
    public void InvalidSellQuantityIsRejectedBeforeTransport(decimal quantity) =>
        Assert.Throws<ArgumentException>(() => new WealthsimpleMarketSell(new("owned", "TFSA"), new("security", "FIX.TO", "XTSE"), quantity, Guid.NewGuid()));

    [Fact]
    public void SellRequiresPersistentRequestIdentity() => Assert.Throws<ArgumentException>(() =>
        new WealthsimpleMarketSell(new("owned", "TFSA"), new("security", "FIX.TO", "XTSE"), 1, Guid.Empty));

    [Fact]
    public async Task SellChecksSellabilityIndependentlyOfBuyability()
    {
        using var f = new Fixture { Buyable = false, Sellable = false }; var order = await f.PrepareSell();
        Assert.Equal(WealthsimpleSubmissionStatus.SecurityUnavailable, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.False(Directory.Exists(f.JournalPath)); Assert.Equal(0, f.Mutations);
        f.Sellable = true;
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
    }

    [Theory]
    [InlineData("currency", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("inactive", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("type", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("ineligible", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("no-market", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("venue", WealthsimpleSubmissionStatus.SecurityUnavailable)]
    [InlineData("security", WealthsimpleSubmissionStatus.PreflightUnavailable)]
    [InlineData("missing-permission", WealthsimpleSubmissionStatus.PreflightUnavailable)]
    [InlineData("timeout", WealthsimpleSubmissionStatus.PreflightUnavailable)]
    public async Task UnverifiedEligibilityPreventsSellWithoutConsumingIntent(string failure, WealthsimpleSubmissionStatus expected)
    {
        using var f = new Fixture(); var order = await f.PrepareSell(); f.EligibilityFailure = failure;
        Assert.Equal(expected, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.False(Directory.Exists(f.JournalPath)); Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task ForeignAccountOrInstrumentCannotSubmitOrReadAnotherAccountsSell()
    {
        using var f = new Fixture(); var order = await f.PrepareSell();
        var foreign = new WealthsimpleMarketSell(new("foreign", "TFSA"), order.Security, 20, Guid.NewGuid());
        Assert.Equal(WealthsimpleSubmissionStatus.AccountUnavailable, (await f.Client.SubmitMarketSellAsync(foreign, f.Journal)).Status);
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable, (await f.Client.ReadMarketSellOrderAsync(foreign)).Status);
        Assert.Equal(WealthsimpleSubmissionStatus.SecurityUnavailable, (await f.Client.SubmitMarketSellAsync(
            new(order.Account, new("cad-security", "FIX.TO", "XTSE"), 20, Guid.NewGuid()), f.Journal)).Status);
        Assert.Equal(0, f.Mutations); Assert.Equal(0, f.DetailReads);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("http500")]
    [InlineData("http429")]
    [InlineData("malformed")]
    [InlineData("graphql-partial")]
    [InlineData("nonce")]
    public async Task AmbiguousSellRemainsFencedAcrossJournalRecreationAndMissingLookup(string failure)
    {
        using var f = new Fixture { Dpop = true }; var order = await f.PrepareSell(); f.MutationFailure = failure;
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        f.DetailFailure = "not-found";
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, (await f.Client.ReadMarketSellOrderAsync(order)).Status);
        f.MutationFailure = null;
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketSellAsync(order, new WealthsimpleOrderAttemptJournal(f.JournalPath))).Status);
        Assert.Equal(1, f.Mutations);
        var marker = File.ReadAllText(Assert.Single(Directory.GetFiles(f.JournalPath)));
        Assert.Matches("^TraderVI.OrderAttempt.v1\\n[0-9a-f]{64}\\n$", marker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingSideAndQuantityCannotBypassAnExistingAttempt(bool sellFirst)
    {
        using var f = new Fixture(); var buy = await f.Prepare(); var sell = new WealthsimpleMarketSell(buy.Account, buy.Security, 1, buy.RequestId);
        if (sellFirst)
        {
            Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketSellAsync(sell, f.Journal)).Status);
            Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketBuyAsync(buy, f.Journal)).Status);
        }
        else
        {
            Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketBuyAsync(buy, f.Journal)).Status);
            Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketSellAsync(sell, f.Journal)).Status);
        }
        Assert.Equal(1, f.Mutations);
    }

    [Theory]
    [InlineData("reject", WealthsimpleSubmissionStatus.Rejected)]
    [InlineData("mixed", WealthsimpleSubmissionStatus.OutcomeUnknown)]
    public async Task RejectionAndAmbiguousAcknowledgementStayDistinctAndFenced(string failure, WealthsimpleSubmissionStatus expected)
    {
        using var f = new Fixture(); var order = await f.PrepareSell(); f.MutationFailure = failure;
        Assert.Equal(expected, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(1, f.Mutations);
    }

    [Theory]
    [InlineData("canonicalAccountId", "foreign")]
    [InlineData("identityId", "foreign")]
    [InlineData("securityId", "foreign")]
    [InlineData("id", "foreign")]
    [InlineData("side", "BUY")]
    [InlineData("orderType", "BUY_QUANTITY")]
    [InlineData("executionType", "LIMIT")]
    [InlineData("timeInForce", "GTC")]
    [InlineData("securityCurrency", "USD")]
    [InlineData("submittedQuantity", "21")]
    [InlineData("fillQuantity", "19")]
    [InlineData("fillQuantity", "21")]
    [InlineData("fillQuantity", "-1")]
    [InlineData("averageFillPrice", null)]
    [InlineData("averageFillPrice", "0")]
    [InlineData("lastFilledAtUtc", null)]
    public async Task ConflictingOrIncompleteSellDetailsNeverConfirmAFill(string field, string value)
    {
        using var f = new Fixture(); var order = await f.PrepareSell(); f.DetailOverrides[field] = value;
        var result = await f.Client.ReadMarketSellOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data); Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task PartialSellKeepsCumulativeQuantityAndActualNetProceedsIncludingFees()
    {
        using var f = new Fixture(); var order = await f.PrepareSell();
        f.DetailOverrides["status"] = "PARTIALLY_FILLED"; f.DetailOverrides["fillQuantity"] = "3.5";
        f.DetailOverrides["filledValue"] = "35.875"; f.DetailOverrides["filledNetValue"] = "35.625";
        f.DetailOverrides["filledTotalFee"] = "0.25";
        var first = await f.Client.ReadMarketSellOrderAsync(order); var again = await f.Client.ReadMarketSellOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.Success, first.Status); Assert.False(first.Data.IsFilled);
        Assert.Equal(3.5m, first.Data.FilledQuantity); Assert.Equal(first.Data, again.Data);
        Assert.Equal(35.625m, first.Data.FilledNetValue.Amount); Assert.Equal(0.25m, first.Data.Fees.Amount);
        Assert.Equal("CAD", first.Data.FilledNetValue.Currency); Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task UnknownSellFillRemainsUnknown()
    {
        using var f = new Fixture(); var order = await f.PrepareSell(); f.DetailFailure = "pending-unknown";
        var result = await f.Client.ReadMarketSellOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Null(result.Data.FilledQuantity); Assert.False(result.Data.IsFilled);
    }

    [Fact]
    public async Task JournalFailureOrCancellationBeforePreflightPreventsTransmission()
    {
        using var f = new Fixture(); var order = await f.PrepareSell();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Client.SubmitMarketSellAsync(order, f.Journal, cancellation.Token));
        Assert.False(Directory.Exists(f.JournalPath));
        Directory.CreateDirectory(f.RootPath); File.WriteAllText(f.JournalPath, "not a directory");
        Assert.Equal(WealthsimpleSubmissionStatus.JournalUnavailable, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task ConcurrentBuyAndSellReservationWithSameIdHasOnlyOneWinner()
    {
        using var f = new Fixture(); var buy = await f.Prepare();
        var sell = new WealthsimpleMarketSell(buy.Account, buy.Security, 1, buy.RequestId);
        var results = await Task.WhenAll(Task.Run(() => f.Journal.TryBegin("identity-fixture", buy)),
            Task.Run(() => f.Journal.TryBegin("identity-fixture", sell)));
        Assert.Single(results.Where(value => value)); Assert.Equal(0, f.Mutations);
        // Even an interrupted write must keep the shared attempt blocked after restart.
        File.WriteAllText(Assert.Single(Directory.GetFiles(f.JournalPath)), "");
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketSellAsync(sell, f.Journal)).Status);
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketBuyAsync(buy, f.Journal)).Status);
    }

    [Fact]
    public async Task UnauthorizedSellClearsSessionAndNeverRetriesMutation()
    {
        using var f = new Fixture(); var order = await f.PrepareSell(); f.MutationFailure = "http401";
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(WealthsimpleSubmissionStatus.SessionRequired, (await f.Client.SubmitMarketSellAsync(order, f.Journal)).Status);
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, (await f.Client.ReadMarketSellOrderAsync(order)).Status);
        Assert.Single(Directory.GetFiles(f.JournalPath)); Assert.Equal(1, f.Mutations);
    }

    private static string Compact(string value) => Regex.Replace(value, @"\s+", "");
}
