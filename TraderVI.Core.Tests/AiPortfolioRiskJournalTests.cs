#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioRiskJournalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Friday = new(2026, 9, 25);
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private readonly AiRiskJournalScope scope = new(Guid.NewGuid(), Guid.NewGuid(), AiRiskAccountMode.Paper);
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new(
        "test", "synthetic", Friday, Monday, [Friday, Monday])));

    private AiPortfolioRiskJournal Open(bool initialize = false, AiRiskJournalScope? otherScope = null) =>
        new(directory, otherScope ?? scope, policy, initialize);
    private PortfolioRiskValuation Value(decimal value, int minute, DateOnly? date = null)
    {
        var session = date ?? Friday;
        DateTimeOffset at = ReviewedTsxSessionCalendar.At(session, new(9, 30)).AddMinutes(minute);
        return new(scope.PortfolioId, Guid.NewGuid(), session, at, at.AddMinutes(2), value, true, true);
    }
    private AiRiskJournalSnapshot Start(AiPortfolioRiskJournal journal) => Observe(journal, 10000m, 0, true);
    private AiRiskJournalSnapshot Observe(AiPortfolioRiskJournal journal, decimal amount, int minute, bool start = false)
    {
        var value = Value(amount, minute);
        return journal.Apply(new(Guid.NewGuid(), journal.ReadCurrent().Revision,
            start ? AiRiskTransitionKind.Start : AiRiskTransitionKind.Observe, value), value.At);
    }
    private AiRiskJournalSnapshot Close(AiPortfolioRiskJournal journal)
    {
        var current = journal.ReadCurrent();
        var state = current.State!;
        var closure = new AiRoundClosure(scope.PortfolioId, state.RoundId, Guid.NewGuid(),
            state.LastValuation.At.AddSeconds(1), 0, 0, state.LastValuation.Value, true);
        return journal.Apply(new(Guid.NewGuid(), current.Revision, AiRiskTransitionKind.ConfirmClosure, Closure: closure), closure.At);
    }
    private AiRiskJournalSnapshot Next(AiPortfolioRiskJournal journal, int minute, DateOnly? date = null)
    {
        var current = journal.ReadCurrent();
        var value = Value(current.State!.LastValuation.Value, minute, date);
        var entry = new AiRoundEntry(current.State.RoundId, value, value.At, value.At, value.ValidUntil, []);
        return journal.Apply(new(Guid.NewGuid(), current.Revision, AiRiskTransitionKind.BeginNextRound, Entry: entry), value.At);
    }

    [Fact]
    public void RestartPreservesPeakAllowanceAndContinuousSecondRound()
    {
        AiRiskJournalSnapshot expected;
        using (var journal = Open(true))
        {
            Start(journal);
            Observe(journal, 11000m, 1);
            Observe(journal, 10450m, 2);
            Close(journal);
            Next(journal, 3);
            Observe(journal, 11000m, 4);
            expected = Observe(journal, 10800m, 5);
        }
        using var recovered = Open();
        Assert.Equal(expected, recovered.ReadCurrent());
        Assert.Equal(2, expected.State!.RoundNumber);
        Assert.Equal(10670m, expected.State.ExitTrigger);
        Assert.Equal(10000m, expected.State.OriginalCapital);
        Assert.Equal(policy.Assess(expected.State, expected.State.LastValuation.At, true, []).NewBuyAllowanceMultiplier,
            policy.Assess(recovered.ReadCurrent().State!, expected.State.LastValuation.At, true, []).NewBuyAllowanceMultiplier);
        Assert.Equal(7, recovered.ReadHistory().Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartRetainsClosingOrReconciledPhase(bool reconcile)
    {
        AiRiskJournalSnapshot expected;
        using (var journal = Open(true))
        {
            Start(journal);
            expected = Observe(journal, 9500m, 1);
            if (reconcile) expected = Close(journal);
        }
        using var recovered = Open();
        Assert.Equal(expected, recovered.ReadCurrent());
        Assert.Equal(reconcile ? AiRiskRoundPhase.AwaitingEntry : AiRiskRoundPhase.Closing, expected.State!.Phase);
        var assessment = policy.Assess(expected.State, expected.State.LastValuation.At, false, []);
        Assert.Equal(0m, assessment.NewBuyAllowanceMultiplier);
        Assert.Equal(!reconcile, assessment.RequiresManagedLiquidation);
    }

    [Fact]
    public void ExhaustedSequenceSurvivesRestartAndWaitsForNextSession()
    {
        Guid sequence;
        using (var journal = Open(true))
        {
            sequence = Start(journal).State!.SequenceId;
            Observe(journal, 9500m, 1); Close(journal); Next(journal, 2);
            Observe(journal, 9215m, 3); Close(journal); Next(journal, 4);
            Observe(journal, 9122.85m, 5); Close(journal);
        }
        using var recovered = Open();
        Assert.Equal(AiRiskRoundPhase.SequencePaused, recovered.ReadCurrent().State!.Phase);
        Assert.Throws<InvalidOperationException>(() => Next(recovered, 6));
        var fresh = Next(recovered, 0, Monday).State!;
        Assert.Equal(2, fresh.SequenceNumber);
        Assert.Equal(1, fresh.RoundNumber);
        Assert.NotEqual(sequence, fresh.SequenceId);
        Assert.Equal(10000m, fresh.OriginalCapital);
    }

    [Fact]
    public void ExactRetriesReturnCurrentStateEvenAfterRestartAndRejectAlteredCommands()
    {
        AiRiskCommand command;
        DateTimeOffset at;
        using (var journal = Open(true))
        {
            var value = Value(10000m, 0);
            at = value.At;
            command = new(Guid.NewGuid(), 0, AiRiskTransitionKind.Start, value);
            journal.Apply(command, at);
            Observe(journal, 9500m, 1);
        }
        using var recovered = Open();
        var before = recovered.ReadCurrent();
        Assert.Equal(before, recovered.Apply(command, at.AddDays(3)));
        Assert.Equal(2, recovered.ReadHistory().Length);
        Assert.Throws<InvalidOperationException>(() => recovered.Apply(command with { ExpectedRevision = 2 }, at));
    }

    [Fact]
    public void StaleRevisionWrongScopeInvalidInputAndRepeatedStartDoNotMutateHistory()
    {
        using var journal = Open(true);
        var initial = Start(journal);
        var value = Value(10100m, 1);
        var command = new AiRiskCommand(Guid.NewGuid(), 0, AiRiskTransitionKind.Observe, value);
        Assert.Throws<InvalidOperationException>(() => journal.Apply(command, value.At));
        Assert.Throws<InvalidOperationException>(() => journal.Apply(command with { ExpectedRevision = 1,
            Valuation = value with { PortfolioId = Guid.NewGuid() } }, value.At));
        Assert.Throws<InvalidOperationException>(() => journal.Apply(command with { ExpectedRevision = 1,
            Valuation = value with { IsComplete = false } }, value.At));
        Assert.Throws<InvalidOperationException>(() => journal.Apply(command with { ExpectedRevision = 1,
            Kind = AiRiskTransitionKind.Start }, value.At));
        Assert.Equal(initial, journal.ReadCurrent());
    }

    [Fact]
    public void OlderEvidenceIdentityCannotBeReusedWithChangedContents()
    {
        using var journal = Open(true);
        var first = Start(journal).State!.LastValuation;
        var current = Observe(journal, 11000m, 1);
        var conflict = Value(11100m, 2) with { EvidenceId = first.EvidenceId };
        Assert.Throws<InvalidDataException>(() => journal.Apply(new(Guid.NewGuid(), current.Revision,
            AiRiskTransitionKind.Observe, conflict), conflict.At));
        Assert.Equal(current, journal.ReadCurrent());
    }

    [Fact]
    public void ExclusiveWriterAndExplicitCreationPreventAccidentalReset()
    {
        using (var journal = Open(true))
        {
            Start(journal);
            Assert.ThrowsAny<IOException>(() => Open());
            Assert.ThrowsAny<IOException>(() => Open(true));
        }
        Assert.ThrowsAny<IOException>(() => Open(true));
        Assert.Throws<InvalidDataException>(() => Open(otherScope: scope with { Mode = AiRiskAccountMode.Live }));
        Assert.Throws<InvalidDataException>(() => Open(otherScope: scope with { AccountScopeId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => Open(otherScope: scope with { PortfolioId = Guid.NewGuid() }));
        using var recovered = Open();
        Assert.Equal(1, recovered.ReadCurrent().Revision);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("pending")]
    [InlineData("version")]
    [InlineData("state")]
    [InlineData("revision")]
    public void DamagedOrUncertainHistoryRequiresRecovery(string damage)
    {
        using (var journal = Open(true)) Start(journal);
        string path = Path.Combine(directory, "ai-risk.json");
        if (damage == "missing") File.Delete(path);
        else if (damage == "corrupt") File.WriteAllText(path, "{}");
        else if (damage == "pending") File.WriteAllText(path + ".pending", "uncertain write");
        else
        {
            // Even a matching checksum cannot make an inconsistent transition valid.
            var envelope = JsonNode.Parse(File.ReadAllText(path))!;
            var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!;
            if (damage == "version") payload["Version"] = 2;
            if (damage == "state") payload["Transitions"]![0]!["State"]!["RoundPeak"] = 20000m;
            if (damage == "revision") payload["Transitions"]![0]!["Revision"] = 2;
            string text = payload.ToJsonString();
            envelope["Payload"] = text;
            envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(path, envelope.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => Open());
        Assert.ThrowsAny<IOException>(() => Open(true));
    }

    [Fact]
    public void SameProcessDetectsRollbackAndDoesNotWriteOverIt()
    {
        using var journal = Open(true);
        Start(journal);
        string path = Path.Combine(directory, "ai-risk.json");
        var old = File.ReadAllBytes(path);
        Observe(journal, 11000m, 1);
        File.WriteAllBytes(path, old);
        Assert.Throws<InvalidDataException>(() => journal.ReadCurrent());
        Assert.Throws<InvalidDataException>(() => Observe(journal, 12000m, 2));
        Assert.Equal(old, File.ReadAllBytes(path));
    }

    [Fact]
    public void FailedWriteDoesNotReturnOrPersistNewState()
    {
        using var journal = Open(true);
        var initial = Start(journal);
        string pending = Path.Combine(directory, "ai-risk.json.pending");
        Directory.CreateDirectory(pending); // force opening the pending file to fail
        var failure = Record.Exception(() => Observe(journal, 11000m, 1));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Directory.Delete(pending);
        Assert.Equal(initial, journal.ReadCurrent());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
