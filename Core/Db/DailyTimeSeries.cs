using Core.Indicators.Models;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;
using db = Core.Db.DailyTimeSeries;

namespace Core.Db
{
    public class DailyTimeSeries : SQLBase
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public DailyTimeSeries() : base("[DailyStock]",
            "[Date],[Ticker],[Open],[Close],[Volume],[High],[Low]") { }


        private string fullyQualifiedFields => "[Date],[Ticker],[Open],[Close],[Volume],[High],[Low],Constituents.[Name]";

        /// <summary>
        /// Pull all stock data into memory
        /// </summary>
        /// <returns>A list of stock data for each ticker</returns>
        public static async Task<List<List<IStockInfo>>> GetAllStocks()
        {
            var constituents = await new SymbolsRepository().GetSymbols();
            var db = new DailyTimeSeries();
            var stockData = new List<List<IStockInfo>>(constituents.Count);  
            foreach (var constituent in constituents)
                stockData.Add(await db.GetAllStockDataFor(constituent.Symbol));

            return stockData;
        }

        // yea. this exposes a static api/method .. cause sometimes
        // I prefer thinking of the database in a singleton type of fashion,
        // just a single static unit.. update, delete, get stuff, etc.
        // exposing this static interface lets me to use the same object in
        // both a static and instance way.. whether or not this is a good idea
        // is debatable.
        public static async Task Insert(IEnumerable<IStockInfo> stocks)
        {
            await new db().InsertDailyStockList(stocks);
        }

        /// <summary>
        /// Inserts a list <see cref="IStockInfo"/> into the Db database
        /// </summary>
        /// <param name="stocks"></param>
        /// <returns></returns>
        public async Task InsertDailyStockList(IEnumerable<IStockInfo> stocks)
        {
            var sqlQueryStatement = $"insert into {DbName} values (@date,@ticker,@open,@close,@volume,@high,@low)";
            foreach (var stock in stocks)
            {
                await base.Insert(sqlQueryStatement,
                    new List<SqlParameter>()
                    {
                        new SqlParameter() {ParameterName = "@date", DbType = DbType.String, Value=SQLBase.DateTimeSQLite(stock.TimeOfRequest)},
                        new SqlParameter() {ParameterName = "@ticker", DbType = DbType.String, Size=12, Value=stock.Ticker},
                        //new SqlParameter() {ParameterName = "@price", DbType = DbType.Decimal, Value = stock.Price},
                        new SqlParameter() {ParameterName = "@open", DbType = DbType.Single, Value = stock.Open},
                        new SqlParameter() {ParameterName = "@close", DbType = DbType.Single, Value = stock.Close},
                        new SqlParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = stock.Volume},
                        new SqlParameter() {ParameterName = "@high", DbType = DbType.Single, Value = stock.High},
                        new SqlParameter() {ParameterName = "@low", DbType = DbType.Single, Value = stock.Low},
                    });
            }
        }

        /// <summary>
        /// Inserts a list <see cref="IStockInfo"/> into the Db database
        /// </summary>
        /// <param name="stocks"></param>
        /// <returns></returns>
        public async Task InsertIntradayStocks(IEnumerable<IStockInfo> stocks)
        {
            var sqlQueryStatement = $"insert into Intraday values (@date,@ticker,@open,@close,@volume,@high,@low)";
            foreach (var stock in stocks)
            {
                await base.Insert(sqlQueryStatement,
                    new List<SqlParameter>()
                    {
                        new SqlParameter() {ParameterName = "@date", DbType = DbType.String, Value=SQLBase.DateTimeSQLite(stock.TimeOfRequest)},
                        new SqlParameter() {ParameterName = "@ticker", DbType = DbType.String, Size=12, Value=stock.Ticker},
                        //new SqlParameter() {ParameterName = "@price", DbType = DbType.Decimal, Value = stock.Price},
                        new SqlParameter() {ParameterName = "@open", DbType = DbType.Single, Value = stock.Open},
                        new SqlParameter() {ParameterName = "@close", DbType = DbType.Single, Value = stock.Close},
                        new SqlParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = stock.Volume},
                        new SqlParameter() {ParameterName = "@high", DbType = DbType.Single, Value = stock.High},
                        new SqlParameter() {ParameterName = "@low", DbType = DbType.Single, Value = stock.Low},
                    });
            }
        }

        /// <summary>
        /// Queries the database for 52 week high of a particular stock
        /// </summary>
        /// <param name="ticker">The ticker symbol to query for</param>
        /// <returns>The 52 week high (based on closing price)</returns>
        public async Task<decimal> Get52WeekHigh(string ticker)
        {
            var today = DateTime.Today.ToShortDateString();
            var query = $@"select max([Close]) 
                           from (select [Close] from [Db].[dbo].[DailyStock]
                                where [Ticker] = '{ticker}' and [Date] >= dateadd(week,-52, '{today}') and [Date] <= '{today}') as d";

            return await base.ExecuteScalarAsync<decimal>(query);
        }

        /// <summary>
        /// Get the date of the latest import of stock data (daily)
        /// </summary>
        /// <returns>A short date string</returns>
        private async Task<string> GetLastImportDate()
        {
            var query = "select [Date] from DailyStock order by [Date] desc limit 1";
            var date = await base.ExecuteScalarAsync<string>(query);
            return DateTime.Parse(date).ToShortDateString();
        }

        public async Task<List<IStockInfo>> GetTopMoversByVolume(int count)//, DateTime date)
        {
            // We don't always have the current days data, so first we will get the last imported date
            var lastDate = await GetLastImportDate();

            var date = DateTimeSQLite(DateTime.Parse(lastDate));

            var query = $@"select {fullyQualifiedFields} from DailyStock
	                       inner join Constituents on DailyStock.Ticker = Constituents.Symbol
                           where [Date] >= '{date}'
                           order by Volume desc
                           limit {count}";

            Debug.WriteLine(nameof(GetTopMoversByVolume) + Environment.NewLine + query);
            
            return await SomethingThatConvertsSQLIntoStockInfo(query);
        }

      
        /// <summary>
        /// basically does a select * for the ticker (parameter)
        /// </summary>
        /// <param name="ticker"></param>
        /// <returns>a list of <see cref="IStockInfo"/></returns>
        public async Task<List<IStockInfo>> GetAllStockDataFor(string ticker, DateRange range = null)
        {
            var query = $@"select [Date],[Ticker],[Open],[Close],[Volume],[High],[Low],Constituents.Name
                           from[DailyStock]
                           inner join Constituents on DailyStock.Ticker = Constituents.Symbol
                           where [Ticker] = '{ticker}' ";
            
            if (range != null)
            {
                var start = DateTimeSQLite(range.StartDate);
                var end = DateTimeSQLite(range.EndDate);

                query += $" and Date between '{start}' and '{end}'";
            }

            query += " order by [Date] desc";

            return await SomethingThatConvertsSQLIntoStockInfo(query);
        }

        // this assumes the query (columns) is in the correct order!!!!!!!!!
        // also, makes the assumption that the current price = the closing price!!!!!
        public async Task<List<IStockInfo>> SomethingThatConvertsSQLIntoStockInfo(string query)
        {
            var retLst = new List<IStockInfo>();
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var info = new Core.Models.StockQuote();
                            info.TimeOfRequest = reader.GetDateTime(0);//.ToShortDateString();
                            info.Ticker = reader.GetString(1);
                            info.Open = reader.GetDecimal(2);
                            info.Close = reader.GetDecimal(3);
                            info.Price = info.Close;
                            info.Volume = reader.GetInt64(4);
                            info.High = reader.GetDecimal(5);
                            info.Low = reader.GetDecimal(6);
                            info.Name = reader.SafeGetString(7);
                       
                            retLst.Add(info);
                        }
                    }
                }
            }
            return retLst;
        }


        // one off method i was using for importing historical stock data to ensure
        // i was adding duplicates
        public async Task<long> GetRecordCount(string ticker)
        {
            var query = $"select count([Ticker]) from [DailyStock] where [Ticker] = '{ticker}'";
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    var rows = (long) await cmd.ExecuteScalarAsync();
                    return rows;
                }
            }
        }
    }
}
