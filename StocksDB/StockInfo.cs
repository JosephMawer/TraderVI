using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StocksDB
{
    public enum Exchange
    {
        NYSE,
        NASDAQ
    }

    public enum Table
    {
        TimeSeries_Intraday,
        TimeSeries_Daily,
        TimeSeries_DailyAdjusted,
        TimeSeries_Weekly,
        TimeSeries_WeeklyAdjusted,
        TimeSeries_Monthly,
        TimeSeries_MonthlyAdjusted
    }

    public struct SymbolInfo
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Exchange { get; set; }
    }

    public struct ConstituentInfo
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
    }

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
            string query = $"SELECT {sql} [Constituent_Name],[Symbol] FROM [StocksDB].[dbo].[TSX_Constituents_09212019]";

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
    public class Symbols : SQLBase
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

            // todo - could use a data reader object here instead
            var table = await base.GetDataTableFromSQL(sql);
            foreach (DataRow row in table.Rows)
            {
                var si = new SymbolInfo
                {
                    Symbol = row[0].ToString(),
                    Name = row[1].ToString(),
                    Exchange = row[2].ToString()
                };
                lst.Add(si);
            }
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
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con)) {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex) {
                    var msg = ex.Message;
                }
            }
        }
    }
    public class StockInfo : SQLBase
    {
        public StockInfo() { }
        /// <summary>
        /// default constructor
        /// </summary>
        public StockInfo(Table Table) : base($"[StocksDB].[dbo].[{Table.ToString()}]","[Exchange],[Ticker],[Time],[Volume],[Open],[Close],[High],[Low]") { }

        #region Properties
        public int ID { get; set; }
        public string Ticker { get; set; }
        public DateTime Time { get; set; }
        public long Volume { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        #endregion


        public async Task<List<StockInfo>> GetListOfStockInfo(string Ticker, Table Table, string Filter = "")
        {

            string query = $"SELECT [ID],[Ticker],[Time],[Volume],[Open],[Close],[High],[Low] FROM [StocksDB].[dbo].[{Table}] WHERE [Ticker] = '{Ticker}'";

            if (!string.IsNullOrEmpty(Filter))
                query += $" AND {Filter}";

            query += " ORDER BY [Time] DESC";
            var lst = new List<StockInfo>();
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
                                var s = new StockInfo()
                                {
                                    ID = reader.GetInt32(0),
                                    Ticker = reader.GetString(1),
                                    Time = reader.GetDateTime(2),
                                    Volume = reader.GetInt64(3),
                                    Open = reader.GetDecimal(4),
                                    Close = reader.GetDecimal(5),
                                    High = reader.GetDecimal(6),
                                    Low = reader.GetDecimal(7)
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
       
        public async Task Insert(string Exchange, string Ticker, DateTime Time, long Volume, decimal Open, decimal Close, decimal High, decimal Low)
            => await base.Insert($"INSERT INTO {Schema} ({Fields}) VALUES (@exchange,@ticker,@time,@volume,@open,@close,@high,@low)",
                    new List<SqlParameter>()
                         {
                                new SqlParameter() {ParameterName = "@exchange", SqlDbType = SqlDbType.VarChar, Size=10, Value=Exchange},
                                new SqlParameter() {ParameterName = "@ticker", SqlDbType = SqlDbType.VarChar, Size=7, Value=Ticker},
                                new SqlParameter() {ParameterName = "@time", SqlDbType = SqlDbType.DateTime2, Value=Time},
                                new SqlParameter() {ParameterName = "@volume", SqlDbType = SqlDbType.BigInt, Value=Volume},
                                new SqlParameter() {ParameterName = "@open", SqlDbType = SqlDbType.Decimal, Value=Open},
                                new SqlParameter() {ParameterName = "@close", SqlDbType = SqlDbType.Decimal, Value=Close},
                                new SqlParameter() {ParameterName = "@high", SqlDbType = SqlDbType.Decimal, Value=High},
                                new SqlParameter() {ParameterName = "@low", SqlDbType = SqlDbType.Decimal, Value=Low},
                         });
    }
}
