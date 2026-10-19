using Core.Db;
using Dapper;
using System.Data;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerManagedStorageReadinessTests
{
    // Match the four SQL bit columns returned by ReadStorageAsync. No SQL Server connection.
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    public void NativeQueryShapeMapsAllFourIndependentFlags(bool baseline, bool continuation, bool session, bool immutable)
    {
        using var table = new DataTable();
        foreach (var column in new[] { "BaselineInstalled", "ContinuationInstalled", "SessionInstalled", "ImmutableStorageVerified" })
            table.Columns.Add(column, typeof(bool));
        table.Rows.Add(baseline, continuation, session, immutable);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var read = reader.GetRowParser<BrokerManagedStorageReadiness>()(reader);
        Assert.Equal(baseline, read.BaselineInstalled);
        Assert.Equal(continuation, read.ContinuationInstalled);
        Assert.Equal(session, read.SessionInstalled);
        Assert.Equal(immutable, read.ImmutableStorageVerified);
    }

    [Fact]
    public void OlderThreeFlagConstructionDoesNotInventImmutableStorageProof()
    {
        Assert.False(new BrokerManagedStorageReadiness(true, true, true).ImmutableStorageVerified);
    }
}
