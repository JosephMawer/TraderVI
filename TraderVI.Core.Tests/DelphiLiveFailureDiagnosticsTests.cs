#nullable enable
using Core.Runtime;
using Shouldly;
using System;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveFailureDiagnosticsTests
{
    [Fact]
    public void DiagnosticRetainsErrorIdentityWithoutLeakingExceptionContent()
    {
        var error = new InvalidOperationException("credential=secret; private-provider-response; account-value=12345",
            new Exception("another-secret"));
        string json = DelphiLiveFailureDiagnostics.Describe(error);
        json.ShouldNotContain("secret"); json.ShouldNotContain("12345"); json.ShouldNotContain("provider-response");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("ExceptionType").GetString().ShouldBe(typeof(InvalidOperationException).FullName);
        document.RootElement.GetProperty("HResult").GetInt32().ShouldBe(error.HResult);
        document.RootElement.GetProperty("SqlErrorNumbers").GetArrayLength().ShouldBe(0);
    }
}
