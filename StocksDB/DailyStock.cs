using Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StocksDB
{
    public class DailyStock : SQLBase
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public DailyStock() : base("[StocksDB].[dbo].[DailyStock]",
            "[Date],[Ticker],[Open],[Close],[Volume],[High],[Low]") { }

        /// <summary>
        /// Inserts a list <see cref="IStockInfo"/> into the StocksDB database
        /// </summary>
        /// <param name="stocks"></param>
        /// <returns></returns>
        public async Task InsertDailyStockList(IEnumerable<IStockInfo> stocks)
        {
            var sqlQueryStatement = $"insert into {Schema} values (@date,@ticker,@open,@close,@volume,@high,@low)";
            foreach (var stock in stocks)
            {
                await base.Insert(sqlQueryStatement,
                    new List<SqlParameter>()
                    {
                        new SqlParameter() {ParameterName = "@date", SqlDbType = SqlDbType.Date, Value=stock.TimeOfRequest},
                        new SqlParameter() {ParameterName = "@ticker", SqlDbType = SqlDbType.VarChar, Size=12, Value=stock.Ticker},
                        //new SqlParameter() {ParameterName = "@price", SqlDbType = SqlDbType.Decimal, Value = stock.Price},
                        new SqlParameter() {ParameterName = "@open", SqlDbType = SqlDbType.Decimal, Value = stock.Open},
                        new SqlParameter() {ParameterName = "@close", SqlDbType = SqlDbType.Decimal, Value = stock.Close},
                        new SqlParameter() {ParameterName = "@volume", SqlDbType = SqlDbType.BigInt, Value = stock.Volume},
                        new SqlParameter() {ParameterName = "@high", SqlDbType = SqlDbType.Decimal, Value = stock.High},
                        new SqlParameter() {ParameterName = "@low", SqlDbType = SqlDbType.Decimal, Value = stock.Low},
                    });
            }
        }

        /// <summary>
        /// basically does a select * for the ticker (parameter)
        /// </summary>
        /// <param name="ticker"></param>
        /// <returns>a list of <see cref="IStockInfo"/></returns>
        public async Task<List<IStockInfo>> GetAllStockDataFor(string ticker)
        {
            var query = $"select {Fields} from {Schema} where [Ticker] = '{ticker}'";
            var retLst = new List<IStockInfo>();
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var info = new Core.StockInfo();
                            info.TimeOfRequest = reader.GetDateTime(0).ToShortDateString();
                            info.Ticker = reader.GetString(1);
                            info.Open = reader.GetDecimal(2);
                            info.Close = reader.GetDecimal(3);
                            info.Volume = reader.GetInt64(4);
                            info.High = reader.GetDecimal(5);
                            info.Low = reader.GetDecimal(6);
                            retLst.Add(info);
                        }
                    }
                }
            }
            return retLst;
        }

        // one off method i was using for importing historical stock data to ensure
        // i was adding duplicates
        public async Task<int> GetRecordCount(string ticker)
        {
            var query = $"select count([Ticker]) from [StocksDB].[dbo].[DailyStock] where [Ticker] = '{ticker}'";
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    var rows = (int) await cmd.ExecuteScalarAsync();
                    return rows;
                }
            }
        }
    }
}
