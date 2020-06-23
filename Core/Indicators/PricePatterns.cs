using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Core.Indicators.PricePatterns
{
    public enum SearchPatterns
    {
        HeadAndShoulders
    }
    public static class PricePatterns
    {
        // price pattern can return one of two things: reversals, continuations


        // todo: implement a method like below  that checks for patterns within STREAMS of data, i.e. so we can
        // get some real time analysis by using the TMX class
        //public static IEnumerable<IStockInfo> RunStreamBasedSampling(this Stream<IStockInfo> stock, SearchPatterns searchPattern, int windowSize, int shiftRate = 1)
        //{
        //    throw new NotImplementedException();
        //}

        // todo: i've made some changes... consider the return type of 'RunWindowsBasedSampling' and see if it
        // still makes sense or if some more useful type can be returned, i.e. some new complex type

        /// <summary>
        /// Finds patterns in historical data by taking a sample 'window' of the data and checking the shape
        /// of the data within that window to see if it matches an expected pattern. Iterates over a 
        /// collection of stock data by taking a windows of <see cref="IStockInfo"/> of a specified size, 
        /// using a specified shiftRate (default is 1 day)
        /// </summary>
        /// <param name="stock"></param>
        /// <param name="searchPattern"></param>
        /// <param name="windowSize">The size of each data sample, measured using distance between two dates. So you could have a sample
        /// size of 15 days, or 2 weeks, whatever.</param>
        /// <param name="shiftRate">How many days to move forward between each sample.</param>
        public static IEnumerable<IStockInfo> RunWindowBasedSampling(this List<IStockInfo> stock, SearchPatterns searchPattern, int windowSize, int shiftRate = 1)
        {
            // guard clause: make sure we actually have data to operate on
            if (stock.Count == 0) return default;

            var numberOfSamples = System.Math.Floor((decimal)(stock.Count / shiftRate));


            // convert the 'windowSize' into a a section of data between two dates that is 
            // the size of the asked window.
            DateTime start, end;
            start = DateTime.Parse(stock.Last().TimeOfRequest);
            end = start.AddDays(windowSize);

            for (int i = 0; i < numberOfSamples; i++)
            {
                // gets a window of data from the entire stock collection
                var window = stock.Where(x => DateTime.Parse(x.TimeOfRequest) >= start && DateTime.Parse(x.TimeOfRequest) <= end);

                // guard clause: make sure our query actually returned some data
                if (window.Count() == 0) continue;


                if (searchPattern == SearchPatterns.HeadAndShoulders)
                {
                    // run the head and shoulders pattern match algorithm on the current window
                    if (window.FindHeadAndShoulders()) return window;
                }

                // move the sampe range ahead x number of days
                start = start.AddDays(shiftRate);
                end = start.AddDays(windowSize);
            }


            return default;
        }

        /// <summary>
        /// Iterates over a collection of stock data by taking a windows of <see cref="IStockInfo"/> of a 
        /// specified size, using a specified shiftRate (default is 1 day) and checks if the shape of the data
        /// matches an expected head and shoulders pattern.
        /// </summary>
        /// <param name="window">Some window of data; a section of the time series;</param>
        /// <returns></returns>
        private static bool FindHeadAndShoulders(this IEnumerable<IStockInfo> window)
        {
            if (window.Count() < 6) throw new NotSupportedException("There are not enough data points in the current window to check for a head and shoulders pattern, which requires at least 7 points.");

            // the required number of points to make a head and shoulders pattern (defines peaks and valleys)
            var hsPoints = 7;

            // gives us the intervals at which we read through the array to do the H&S check
            var interval = window.Count() / hsPoints;

            var currentInterval = 0;
            var dataPoints = new List<IStockInfo>();
            for (int j = 0; j < hsPoints; j++)
            {
                var datapoint = window.ElementAt(currentInterval);
                dataPoints.Add(datapoint);
                currentInterval += interval;
            }

            // pattern match: check for the head and shoulders 'shape'
            // [A] [B] [C] [D] [E] [F] [G]
            // [0] [1] [2] [3] [4] [5] [6]
            var A = dataPoints.ElementAt(0).Close;
            var B = dataPoints.ElementAt(1).Close;
            var C = dataPoints.ElementAt(2).Close;
            var D = dataPoints.ElementAt(3).Close;
            var E = dataPoints.ElementAt(4).Close;
            var F = dataPoints.ElementAt(5).Close;
            var G = dataPoints.ElementAt(6).Close;
            if (B > A &&
                B > C &&
                D > C &&
                D > E &&
                F > E &&
                F > G)
            {
                // todo: 
                // 1) confirm that volume pattern matches the above price pattern
                // 2) get the trend line and return the value at which the reversal/continuation will
                // manifest itself.

                // congrats you found the pattern!
                return true;
            }
            return false;
        }
    }
}
