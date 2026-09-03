#nullable enable

using Core.Calibration;
using Shouldly;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelayedIntradayOutcomeReasonPresenterTests
{
    [Fact]
    public void FirstPolicySessionOrdinalNotOne_HasPlainOperatorMessage()
    {
        DelayedIntradayOutcomeReasonPresenter
            .ToOperatorMessage("FirstPolicySessionOrdinalNotOne")
            .ShouldBe("Intraday evidence begins after the entry session.");
        DelayedIntradayOutcomeReasonPresenter
            .EventTimeLabel("FirstPolicySessionOrdinalNotOne")
            .ShouldBe("First evidence");
    }

    [Fact]
    public void MissingExpectedPolicyBar_HasPlainOperatorMessage()
    {
        DelayedIntradayOutcomeReasonPresenter
            .ToOperatorMessage("MissingExpectedPolicyBar")
            .ShouldBe("A required 15-minute intraday bar is missing.");
        DelayedIntradayOutcomeReasonPresenter
            .EventTimeLabel("MissingExpectedPolicyBar")
            .ShouldBe("Missing bar");
    }

    [Fact]
    public void UnknownReason_RemainsAvailableForDiagnostics()
    {
        DelayedIntradayOutcomeReasonPresenter
            .ToOperatorMessage("FutureReason")
            .ShouldBe("FutureReason");
    }
}
