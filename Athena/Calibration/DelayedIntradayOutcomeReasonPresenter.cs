#nullable enable

namespace Core.Calibration;

public static class DelayedIntradayOutcomeReasonPresenter
{
    public static string ToOperatorMessage(string? reasonCode) => reasonCode switch
    {
        "FirstPolicySessionOrdinalNotOne" =>
            "Intraday evidence begins after the entry session.",
        "MissingExpectedPolicyBar" =>
            "A required 15-minute intraday bar is missing.",
        null or "" =>
            "The intraday outcome failed an evidence-quality check.",
        _ => reasonCode
    };

    public static string EventTimeLabel(string? reasonCode) => reasonCode switch
    {
        "FirstPolicySessionOrdinalNotOne" => "First evidence",
        "MissingExpectedPolicyBar" => "Missing bar",
        _ => "Evidence time"
    };
}
