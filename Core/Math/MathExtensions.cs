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
        public static decimal CalculateDifference(this List<IStockInfo> stock, string StartDate, string EndDate)
            => MathHelpers.GetDifference(stock.Where(p => p.TimeOfRequest == StartDate).Single().Close,// Price today
                                         stock.Where(p => p.TimeOfRequest == EndDate).Single().Close); // Price some time ago
        
    }
}
