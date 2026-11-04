#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

public sealed class PaperIncrementalVerificationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("database changed")]
    [InlineData("boundary missing")]
    [InlineData("checkpoint changed")]
    [InlineData("owner changed")]
    [InlineData("root changed")]
    [InlineData("rolled back")]
    public void RestartRequiresTheOriginalCurrentBoundaryWithoutReadingHistory(string scenario)
    {
        var full = DelphiLiveWorkingStateTests.History(2);
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var checkpoint = DelphiLiveWorkingCheckpoint.Qualify(full, package).Checkpoint;
        var db = new EngineDatabaseIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var policy = new PaperVerificationPolicy(db, Guid.NewGuid(), [full.PortfolioId]);
        PaperVerifiedBoundary? baseline = new(db, policy.BaselineRun, package.SnapshotJson, checkpoint);
        var owner = full.PortfolioId;
        string root = package.SnapshotJson;
        if (scenario == "database changed") db = db with { RecoveryFork = Guid.NewGuid() };
        if (scenario == "boundary missing") baseline = null;
        if (scenario == "checkpoint changed") checkpoint = checkpoint with { Totals = checkpoint.Totals with { ArchivedCashNet = 99 } };
        if (scenario == "owner changed") owner = Guid.NewGuid();
        if (scenario == "root changed") root += " ";
        if (scenario == "rolled back")
        {
            var node = JsonNode.Parse(baseline!.Root)!; node["revision"] = 1;
            baseline = baseline with { Root = node.ToJsonString() };
        }
        void Check() => EnginePaperVerificationRepository.RequireCurrentBoundary(policy, db, owner, root, checkpoint, baseline);
        if (scenario == "unchanged") Check(); else Assert.Throws<InvalidDataException>(Check);
    }
    [Theory]
    [InlineData(2)]
    [InlineData(10000)]
    public async Task ColdRestartAndSuccessiveAuditStepsNeedOnlyWorkingRecords(int closedTrades)
    {
        var full = DelphiLiveWorkingStateTests.History(closedTrades);
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var boundary = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        var archive = package.Contents.ToDictionary(x => x.Key, x => x.Value);
        Add(archive, boundary.NewContents);
        string root = package.SnapshotJson;
        var checkpoint = boundary.Checkpoint;
        int peakReadRecords = 0;
        var timer = Stopwatch.StartNew();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            // A cold process has only a serialized checkpoint and the content it references.
            checkpoint = DelphiLiveLedgerJson.Deserialize<DelphiLiveWorkingCheckpoint>(DelphiLiveLedgerJson.Serialize(checkpoint));
            var current = checkpoint.Restore(root, CurrentContents(checkpoint, archive));
            var next = current with { Revision = current.Revision + 1, LastCompletedCycleId = Guid.NewGuid() };
            var written = await checkpoint.AdvanceAsync(current, next, CurrentContents(checkpoint, archive),
                (hash, _) => Task.FromResult(archive.GetValueOrDefault(hash)), default);
            Add(archive, written.NewContents);
            var reads = CurrentContents(checkpoint, archive);
            foreach (var item in written.ChangedItems) reads[item.ContentHash] = archive[item.ContentHash];
            reads[written.Checkpoint.CandidateStateHash!] = archive[written.Checkpoint.CandidateStateHash!];
            peakReadRecords = Math.Max(peakReadRecords, reads.Count);
            var verified = await DelphiLiveIncrementalVerification.VerifyAsync(checkpoint, root, written.Archive.SnapshotJson,
                written.ChangedItems, reads, (_, _) => throw new Exception("No archive-tree read is needed for unchanged collections."), default);
            verified.Checkpoint.RequireSameReconstruction(written.Checkpoint);
            Assert.Equal(current.Cash, verified.Snapshot.Cash);
            checkpoint = verified.Checkpoint; root = written.Archive.SnapshotJson;
        }
        output.WriteLine($"Archive trades={closedTrades}; four cold current restores plus incremental checks={timer.Elapsed.TotalMilliseconds:F1}ms; peak content records={peakReadRecords}");
        Assert.True(peakReadRecords <= 4, $"Read {peakReadRecords} records instead of the working set.");
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing change")]
    [InlineData("damaged new content")]
    [InlineData("skipped revision")]
    [InlineData("duplicate change")]
    public async Task AuditChecksNewRecordsAndTheirExactConnectionToTheSavedBoundary(string scenario)
    {
        var full = DelphiLiveWorkingStateTests.History(10);
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var before = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        var archive = package.Contents.ToDictionary(x => x.Key, x => x.Value); Add(archive, before.NewContents);
        var mark = new DelphiLiveLedgerMark(Guid.NewGuid(), full.CurrentSession!.Value,
            DelphiLivePortfolioMarkKind.Checkpoint, full.UpdatedUtc, true, full.Cash, [], "New");
        var next = before.Snapshot with { Revision = 1, Marks = before.Snapshot.Marks.Add(mark) };
        var written = await before.Checkpoint.AdvanceAsync(before.Snapshot, next, archive,
            (hash, _) => Task.FromResult(archive.GetValueOrDefault(hash)), default);
        Add(archive, written.NewContents);
        var reads = CurrentContents(before.Checkpoint, archive);
        foreach (var item in written.ChangedItems) reads[item.ContentHash] = archive[item.ContentHash];
        var changes = written.ChangedItems;
        string root = written.Archive.SnapshotJson;
        if (scenario == "missing change") changes = [];
        if (scenario == "duplicate change") changes = changes.Add(changes[0]);
        if (scenario == "damaged new content") reads[changes[0].ContentHash] += " ";
        if (scenario == "skipped revision")
        {
            var node = JsonNode.Parse(root)!;
            node["revision"] = 2;
            node[DelphiLiveSnapshotCodec.Marker]!.AsObject().Remove("fullHash");
            node[DelphiLiveSnapshotCodec.Marker]!["fullHash"] = DelphiLiveSnapshotCodec.Fingerprint(node.ToJsonString());
            root = node.ToJsonString();
        }
        Task Verify() => DelphiLiveIncrementalVerification.VerifyAsync(before.Checkpoint, package.SnapshotJson, root,
            changes, reads, (hash, _) => Task.FromResult(archive.GetValueOrDefault(hash)), default);
        if (scenario == "valid") await Verify();
        else await Assert.ThrowsAnyAsync<Exception>(Verify);
        // Failed validation never advances the caller's durable receipt.
        Assert.Equal(0, before.Snapshot.Revision);
    }

    [Fact]
    public async Task UntouchedOldContentIsNotClaimedAsFreshlyVerified()
    {
        var full = DelphiLiveWorkingStateTests.History(20);
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var before = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        var all = package.Contents.ToDictionary(x => x.Key, x => x.Value); Add(all, before.NewContents);
        var next = await before.Checkpoint.AdvanceAsync(before.Snapshot, before.Snapshot with { Revision = 1 }, all,
            (hash, _) => Task.FromResult(all.GetValueOrDefault(hash)), default);
        Add(all, next.NewContents);
        var old = package.Items.First(i => !before.Checkpoint.Items.Any(w => w.ContentHash == i.ContentHash));
        all[old.ContentHash] = "damaged old bytes";
        await DelphiLiveIncrementalVerification.VerifyAsync(before.Checkpoint, package.SnapshotJson, next.Archive.SnapshotJson,
            next.ChangedItems, CurrentContents(before.Checkpoint, all), (_, _) => throw new Exception("Unexpected archive read"), default);
        Assert.ThrowsAny<Exception>(() => DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, all));
    }

    [Fact]
    public void PaperQualificationNeverClearsHistoryOrGrantsRealOrResearchAccess()
    {
        Guid owner = Guid.NewGuid();
        var incident = new EngineIntegrityIncident(Guid.NewGuid(), EngineIntegrityScope.Global, null,
            "EngineContinuity/" + Guid.NewGuid(), "EngineRecoveryRequired", DateTime.UtcNow, true);
        var qualification = new PaperRestartQualification(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), DateTime.UtcNow, [owner], [incident]);
        Assert.True(qualification.Allows([incident], EngineIntegrityScope.Paper, owner));
        Assert.False(qualification.Allows([incident], EngineIntegrityScope.Paper, Guid.NewGuid()));
        Assert.False(qualification.Allows([incident], EngineIntegrityScope.Broker, owner));
        Assert.False(qualification.Allows([incident], EngineIntegrityScope.Research, owner));
        Assert.False(qualification.Allows([incident], EngineIntegrityScope.Global, null));
        Assert.True(qualification.Allows([incident], EngineIntegrityScope.Global, null, paperSharedInputs: true));
        Assert.False(qualification.Allows([incident, incident with { IncidentId = Guid.NewGuid(), FailureCode = "ChecksumFailed" }], EngineIntegrityScope.Paper, owner));
        Assert.False(qualification.Allows([incident with { FailureCode = "EngineContinuityLost" }], EngineIntegrityScope.Paper, owner));
        Assert.Single(qualification.Incidents);
        var otherFailure = incident with { IncidentId = Guid.NewGuid(), Scope = EngineIntegrityScope.Paper,
            OwnerId = Guid.NewGuid(), FailureCode = "PaperIncrementalVerificationFailed" };
        Assert.True(qualification.Allows([incident, otherFailure], EngineIntegrityScope.Paper, owner));
        Assert.True(qualification.AllowsWorker([incident, otherFailure]));
        Assert.False(qualification.Allows([incident, otherFailure], EngineIntegrityScope.Paper, otherFailure.OwnerId));
    }

    [Fact]
    public void AutomaticScheduleCannotResumeLegacyFullPaperScans()
    {
        var db = new EngineDatabaseIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = DateTime.UtcNow;
        EngineAuditRun Run(EngineAuditKind kind, EngineRecoveryScope scope) => new(Guid.NewGuid(),
            new(kind, now, []) { Scope = scope, Database = db }, new(0, 0, null, 0, true), 0, "Complete", now);
        Assert.False(IndependentAuditService.CanRunAutomatically(Run(EngineAuditKind.Archive, EngineRecoveryScope.All)));
        Assert.False(IndependentAuditService.CanRunAutomatically(Run(EngineAuditKind.NewHistory, EngineRecoveryScope.All)));
        Assert.True(IndependentAuditService.CanRunAutomatically(Run(EngineAuditKind.Archive, EngineRecoveryScope.Real)));
        Assert.True(IndependentAuditService.CanRunAutomatically(Run(EngineAuditKind.NewHistory, EngineRecoveryScope.Paper)));
        Assert.True(IndependentAuditService.CanRunAutomatically(Run(EngineAuditKind.Recovery, EngineRecoveryScope.Paper)));
    }

    private static Dictionary<string, string> CurrentContents(DelphiLiveWorkingCheckpoint checkpoint, Dictionary<string, string> all)
    {
        var result = checkpoint.Items.Select(i => i.ContentHash).Append(checkpoint.CandidateStateHash!).Distinct()
            .ToDictionary(hash => hash, hash => all[hash]);
        foreach (var value in result.Values.ToArray())
        {
            var node = JsonNode.Parse(value)!;
            foreach (var field in DelphiLiveSnapshotCodec.DossierFields)
                if (node[field] is JsonObject link && link["contentHash"] is JsonValue hash)
                    result[hash.GetValue<string>()] = all[hash.GetValue<string>()];
        }
        return result;
    }
    private static void Add(Dictionary<string, string> to, IEnumerable<KeyValuePair<string, string>> values)
    { foreach (var pair in values) to[pair.Key] = pair.Value; }
}
