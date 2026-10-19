using System;
using System.Data;
using System.IO;
using System.Text.Json;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerRiskSetupDiagnosticTests
{
    [Fact]
    public void UnexpectedFailureExposesCategoryWithoutPrivateExceptionText()
    {
        var error = new IOException("private account, file path or connection data", new JsonException("private field"));
        Assert.Equal("IOException", BrokerRiskSetupDiagnostic.Describe(error));
    }

    [Fact]
    public void MappingFailureIncludesInnerCategoryWithoutColumnValues()
    {
        var error = new DataException("Error parsing column with private account value", new FormatException("private value"));
        Assert.Equal("Data mapping error (FormatException)", BrokerRiskSetupDiagnostic.Describe(error));
    }
}
