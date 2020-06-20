using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace StocksDB
{
    public class Symbols : SQLiteBase
    {
        public Symbols() : base("[StocksDB].[dbo].[Symbols]", "[Symbol],[Name],[Exchange]") { }

        /// <summary>
        /// Gets a list of symbols
        /// </summary>
        /// <param name="Filter"></param>
        /// <returns>An ordered list of <see cref="SymbolInfo"/></returns>
        public async Task<List<SymbolInfo>> GetSymbolsList(string Filter)
        {
            var sql = $"select {Fields} from {Schema}";
            if (!string.IsNullOrEmpty(Filter))
                sql += $" {Filter}";
            sql += $" order by [Symbol]";


            var lst = new List<SymbolInfo>();

            throw new NotImplementedException("need to implement reader for sqlite.");
            // todo - could use a data reader object here instead
            //var table = await base.GetDataTableFromSQL(sql);
            //foreach (DataRow row in table.Rows)
            //{
            //    var si = new SymbolInfo
            //    {
            //        Symbol = row[0].ToString(),
            //        Name = row[1].ToString(),
            //        Exchange = row[2].ToString()
            //    };
            //    lst.Add(si);
            //}
            return lst;
        }

     
        /// <summary>
        /// Inserts a symbol into the Symbols table
        /// </summary>
        /// <param name="symbol">The stock symbol, aka ticker</param>
        /// <param name="name">Name or description of stock</param>
        /// <param name="exchange">The exchange to which the stock belongs</param>
        /// <returns></returns>
        public async Task InsertSymbol(string symbol, string name, string exchange)
        {
            var query = $"INSERT INTO {Schema} VALUES ('{symbol.Replace("'", "''")}','{name.Replace("'", "''")}','{exchange}')";
            using (SQLiteConnection con = new SQLiteConnection(ConnectionString))
            {
                try {
                    await con.OpenAsync();
                    using (SQLiteCommand cmd = new SQLiteCommand(query, con)) {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex) {
                    var msg = ex.Message;
                }
            }
        }
    }
}
