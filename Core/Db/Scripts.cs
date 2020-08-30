using Core.Utilities;
using System.Data.SQLite;

namespace Core.Db
{
    // scripts... for lack of a better name. wow.
    public static class Scripts
    {
        private static readonly string[] Tables =
        {
            "CREATE TABLE `Constituents` (`Name` TEXT,`Symbol` TEXT)",
            "CREATE TABLE `DailyStock` (`Date` TEXT,`Ticker` TEXT,`Open`	REAL,`Close` REAL,`Volume` NUMERIC,`High` REAL,`Low` REAL)"
        };

        public static void CreateDatabase()
        {
            using var con = new SQLiteConnection(Utils.GetConnectionString);
            con.Open();
            foreach (var query in Tables)
            {
                using var cmd = new SQLiteCommand(query, con);
                cmd.ExecuteNonQuery();
            }
        }
	}
}
