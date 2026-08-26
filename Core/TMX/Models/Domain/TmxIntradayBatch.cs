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
        /// Age of the newest returned interval when this fetch was received.
        /// A negative value means that the newest bar was still forming and is
        /// retained as a diagnostic rather than hidden.
        /// </summary>
        public TimeSpan? LatestEvidenceAgeAtReceipt =>
            LatestIntervalCompletedUtc.HasValue
                ? ReceivedUtc - LatestIntervalCompletedUtc.Value
                : null;

        /// <summary>
        /// The newest bar whose full interval had elapsed by receipt time.
        /// A newer returned bar may still be forming and may later be revised.
        /// </summary>
        public OhlcvBar LatestCompletedBarAtReceipt =>
            Bars.LastOrDefault(bar =>
                bar.TimestampUtc.AddMinutes(IntervalMinutes) <= ReceivedUtc);

        /// <summary>The start timestamp of the newest completed bar.</summary>
        public DateTime? LatestCompletedEventUtc =>
            LatestCompletedBarAtReceipt?.TimestampUtc;

        /// <summary>
        /// Age of the newest completed interval at receipt time. Unlike
        /// <see cref="LatestEvidenceAgeAtReceipt"/>, this cannot be negative.
        /// </summary>
        public TimeSpan? LatestCompletedEvidenceAgeAtReceipt =>
            LatestCompletedEventUtc.HasValue
                ? ReceivedUtc - LatestCompletedEventUtc.Value.AddMinutes(IntervalMinutes)
                : null;

        /// <summary>Whether the newest returned bar was still forming.</summary>
        public bool HasFormingBarAtReceipt =>
            LatestIntervalCompletedUtc.HasValue &&
            LatestIntervalCompletedUtc.Value > ReceivedUtc;
    }
}
