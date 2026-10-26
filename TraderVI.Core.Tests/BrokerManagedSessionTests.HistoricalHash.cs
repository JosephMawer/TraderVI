#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task PackedArchiveEscapingAndHistoricalMemoryOrderRetainExactApprovalHash()
    {
        var fixture = ContinuedFixture();
        var prior = fixture.Close;
        var next = await fixture.Workflow().ApproveAsync(fixture.Approval());
        string typedPrior = JsonSerializer.Serialize(prior);
        string archivedPrior = JsonNode.Parse(typedPrior)!.ToJsonString();
        using var nextDocument = JsonDocument.Parse(JsonNode.Parse(JsonSerializer.Serialize(next))!.ToJsonString());
        string memory = nextDocument.RootElement.GetProperty("Memory").GetRawText();
        string reordered = "{" + string.Join(",", nextDocument.RootElement.GetProperty("Memory").EnumerateObject().Reverse()
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText())) + "}";
        string archivedNext = nextDocument.RootElement.GetRawText().Replace(memory, reordered, StringComparison.Ordinal);
        string originalRepresentation = BrokerHistoricalStateHash.RestoreMemoryRepresentation(typedPrior, archivedNext);
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalRepresentation)));
        Assert.NotEqual(typedPrior, archivedPrior); // Archive JSON escapes timestamp '+' characters.
        Assert.NotEqual(BrokerAccountImport.Hash(prior), expected);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            BrokerHistoricalStateHash.RestoreMemoryRepresentation(archivedPrior, archivedNext)))), expected);

        var evidence = BrokerHistoricalStateHash.Create(prior, archivedPrior, next, archivedNext);
        var historicalApproval = fixture.Continuation with { PreviousSessionHash = expected };
        historicalApproval.RequirePrior(prior, fixture.F.Store.Calendar!, evidence);
        Assert.Throws<InvalidOperationException>(() => historicalApproval.RequirePrior(prior, fixture.F.Store.Calendar!));
        Assert.False(BrokerHistoricalStateHash.Matches(prior, new string('A', 64), evidence));
        Assert.False(BrokerHistoricalStateHash.Matches(prior with { Memory = prior.Memory with { DailyLossHold = true } }, expected, evidence));
        Assert.Equal(typedPrior, JsonSerializer.Serialize(prior));
        Assert.Equal(0, fixture.F.Transport.Sent);
        Assert.Null(fixture.Grants.Value);
    }

    [Fact]
    public async Task HistoricalRepresentationProvesExactOldHashWithoutChangingCurrentApprovalValidation()
    {
        var fixture = ContinuedFixture();
        var prior = fixture.Close;
        var next = await fixture.Workflow().ApproveAsync(fixture.Approval());
        string a = JsonSerializer.Serialize(prior), b = JsonSerializer.Serialize(next);
        using var document = JsonDocument.Parse(a);
        string memory = document.RootElement.GetProperty("Memory").GetRawText();
        string reversed = "{" + string.Join(",", document.RootElement.GetProperty("Memory").EnumerateObject().Reverse()
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText())) + "}";
        string originalRepresentation = a.Replace(memory, reversed, StringComparison.Ordinal);
        string savedNext = b.Replace(memory, reversed, StringComparison.Ordinal);
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalRepresentation)));
        Assert.NotEqual(BrokerAccountImport.Hash(prior), expected);
        var evidence = BrokerHistoricalStateHash.Create(prior, a, next, savedNext);
        var historicalApproval = fixture.Continuation with { PreviousSessionHash = expected };
        historicalApproval.RequirePrior(prior, fixture.F.Store.Calendar!, evidence);
        Assert.Throws<InvalidOperationException>(() => historicalApproval.RequirePrior(prior, fixture.F.Store.Calendar!));
        Assert.False(BrokerHistoricalStateHash.Matches(prior, new string('A', 64), evidence));
        Assert.False(BrokerHistoricalStateHash.Matches(prior with { RevisionId = Guid.NewGuid() }, expected, evidence));
        Assert.False(BrokerHistoricalStateHash.Matches(prior with { Memory = prior.Memory with { DailyLossHold = true } }, expected, evidence));
        Assert.Equal(a, JsonSerializer.Serialize(prior));
        Assert.Equal(0, fixture.F.Transport.Sent);
        Assert.Null(fixture.Grants.Value);
    }

    [Fact]
    public async Task HistoricalProofRejectsMismatchedRecordsAndNonAdjacentIdentity()
    {
        var fixture = ContinuedFixture();
        var prior = fixture.Close;
        var next = await fixture.Workflow().ApproveAsync(fixture.Approval());
        string a = JsonSerializer.Serialize(prior), b = JsonSerializer.Serialize(next);
        Assert.Throws<InvalidDataException>(() => BrokerHistoricalStateHash.Create(prior, a,
            next with { PreviousRevisionId = Guid.NewGuid() }, b));
        Assert.Throws<InvalidDataException>(() => BrokerHistoricalStateHash.Create(prior with { BindingId = Guid.NewGuid() }, a, next, b));
        Assert.Throws<InvalidDataException>(() => BrokerHistoricalStateHash.Create(prior, a.Replace("\"DailyLossHold\":false", "\"DailyLossHold\":true"), next, b));
        Assert.Throws<InvalidDataException>(() => BrokerHistoricalStateHash.Create(prior, a, next, b[..^1] + ",\"UnknownAuthority\":true}"));
    }

    [Fact]
    public void WitnessSuppliesOnlyDictionaryOrderAndNeverReplacesValuesOrArrayPositions()
    {
        const string original = "{\"Keep\":1.00,\"Memory\":{\"Symbols\":{\"A\":{\"RawValues\":{\"High\":12.50,\"Low\":10}},\"B\":null},\"Sequence\":[1,2],\"Hold\":true}}";
        const string witness = "{\"Memory\":{\"Symbols\":{\"B\":{},\"A\":{\"RawValues\":{\"Low\":999,\"High\":999,\"Extra\":999}}},\"Sequence\":[9,8],\"Hold\":false}}";
        const string expected = "{\"Keep\":1.00,\"Memory\":{\"Symbols\":{\"B\":null,\"A\":{\"RawValues\":{\"Low\":10,\"High\":12.50}}},\"Sequence\":[1,2],\"Hold\":true}}";
        Assert.Equal(expected, BrokerHistoricalStateHash.RestoreDictionaryOrder(original, witness));
        using var a = JsonDocument.Parse(original);
        using var b = JsonDocument.Parse(BrokerHistoricalStateHash.RestoreMemoryRepresentation(original, witness));
        Assert.True(JsonElement.DeepEquals(a.RootElement, b.RootElement));
        Assert.Throws<InvalidDataException>(() => BrokerHistoricalStateHash.RestoreDictionaryOrder(original,
            "{\"Memory\":{\"Duplicate\":1,\"Duplicate\":2}}"));
    }
}
