using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GranvilleIndicator
{
    public static class Enums
    {
        public static IEnumerable<T> Get<T>()
        {
            return System.Enum.GetValues(typeof(T)).Cast<T>();
        }
    }

    public struct Points
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }
    public enum PluralityOptions
    {
        /// <summary>
        /// When the number of declines outnumbers the number of advances
        /// together with a rise in the S&P/TSX Market average indicating the market is on the
        /// verge of a decline.
        /// </summary>
        DECLINE = 1,

        /// <summary>
        /// When the number of advances outnumbers declines together with a fall
        /// in the S&P/TSX market average, then the market is on the verge of an advance
        /// </summary>
        ADVANCE = 2,

        DECLINE_WILL_CONTINUE = 3,

        ADVANCE_WILL_CONTINUE = 4
    }
    /// <summary>
    /// This class runs all 56 basic day-to-day indicators outlined in Granvilles strategy
    /// </summary>
    public class Granville
    {
        /// <summary>
        /// should run all 56 indicators and return an array consisting of weightings (as outlined in Granvilles
        /// bool, around page 69
        /// </summary>
        /// <returns>An array of points where points with even values or bullish and points with odd values are bearish</returns>
        public async Task<Points[]> GetDailyMarketForecast()
        {
            var points = new List<Points>();

            // Get market plurality
            var plurality = await Plurality.GetMarketPlurality();
            points.Add(plurality);




            return points.ToArray();
        }

        #region Plurality 1 - 4
        public struct ADLine
        {
            public DateTime Date { get; set; }
            public int Advances { get; set; }
            public int Declines { get; set; }
            public int CumulativeAdvances { get; set; }
            public int CumulativeDeclines { get; set; }
            public int CumulativeDifferential { get; set; }
            public decimal TSXAverage { get; set; } // needs to be decimal for database purposes.. blah, blah, blah
        }

        // Daily Plurality: the 'difference' between the number of advancing issues and the number of declining issues
        public static async Task<List<ADLine>> GetAdvanceDeclineLine()
        {
            // Check advancers vs. decliners
            var market = new StocksDB.MarketSummary();
            var indices = new StocksDB.IndiceSummary();
            var summary = await market.GetFullMarketSummary();  // by default, retrieves market info from tsx

            // start generating the Advance-decline list/line thingy...
            var adLst = new List<ADLine>(); // list of Advance-Decline (ADLine) objects
            StocksDB.MarketValues prevAvg = new StocksDB.MarketValues();
            var firstIteration = true;
            var cumulativeAdvancesRunningTotal = 0;
            var cumulativeDeclinesRunningTotal = 0;
            foreach (var s in summary)
            {
                var ad = new ADLine();
                ad.Date = s.Date;
                ad.Advances = s.Advanced;
                ad.Declines = s.Declined;
                ad.TSXAverage = await indices.GetDailyAverage(StocksDB.Indices.TSX, ad.Date);


                if (firstIteration)
                {
                    // First time through the loop this should be execute as there is no
                    // 'previous' data to work with

                    cumulativeAdvancesRunningTotal = s.Advanced;
                    cumulativeDeclinesRunningTotal = s.Declined;
                    firstIteration = false; // ensure flag is set to not hit this code anymore
                }
                else
                {
                    
                    cumulativeAdvancesRunningTotal += s.Advanced;
                    cumulativeDeclinesRunningTotal += s.Declined;
                    var differential = cumulativeAdvancesRunningTotal - cumulativeDeclinesRunningTotal;
                    ad.CumulativeAdvances = cumulativeAdvancesRunningTotal;
                    ad.CumulativeDeclines = cumulativeDeclinesRunningTotal;
                    ad.CumulativeDifferential = differential;
                }

                // add the newly generate adLine struct to our list
                adLst.Add(ad);

                // set the previous market value now and increment the counter
                prevAvg = s;
            }

            // finally, return the list of ADLine structs (and generate table in console) ... or something
            return adLst;
        }
        #endregion

        #region Weighting 15 - 16
        // really, this should be able to run at anytime, so let's call into
        // tmx.market library to acheive real time data (as opposed to pulling from our local db)
        public static async Task GetDailyWeighting()
        {
            // basically, we're looking for noticeable gains in the constituents of each indice which in turn
            // results in a gain for the entire average (sector/indice)



            // right now, the crude implementation is to just look for noticeable gains in the indices

            var market = new Core.TMX.Market();

            // Get daily summary of market indices
            var indice = await market.GetMarketIndices();

            // ask: see what was larger: the total increase or total decrease
            foreach (var x in indice)
            {
                
            }
        }
        #endregion
    }

    public static class Plurality
    {
        /// <summary>
        /// Determines 'balance' by seeing if market is declining or advancing
        /// </summary>
        /// <returns>A balance options enum value indicating market decline or advance</returns>
        public static async Task<Points> GetMarketPlurality()
        {
            var point = new Points();
            point.Name = "Plurality";

            // Check advancers vs. decliners
            var market = new StocksDB.MarketSummary();
            var summary = await market.GetDailyMarketSummary(StocksDB.Markets.TSX);

            // Check TSX Average
            var indice = new StocksDB.IndiceSummary();
            var index = await indice.GetDailyMarketAverage(StocksDB.Indices.TSX);   // gets the daily average for the specified index

            if (summary.Declined > summary.Advanced &&
                index.Change > 0)
            {
                // The average is on the verge of a decline
                point.Value = (int)PluralityOptions.DECLINE;
                return point;
            }
            else if (summary.Declined > summary.Advanced &&
                index.Change < 0)
            {
                // The decline will continue
                point.Value = (int)PluralityOptions.DECLINE_WILL_CONTINUE;
                return point;
            }
            else if (summary.Advanced > summary.Declined &&
                index.Change < 0)
            {
                // The average is on the verge of an advance
                point.Value = (int)PluralityOptions.ADVANCE;
                return point;
            }
            else if (summary.Advanced > summary.Declined &&
                index.Change > 0)
            {
                // The advance will continue
                point.Value = (int)PluralityOptions.ADVANCE_WILL_CONTINUE;
                return point;
            }
            else
            {
                throw new Exception("something wierd happened while getting market balance");
            }
        }
    }

}
