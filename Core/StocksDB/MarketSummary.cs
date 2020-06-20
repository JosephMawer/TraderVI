using Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace StocksDB
{
    public enum Markets
    {
        TSX = 1,
        TSXV = 2,
        Alpha = 3
    }
    public struct MarketValues
    {
        public DateTime Date { get; set; }
        public string Name { get; set; }
        public long Volume { get; set; }
        public long Value { get; set; }
        public int IssuesTraded { get; set; }
        public int Advanced { get; set; }
        public int Unchanged { get; set; }
        public int Declined { get; set; }
    }
    public class MarketSummary : SQLiteBase
    {
        public MarketSummary() : base("[StocksDB].[dbo].[MarketSummary]",
            "[Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]") { }


        public async Task InsertMarketSummary(IList<IMarketSummaryInfo> tsxList)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[MarketSummary] values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";
            foreach (var tsx in tsxList)
            {
               await base.Insert(sqlQueryStatement,
                   new List<SQLiteParameter>()
                   {
                        new SQLiteParameter() {ParameterName = "@date", DbType = DbType.DateTime2, Value=tsx.Date},
                        new SQLiteParameter() {ParameterName = "@name", DbType = DbType.String, Size=50, Value=tsx.Name},
                        new SQLiteParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = tsx.Volume},
                        new SQLiteParameter() {ParameterName = "@value", DbType = DbType.Int64, Value = tsx.Value},
                        new SQLiteParameter() {ParameterName = "@traded", DbType = DbType.Int32, Value = tsx.IssuesTraded},
                        new SQLiteParameter() {ParameterName = "@advancers", DbType = DbType.Int32, Value = tsx.Advancers},
                        new SQLiteParameter() {ParameterName = "@unchanged", DbType = DbType.Int32, Value = tsx.Unchanged},
                        new SQLiteParameter() {ParameterName = "@decliners", DbType = DbType.Int32, Value = tsx.Decliners},
                   });
            }
        }
        public async Task InsertMarketSummary(IMarketSummaryInfo tsx)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[MarketSummary] values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";

            await base.Insert(sqlQueryStatement,
               new List<SQLiteParameter>()
               {
                    new SQLiteParameter() {ParameterName = "@date", DbType = DbType.DateTime2, Value=tsx.Date},
                    new SQLiteParameter() {ParameterName = "@name", DbType = DbType.String, Size=50, Value=tsx.Name},
                    new SQLiteParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = tsx.Volume},
                    new SQLiteParameter() {ParameterName = "@value", DbType = DbType.Int64, Value = tsx.Value},
                    new SQLiteParameter() {ParameterName = "@traded", DbType = DbType.Int32, Value = tsx.IssuesTraded},
                    new SQLiteParameter() {ParameterName = "@advancers", DbType = DbType.Int32, Value = tsx.Advancers},
                    new SQLiteParameter() {ParameterName = "@unchanged", DbType = DbType.Int32, Value = tsx.Unchanged},
                    new SQLiteParameter() {ParameterName = "@decliners", DbType = DbType.Int32, Value = tsx.Decliners},
               });
        }

        public async Task<List<MarketValues>> GetFullMarketSummary(Markets market = Markets.TSX)
        {
            string query = $"SELECT [Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]" +
                    $" FROM [StocksDB].[dbo].[MarketSummary]" +
                    $" WHERE [Name] = '" + EnumToString(market) + "'" +
                    $" ORDER BY [Date]";

            var lst = new List<MarketValues>();
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
                                var indice = new MarketValues();
                                indice.Date = reader.GetDateTime(0);
                                indice.Name = reader.GetString(1);
                                indice.Volume = reader.GetInt64(2);
                                indice.Value = reader.GetInt64(3);
                                indice.IssuesTraded = reader.GetInt32(4);
                                indice.Advanced = reader.GetInt32(5);
                                indice.Unchanged = reader.GetInt32(6);
                                indice.Declined = reader.GetInt32(7);
                                lst.Add(indice);
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
        public async Task<MarketValues> GetDailyMarketSummary(Markets market)
        {
            string query = $"SELECT TOP(1) [Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]" +
                    $" FROM [StocksDB].[dbo].[MarketSummary]" +
                    $" WHERE [Name] = '" + EnumToString(market) + "'" +
                    $" ORDER BY [Date] DESC";


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
                                var indice = new MarketValues();
                                indice.Date = reader.GetDateTime(0);
                                indice.Name = reader.GetString(1);
                                indice.Volume = reader.GetInt64(2);
                                indice.Value = reader.GetInt64(3);
                                indice.IssuesTraded = reader.GetInt32(4);
                                indice.Advanced = reader.GetInt32(5);
                                indice.Unchanged = reader.GetInt32(6);
                                indice.Declined = reader.GetInt32(7);
                                return indice;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                }

            }

            throw new Exception("Where's dave?");
        }
        private string EnumToString(Markets market)
        {
            switch (market)
            {
                case Markets.Alpha:
                    return "Alpha";
                case Markets.TSX:
                    return "Toronto Stock Exchange";
                case Markets.TSXV:
                    return "TSX Venture Exchange";
                default:
                    return "";
            }
        }

    }
}
