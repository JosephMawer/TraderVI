using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Indicators
{
    // ═══════════════════════════════════════════════════════════════════
    // DEPRECATED — Legacy Granville implementation.
    // Replaced by Core.Indicators.Granville namespace (folder).
    // Kept for reference only. Do not use in new code.
    // ═══════════════════════════════════════════════════════════════════

    [Obsolete("Use Core.Indicators.Granville.GranvilleComposite and related classes instead.")]
    public static class Enums
    {
        public static IEnumerable<T> Get<T>()
        {
            return System.Enum.GetValues(typeof(T)).Cast<T>();
        }
    }

    [Obsolete("Use Core.Indicators.Granville.GranvilleResult instead.")]
    public struct Points
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }

    [Obsolete("Use Core.Indicators.Granville.IndicatorSignal instead.")]
    public enum PluralityOptions
    {
        DECLINE = 1,
        ADVANCE = 2,
        DECLINE_WILL_CONTINUE = 3,
        ADVANCE_WILL_CONTINUE = 4
    }

    [Obsolete("Use Core.Indicators.Granville.GranvilleComposite instead. Renamed to avoid namespace collision.")]
    public class GranvilleLegacy
    {
        public async Task<Points[]> GetDailyMarketForecast()
        {
            throw new NotSupportedException("Use Core.Indicators.Granville.GranvilleComposite.Evaluate() instead.");
        }

        #region Plurality 1 - 4
        [Obsolete("Use Core.Indicators.ADLineEntry instead.")]
        public struct ADLine
        {
            public DateTime Date { get; set; }
            public int Advances { get; set; }
            public int Declines { get; set; }
            public int CumulativeAdvances { get; set; }
            public int CumulativeDeclines { get; set; }
            public int CumulativeDifferential { get; set; }
            public decimal TSXAverage { get; set; }
        }

        [Obsolete("Use AdvanceDeclineCalculator.Compute() instead.")]
        public static async Task<List<ADLine>> GetAdvanceDeclineLine()
        {
            throw new NotSupportedException("Use AdvanceDeclineCalculator.Compute() instead.");
        }
        #endregion

        #region Weighting 15 - 16
        [Obsolete("Not yet reimplemented in the new Granville system.")]
        public static async Task GetDailyWeighting()
        {
            throw new NotSupportedException("Will be reimplemented as WeightingIndicators in Core.Indicators.Granville namespace.");
        }
        #endregion
    }

    [Obsolete("Use Core.Indicators.Granville.PluralityIndicators instead.")]
    public static class Plurality
    {
        public static async Task<Points> GetMarketPlurality()
        {
            throw new NotSupportedException("Use Core.Indicators.Granville.PluralityIndicators.Evaluate() instead.");
        }
    }
}
