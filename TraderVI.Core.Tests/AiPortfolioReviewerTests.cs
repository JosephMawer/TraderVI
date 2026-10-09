#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Oracle.Llm;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioReviewerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task CreatesStructuredProposalWithOriginalPacketIdentityAndExplainedDiscretion()
    {
        var packet = Packet();
        var fake = new FakeClient(request =>
        {
            Assert.Equal(packet.PacketId, request.CallId);
            Assert.NotNull(request.OutputSchema);
            Assert.Contains("proposal", request.SystemPrompt);
            return Serialize(Proposal(packet));
        });
        var result = await new AiPortfolioReviewer(fake, new Clock(Now)).ReviewAsync(packet);
        Assert.Equal("select", result.Action);
        Assert.Single(result.RuleOverrides);
        Assert.Equal("breadth", result.RuleOverrides[0].RuleId);
    }

    [Fact]
    public void CompactRequestExplainsDecodingAndRequiresOriginalResponseIdentities()
    {
        var packet = Packet();
        packet.Evidence[0] = packet.Evidence[0] with { Description = new string('x', 61_000) };
        var request = AiPortfolioReviewer.CreateRequest(packet);
        Assert.Contains(AiPortfolioPrompt.Format, request.SystemPrompt);
        Assert.Contains("@N", request.SystemPrompt);
        Assert.Contains("defaults", request.SystemPrompt);
        Assert.Contains("fully expanded original IDs and versions", request.SystemPrompt);
        using var document = JsonDocument.Parse(request.UserPrompt);
        Assert.Equal(AiPortfolioPrompt.Format, document.RootElement.GetProperty("format").GetString());
        Assert.Equal(packet.PacketId, request.CallId);
        Assert.NotNull(request.OutputSchema);
        // Compact references are input notation, never accepted as response evidence identities.
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(
            Serialize(Proposal(packet) with { EvidenceIds = ["@0"] }), packet));
    }

    [Fact]
    public async Task NestedEvidenceRetainsExactContentWithoutHtmlEscapeExpansion()
    {
        var packet = Packet();
        string evidence = JsonSerializer.Serialize(new { Name = "é & <quoted>", Price = 12.34m, Nested = "\"stop\"\nC:\\prices" });
        packet.Evidence[0] = packet.Evidence[0] with { Description = evidence };
        var fake = new FakeClient(request =>
        {
            var restored = JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!;
            Assert.Equal(evidence, restored.Evidence[0].Description);
            Assert.Equal(JsonSerializer.Serialize(packet), JsonSerializer.Serialize(restored));
            Assert.True(request.UserPrompt.Length < JsonSerializer.Serialize(packet, Json).Length);
            Assert.Contains("\\\"Name\\\"", request.UserPrompt);
            return Serialize(Proposal(packet));
        });
        await new AiPortfolioReviewer(fake, new Clock(Now)).ReviewAsync(packet);
    }

    [Theory]
    [InlineData("hard-rule")]
    [InlineData("unknown-rule")]
    [InlineData("missing-reason")]
    [InlineData("unknown-evidence")]
    [InlineData("wrong-version")]
    [InlineData("unknown-portfolio")]
    [InlineData("wrong-packet")]
    [InlineData("wrong-action")]
    [InlineData("duplicate-evidence")]
    public void RejectsInventedFactsHardOverridesAndInvalidIdentity(string fault)
    {
        var packet = Packet();
        var proposal = Proposal(packet);
        proposal = fault switch
        {
            "hard-rule" => proposal with { RuleOverrides = [new("stop", "Want more room")] },
            "unknown-rule" => proposal with { RuleOverrides = [new("imaginary", "Why")] },
            "missing-reason" => proposal with { RuleOverrides = [new("breadth", "")] },
            "unknown-evidence" => proposal with { EvidenceIds = ["tomorrow"] },
            "wrong-version" => proposal with { StrategyVersion = "v2" },
            "unknown-portfolio" => proposal with { PortfolioId = "other" },
            "wrong-packet" => proposal with { PacketId = Guid.NewGuid() },
            "wrong-action" => proposal with { Action = "submit_order" },
            _ => proposal with { EvidenceIds = ["today", "today"] }
        };
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Serialize(proposal), packet));
    }

    [Fact]
    public void RejectsIneligiblePortfolioAndSwitchWithoutCurrentAssignment()
    {
        var packet = Packet();
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(
            Serialize(Proposal(packet) with { Action = "switch" }), packet));
        packet.Candidates[0] = packet.Candidates[0] with { Eligible = false };
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Serialize(Proposal(packet)), packet));
    }

    [Fact]
    public void KeepAndSwitchRequireTheirExactAssignmentContext()
    {
        var packet = Packet() with { ActivePortfolioId = "breakout" };
        Assert.Equal("keep", AiPortfolioReviewer.ParseAndValidate(Serialize(Proposal(packet) with { Action = "keep" }), packet).Action);
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Serialize(Proposal(packet)), packet));
        var other = packet.Candidates[0] with { Id = "continuation" };
        packet = packet with { Candidates = [packet.Candidates[0], other] };
        Assert.Equal("switch", AiPortfolioReviewer.ParseAndValidate(Serialize(Proposal(packet) with
        { Action = "switch", PortfolioId = "continuation" }), packet).Action);
    }

    [Fact]
    public void WaitingCannotChangeAssignmentOrCarryOverrides()
    {
        var packet = Packet();
        var waiting = Proposal(packet) with { Action = "wait", PortfolioId = null, StrategyVersion = null, RuleOverrides = [], EvidenceIds = [] };
        Assert.Equal("wait", AiPortfolioReviewer.ParseAndValidate(Serialize(waiting), packet).Action);
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Serialize(waiting with { PortfolioId = "breakout" }), packet));
    }

    [Theory]
    [InlineData("{\"action\":\"wait\"}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void MissingRequiredOutputIsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(json, Packet()));

    [Fact]
    public void UnexpectedAndDuplicatePropertiesAreRejected()
    {
        var packet = Packet();
        string json = Serialize(Proposal(packet));
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(json.Insert(1, "\"execute\":true,"), packet));
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(json.Insert(1, "\"action\":\"wait\","), packet));
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(
            json.Replace("\"rule_id\":", "\"rule_id\":\"stop\",\"rule_id\":"), packet));
    }

    [Fact]
    public async Task StaleInputNeverCallsModelAndExpirationDuringCallRejectsOutput()
    {
        var packet = Packet();
        int calls = 0;
        var clock = new Clock(Now);
        var fake = new FakeClient(_ => { calls++; clock.Now = Now.AddMinutes(6); return Serialize(Proposal(packet)); });
        var reviewer = new AiPortfolioReviewer(fake, clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => reviewer.ReviewAsync(packet with { ExpiresUtc = Now }));
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<InvalidDataException>(() => reviewer.ReviewAsync(packet));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CallerMutationCannotChangeFrozenEligibilityAfterModelStarts()
    {
        var packet = Packet();
        var fake = new FakeClient(_ =>
        {
            packet.Candidates[0] = packet.Candidates[0] with { StrategyVersion = "v2" };
            return Serialize(Proposal(packet));
        });
        var result = await new AiPortfolioReviewer(fake, new Clock(Now)).ReviewAsync(packet);
        Assert.Equal("v1", result.StrategyVersion);
    }

    [Fact]
    public void FutureEvidenceAndDuplicatePortfolioIdentityAreRejected()
    {
        var packet = Packet();
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ValidatePacket(packet with
        { Evidence = [packet.Evidence[0] with { ObservedUtc = Now.AddSeconds(1) }] }, Now));
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ValidatePacket(packet with
        { Candidates = [packet.Candidates[0], packet.Candidates[0]] }, Now));
    }

    private static AiPortfolioPacket Packet() => new(Guid.NewGuid(), 1, Now, Now.AddMinutes(5), null,
        [new("breakout", "v1", "DailyShadow", true, "Synthetic momentum candidate",
            [new("breadth", false, "Weak"), new("stop", true, "Must retain protection")])],
        [new("today", Now, "Synthetic dated observation")], ["Do not infer skill from one winning trade."]);
    private static AiPortfolioProposal Proposal(AiPortfolioPacket packet) => new(packet.PacketId, "select", "breakout", "v1",
        "Momentum evidence supports a proposal.", ["today"], [new("breadth", "Supplied momentum evidence outweighs weak breadth.")],
        "Reconsider if evidence becomes stale.", ["Test this hypothesis on future outcomes."]);
    private static string Serialize(AiPortfolioProposal proposal) => JsonSerializer.Serialize(proposal, Json);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeClient(Func<LlmRequest, string> respond) : ILlmClient
    {
        public string Provider => "synthetic";
        public string Model => "synthetic";
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse(respond(request), new LlmUsage(0, 0, 0), TimeSpan.Zero, Provider, Model));
    }
}
