using System;
using System.Data.SQLite;
using System.IO;

namespace Core.Utilities
{
    public static class Utils
    {

        /// <summary>
        /// Gets the date from a specified amount of days (adjusts for weekends)
        /// </summary>
        /// <param name="previousDays"></param>
        /// <returns>A short date string</returns>
        public static string GetPastDate(int previousDays)
        {
            var date = DateTime.Today.AddDays(previousDays);

            // If the request falls on a weekend, adjust date accordingly
            DayOfWeek day = date.DayOfWeek;
            if (day == DayOfWeek.Saturday)
                _ = date.AddDays(-1);
            else if (day == DayOfWeek.Sunday)
                _ = date.AddDays(-2);

            return date.ToShortDateString();
        }

        /// <summary>
        /// Ensures the date falls on a business day and not a weekend. This is useful for doing
        /// research on the weeknd and avoiding errors when using 'DateTime.Today'.
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static DateTime GetBusinessDay(DateTime date)
        {
            var _date = new DateTime(date.Year, date.Month, date.Day);

            // If the request falls on a weekend, adjust date accordingly
            DayOfWeek day = _date.DayOfWeek;
            if (day == DayOfWeek.Saturday)
                _date = _date.AddDays(-1);
            else if (day == DayOfWeek.Sunday)
                _date = _date.AddDays(-2);

            return _date;
        }



        /// <summary>
        /// This was a little helper method I used to import all the stocks that compose the S&P/TSX Composite index
        /// from a .csv file.  I found all the stocks on Wikipedia: https://en.wikipedia.org/wiki/S%26P/TSX_Composite_Index
        /// </summary>
        public static void ImportTSXCompositeIndex()
        {
            var lines = File.ReadAllLines(@"C:\Users\sesa345094\Desktop\tsx_composite.csv");
            foreach (var line in lines)
            {
                var i = line.Split(',');
                var query = $@"insert into TSXCompositeIndex values ('{i[0].Replace("'", "''")}','{i[1].Replace("'", "''")}','{i[2].Replace("'", "''")}','{i[3].Replace("'", "''")}')";

                using (var con = new SQLiteConnection("Data Source=.;Initial Catalog=Db;Integrated Security=True;"))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(query, con))
                    {
                        cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

    }
}
