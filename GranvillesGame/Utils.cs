using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GranvillesGame
{
    public static class Utils
    {

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

                using (var con = new SqlConnection("Data Source=.;Initial Catalog=StocksDB;Integrated Security=True;"))
                {
                    con.Open();
                    using (var cmd = new SqlCommand(query, con))
                    {
                        cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

    }
}
