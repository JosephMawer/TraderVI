using Core.Utilities;
using System;

namespace Core.Indicators.Models
{
    public class DateRange
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; }
        public double DifferenceInDays => StartDate.Subtract(EndDate).TotalDays;

        /// <summary>
        /// Simple struct used for passing around a date range. Useful for when you need to compare
        /// some data between a start and end date. It ensures the start and end dates don't fall on weekends.
        /// </summary>
        /// <param name="daysAgo">The number of days prior to the start date.</param>
        public DateRange(DateTime startDate, int daysAgo)
        {
            if (daysAgo > 0) daysAgo *= -1; // convert positive value to a negative value
            StartDate = Utils.GetBusinessDay(startDate);
            EndDate = DateTime.Parse(Utils.GetPastDate(daysAgo));
        }
    }
}
