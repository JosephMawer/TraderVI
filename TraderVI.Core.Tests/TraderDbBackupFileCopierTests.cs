using Core.Db;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TraderDbBackupFileCopierTests
{
    [Fact]
    public async Task CopyAsync_CopiesBackupAndReturnsMatchingHash()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "source"));
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "destination"));
            var sourceFile = Path.Combine(sourceDirectory.FullName, "TraderDB_FULL_test.bak");
            await File.WriteAllBytesAsync(sourceFile, Encoding.UTF8.GetBytes("TraderVI backup test payload"));

            var result = await VerifiedBackupFileCopier.CopyAsync(sourceFile, destinationDirectory.FullName);

            File.Exists(result.DestinationFile).ShouldBeTrue();
            result.SizeBytes.ShouldBe(new FileInfo(sourceFile).Length);
            result.Sha256.ShouldNotBeNullOrWhiteSpace();
            (await File.ReadAllBytesAsync(result.DestinationFile))
                .ShouldBe(await File.ReadAllBytesAsync(sourceFile));
            Directory.EnumerateFiles(destinationDirectory.FullName, "*.partial")
                .ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CopyAsync_RefusesToOverwriteExistingBackup()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "source"));
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "destination"));
            var sourceFile = Path.Combine(sourceDirectory.FullName, "TraderDB_FULL_test.bak");
            var destinationFile = Path.Combine(destinationDirectory.FullName, Path.GetFileName(sourceFile));
            await File.WriteAllTextAsync(sourceFile, "new backup");
            await File.WriteAllTextAsync(destinationFile, "existing backup");

            await Should.ThrowAsync<IOException>(() =>
                VerifiedBackupFileCopier.CopyAsync(sourceFile, destinationDirectory.FullName));

            (await File.ReadAllTextAsync(destinationFile)).ShouldBe("existing backup");
            Directory.EnumerateFiles(destinationDirectory.FullName).Count().ShouldBe(1);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "TraderVI.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
