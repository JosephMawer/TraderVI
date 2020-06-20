﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace Core.Db
{
    public class StockInfo : SQLiteBase
    {
        public StockInfo() { }

        /// <summary>
        /// default constructor
        /// </summary>
        public StockInfo(Table Table) : base($"[Db].[dbo].[{Table.ToString()}]","[Exchange],[Ticker],[Time],[Volume],[Open],[Close],[High],[Low]") { }

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

            string query = $"SELECT [ID],[Ticker],[Time],[Volume],[Open],[Close],[High],[Low] FROM [Db].[dbo].[{Table}] WHERE [Ticker] = '{Ticker}'";

            if (!string.IsNullOrEmpty(Filter))
                query += $" AND {Filter}";

            query += " ORDER BY [Time] DESC";
            var lst = new List<StockInfo>();
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
                    new List<SQLiteParameter>()
                         {
                                new SQLiteParameter() {ParameterName = "@exchange", DbType = DbType.String, Size=10, Value=Exchange},
                                new SQLiteParameter() {ParameterName = "@ticker", DbType = DbType.String, Size=7, Value=Ticker},
                                new SQLiteParameter() {ParameterName = "@time", DbType = DbType.DateTime2, Value=Time},
                                new SQLiteParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value=Volume},
                                new SQLiteParameter() {ParameterName = "@open", DbType = DbType.Decimal, Value=Open},
                                new SQLiteParameter() {ParameterName = "@close", DbType = DbType.Decimal, Value=Close},
                                new SQLiteParameter() {ParameterName = "@high", DbType = DbType.Decimal, Value=High},
                                new SQLiteParameter() {ParameterName = "@low", DbType = DbType.Decimal, Value=Low},
                         });
    }
}
