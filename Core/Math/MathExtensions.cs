using Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Core.Math
{
    public static class MathExtensions
    {
        /// <summary>
        /// Calculates the price [Close] difference between two dates
        /// </summary>
        /// <param name="stockInfo"></param>=
        /// <returns>The difference of closing prices between two dates as a decimal</returns>
        public static decimal CalculateDifference(this List<IStockInfo> stock, DateTime StartDate, DateTime EndDate)
            => MathHelpers.GetDifference(GetDateOrMostRecent(stock, StartDate),// Price today
                                         GetBusinessDay(stock, EndDate)); // Price some time ago
        
        // makes sure the given date falls on a business day
        private static decimal GetBusinessDay(List<IStockInfo> stock, DateTime date)
        {
            if (stock.Count == 0) return default;
            var adjustedDate = Utils.GetBusinessDay(date);
            try
            {
                var price = stock.Where(p => p.TimeOfRequest == adjustedDate).Single().Close;
                return price;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                return default;
            }
        }

        // sometimes when we pass a date in, it may be the weekend, so of course no stock data exists for that 
        // date.. so what we want to do is grab the most recent date... 
        // this is really poorly written code... fine. agreed. but i don't feel like thinking about this
        // right now..
        private static decimal GetDateOrMostRecent(List<IStockInfo> stock, DateTime date)
        {
            if (stock.Count == 0) return default;
            try
            {
                var adjustedDate = Utils.GetBusinessDay(date);
                var price = stock.Where(p => p.TimeOfRequest == adjustedDate).Single().Close;
                return price;
            }
            catch (Exception ex)
            {
                var s = stock.OrderByDescending(x => x.TimeOfRequest).First();
                // check how far apart this stock data is from expected
                var expectedDate = date;
                var actualDate = s.TimeOfRequest;
                var difference = expectedDate.Subtract(actualDate).TotalDays;

                return s.Close;
            }
        }

    }
}
