using Core.Db;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace DataAudit.Tests;

public sealed class AuditConnectionOptionsTests
{
    [Fact]
    public void DefaultRetainsLocalIntegratedReadOnlyAuditSettings()
    {
        var options = MarketDataAuditRepository.BuildConnectionOptions(null);

        options.DataSource.ShouldBe("localhost");
        options.InitialCatalog.ShouldBe("TraderDB");
        options.IntegratedSecurity.ShouldBeTrue();
        options.TrustServerCertificate.ShouldBeTrue();
        options.ApplicationIntent.ShouldBe(ApplicationIntent.ReadOnly);
    }

    [Fact]
    public void ExplicitConfigurationRetainsTargetButForcesReadOnlyIntent()
    {
        var configured = new SqlConnectionStringBuilder
        {
            DataSource = "synthetic-audit-host",
            InitialCatalog = "SyntheticAudit",
            IntegratedSecurity = true,
            TrustServerCertificate = false,
            ApplicationIntent = ApplicationIntent.ReadWrite
        };

        var options = MarketDataAuditRepository.BuildConnectionOptions(configured.ConnectionString);

        options.DataSource.ShouldBe(configured.DataSource);
        options.InitialCatalog.ShouldBe(configured.InitialCatalog);
        options.IntegratedSecurity.ShouldBeTrue();
        options.TrustServerCertificate.ShouldBeFalse();
        options.ApplicationIntent.ShouldBe(ApplicationIntent.ReadOnly);
        configured.ApplicationIntent.ShouldBe(ApplicationIntent.ReadWrite);
    }
}
