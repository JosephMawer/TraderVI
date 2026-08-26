using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.TMX.Models.Domain
{
    /// <summary>
    /// One validated TMX intraday fetch, including the local observation times
    /// needed to distinguish market-event time from data-receipt time.
    /// </summary>
    public sealed record TmxIntradayBatch(
        string Symbol,
        int IntervalMinutes,
        DateTime RequestedStartUtc,
        DateTime RequestedEndUtc,
        DateTime FetchStartedUtc,
        DateTime ReceivedUtc,
        int AttemptCount,
        int RequestCount,
        IReadOnlyList<OhlcvBar> Bars)
    {
        /// <summary>The newest provider bar-start timestamp in the batch.</summary>
        public DateTime? LatestEventUtc =>
            Bars.Count == 0 ? null : Bars.Max(bar => bar.TimestampUtc);

        /// <summary>
        /// The expected completion time of the newest interval. TMX's verified
        /// intraday timestamps identify the start of each bar.
        /// </summary>
        public DateTime? LatestIntervalCompletedUtc =>
            LatestEventUtc?.AddMinutes(IntervalMinutes);

        /// <summary>
        /// Age of the newest completed interval when this fetch was received.
        /// A negative value is retained as a diagnostic rather than hidden.
        /// </summary>
        public TimeSpan? LatestEvidenceAgeAtReceipt =>
            LatestIntervalCompletedUtc.HasValue
                ? ReceivedUtc - LatestIntervalCompletedUtc.Value
                : null;
    }
}
