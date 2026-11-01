using System;
using System.IO;
using Shouldly;
using Xunit;

namespace Athena.Tests;

public sealed class IntradayOutcomeReaderTests
{
    [Fact]
    public void CompatibleV3Facts_PreserveLegacyOutcomeIntervalAndPolicyBoundaries()
    {
        string sql = ReadSource();
        sql.ShouldContain("o.[PolicyVersion] = @PolicyVersion");
        sql.ShouldContain("o.[PolicyVersion] IS NULL");
        sql.ShouldContain("o.[CollectorVersion] = N'IntradayEvidenceCollectorV3'");
        sql.ShouldContain("b.[IntervalMinutes] = 5");
        sql.ShouldContain("o.[SourceContractVersion] = @SourceContractVersion");
        sql.ShouldContain("o.[Purpose] = N'PaperMonitor'");
        sql.ShouldContain("o.[AuditState] IN (N'Valid', N'Degraded')");
        sql.ShouldContain("b.[IntervalMinutes] = @IntervalMinutes");
        sql.ShouldContain("b.[EventUtc] >= @FromUtc");
        sql.ShouldContain("b.[EventUtc] <= @ThroughUtc");
    }

    private static string ReadSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TraderVI.sln")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Repository source is unavailable.");
        return File.ReadAllText(Path.Combine(directory.FullName, "Athena", "Db", "IntradayOutcomeReader.cs"));
    }
}
