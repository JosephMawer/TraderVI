using Core.Db;
using Shouldly;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TraderDbBackupRetentionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TraderVI.Core.Tests", Guid.NewGuid().ToString("N"));
    private string Staging => Path.Combine(root, "staging");
    private string Destination => Path.Combine(root, "destination");
    public TraderDbBackupRetentionTests() { Directory.CreateDirectory(Staging); Directory.CreateDirectory(Destination); }
    public void Dispose() { Directory.Delete(root, true); }

    [Fact]
    public void KeepsTwoInEachLocationAndIgnoresNewerIncompleteAndUnrelatedFiles()
    {
        foreach (string directory in new[] { Staging, Destination })
        {
            for (int day = 1; day <= 6; day++) Write(directory, day);
            File.WriteAllText(Path.Combine(directory, "unrelated.bak"), "keep");
            File.WriteAllText(Path.Combine(directory, "TraderDB_FULL_20260999_000000_000.bak"), "keep");
            File.WriteAllText(Path.Combine(directory, "TraderDB_FULL_20260907_000000_000.bak.partial"), "keep");
        }
        var removed = TraderDbBackupRetention.Apply(FileName(Staging, 5), FileName(Destination, 5), 4, Completed());
        removed.Count.ShouldBe(6);
        foreach (string directory in new[] { Staging, Destination })
        {
            File.Exists(FileName(directory, 3)).ShouldBeFalse();
            File.Exists(FileName(directory, 4)).ShouldBeTrue();
            File.Exists(FileName(directory, 5)).ShouldBeTrue();
            File.Exists(FileName(directory, 6)).ShouldBeTrue();
            Directory.GetFiles(directory).Length.ShouldBe(6);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrChangedVerifiedCopyLeavesBothLocationsUntouched(bool missing)
    {
        for (int day = 1; day <= 3; day++) { Write(Staging, day); Write(Destination, day); }
        if (missing) File.Delete(FileName(Destination, 3));
        else File.WriteAllText(FileName(Destination, 3), "changed");
        Should.Throw<IOException>(() => TraderDbBackupRetention.Apply(FileName(Staging, 3), FileName(Destination, 3), 4, Completed()));
        Directory.GetFiles(Staging).Length.ShouldBe(3);
        File.Exists(FileName(Destination, 1)).ShouldBeTrue();
    }

    [Fact]
    public void SameLocationIsRejected()
    {
        Write(Staging, 3);
        Should.Throw<InvalidOperationException>(() => TraderDbBackupRetention.Apply(FileName(Staging, 3), FileName(Staging, 3), 4, Completed()));
    }

    [Theory]
    [InlineData(0x9000001au, true)]
    [InlineData(0x9000601au, true)]
    [InlineData(0xa000000cu, false)]
    [InlineData(0xa0000003u, false)]
    [InlineData(0x9000601bu, false)]
    public void CloudFilesAreAllowedButLinksAndJunctionsAreRejected(uint tag, bool allowed) =>
        TraderDbBackupRetention.IsCloudTag(tag).ShouldBe(allowed);
    private static IReadOnlySet<string> Completed() => Enumerable.Range(1, 10)
        .Select(day => Path.GetFileName(FileName("", day))).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void FailedLegacyBackupIsNeitherCountedNorDeletedAsCompleted()
    {
        for (int day = 1; day <= 5; day++) { Write(Staging, day); Write(Destination, day); }
        var completed = Completed().Where(x => x != Path.GetFileName(FileName("", 4))).ToHashSet(StringComparer.Ordinal);
        TraderDbBackupRetention.Apply(FileName(Staging, 5), FileName(Destination, 5), 4, completed);
        foreach (string directory in new[] { Staging, Destination })
        {
            File.Exists(FileName(directory, 2)).ShouldBeFalse();
            File.Exists(FileName(directory, 3)).ShouldBeTrue();
            File.Exists(FileName(directory, 4)).ShouldBeTrue();
            File.Exists(FileName(directory, 5)).ShouldBeTrue();
        }
    }
    private static string FileName(string directory, int day) => Path.Combine(directory, $"TraderDB_FULL_202609{day:00}_000000_000.bak");
    private static void Write(string directory, int day) => File.WriteAllText(FileName(directory, day), "data");
}
