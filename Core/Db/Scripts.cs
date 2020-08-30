using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace Core.Db
{
    // scripts... for lack of a better name. wow.
    public static class Scripts
    {
        public static void EnsureDbExists(bool overwrite = false)
        {
            // run the setup scripts to create and initialize the db
            using var con = new SQLiteConnection($"Data Source=.;Initial Catalog=Db;Integrated Security=True;");
            con.Open();
            var query = "";
            using var cmd = new SQLiteCommand(query, con);
            cmd.ExecuteNonQueryAsync();
        }

//        public string DailyStock = "CREATE TABLE "DailyStock" (
//	"Date"	TEXT,
//	"Ticker"	INTEGER,
//	"Open"	REAL,
//	"Close"	REAL,
//	"Volume"	NUMERIC,
//	"High"	REAL,
//	"Low"	REAL
//);"
	}
}
