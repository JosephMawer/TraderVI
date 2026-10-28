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
    public async Task InvestedBuildApprovalRecoversProtectionDictionaryOrderAndTypedScalarsWithExactUtf8Hash()
    {
        var x = await RiskSyncFixture();
        var prior = await x.Workflow().SynchronizeAsync(x.Prior.BindingId);
        var result = JsonSerializer.Deserialize<global::Core.Trader.DelphiLive.DelphiLiveEvaluationResult>(
            """{"Counterfactuals":[],"RawValues":{"A":1,"B":2},"DerivedFacts":{"C":"x","D":"y"}}""")!;
        var protection = prior.Protection[0];
        prior = prior with { Protection = [protection with { State = protection.State with { LastLiveResult = result } }] };
        x.F.Store.State = prior; x.F.RunningImplementation = "repaired-hash-build";
        var current = x.Original.Existing.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        var proposal = BrokerImplementationReviewWorkflow.Prepare(prior, current, x.F.Now, x.Account, true,
            x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now);
        var next = await new BrokerImplementationReviewWorkflow(x.F.Store, x.F, x.Original.Existing.Grants,
            SelectedSessionCalendar(), new HostClock(x.F)).ApproveAsync(proposal);
        string typed = JsonSerializer.Serialize(prior);
        using var document = JsonDocument.Parse(typed);
        var values = document.RootElement.GetProperty("Protection")[0].GetProperty("State")
            .GetProperty("LastLiveResult").GetProperty("RawValues");
        string reversed = "{" + string.Join(",", values.EnumerateObject().Reverse()
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText())) + "}";
        string original = typed.Replace(values.GetRawText(), reversed, StringComparison.Ordinal);
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
        Assert.NotEqual(global::Core.Runtime.EngineStrategySettings.Hash(original), expected); // Database roots use UTF-16; approval hashes use UTF-8.
        next = next with { ImplementationReview = next.ImplementationReview! with { PreviousSessionHash = expected } };
        string nextOriginal = JsonSerializer.Serialize(next).Replace(values.GetRawText(), reversed, StringComparison.Ordinal);
        var before = global::Core.Runtime.EngineArchiveCodec.Pack(typed);
        var after = global::Core.Runtime.EngineArchiveCodec.Pack(nextOriginal);
        string a = global::Core.Runtime.EngineArchiveCodec.Restore(before.Manifest, before.Contents);
        string b = global::Core.Runtime.EngineArchiveCodec.Restore(after.Manifest, after.Contents);
        Assert.NotEqual(expected, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            BrokerHistoricalStateHash.RestoreMemoryRepresentation(typed, b)))));
        var proof = BrokerHistoricalStateHash.Create(prior, a, next, b);
        Assert.True(BrokerHistoricalStateHash.Matches(prior, expected, proof));
        Assert.False(BrokerHistoricalStateHash.Matches(prior, new string('A', 64), proof));
        BrokerManagedSessionRepository.ValidateNext(prior, next, SelectedSessionCalendar(), proof);
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(prior, next, SelectedSessionCalendar()));
        Assert.Equal(typed, JsonSerializer.Serialize(prior)); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Null(x.Original.Existing.Grants.Value);
    }

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
