using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace StocksDB
{
    public class Constituents : SQLBase
    {
        public Constituents() : base("[StocksDB].[dbo].[TSX_Constituents_10142019]", "[Constituent_Name],[Symbol]") { }

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
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con))
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

        public async Task<List<ConstituentInfo>> GetConstituents(int? count = null)
        {
            var sql = (count != null) ? $"TOP({count})" : "";
            string query = $"SELECT {sql} [Constituent_Name],[Symbol] FROM {Schema}";

            var lst = new List<ConstituentInfo>();
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
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
