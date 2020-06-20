using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace StocksDB
{
    public class Constituents : SQLiteBase
    {
        public Constituents() : base("[Constituents]", "[Name],[Symbol]") { }

        /// <summary>
        /// Inserts a symbol into the Symbols table
        /// </summary>
        /// <param name="symbol">The stock symbol, aka ticker</param>
        /// <param name="name">Name or description of stock</param>
        /// <param name="exchange">The exchange to which the stock belongs</param>
        /// <returns></returns>
        public async Task InsertConstituent(string name, string symbol)
        {
            var query = $"INSERT INTO {Schema} VALUES ('{name.Replace("'", "''")}','{symbol.Replace("'", "''")}')";
            using (SQLiteConnection con = new SQLiteConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SQLiteCommand cmd = new SQLiteCommand(query, con))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                }
            }
        }

        /// <summary>
        /// Returns a list of constituents from TSX
        /// </summary>
        /// <param name="count">Optional: the number of constituents to return</param>
        /// <returns>The full list of constituents, if a count is provided it will return count many constituents</returns>
        public async Task<List<ConstituentInfo>> GetConstituents(int? count = null)
        {
            var sql = (count != null) ? $"TOP({count})" : "";
            string query = $"SELECT {sql} Name,Symbol FROM {Schema}";

            var lst = new List<ConstituentInfo>();
            using (SQLiteConnection con = new SQLiteConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SQLiteCommand cmd = new SQLiteCommand(query, con))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var s = new ConstituentInfo()
                                {
                                    Name = reader.GetString(0),
                                    Symbol = reader.GetString(1)
                                };
                                lst.Add(s);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                }

            }

            return lst;
        }
    }
}
