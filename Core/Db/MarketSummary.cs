using Core.TMX.Models;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace Core.Db
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
    public class MarketSummary : SQLBase
    {
        public MarketSummary() : base("[Db].[dbo].[MarketSummary]",
            "[Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]") { }


        public async Task InsertMarketSummary(IList<TMX.Models.MarketMoverItem> tsxList)
        {
            var sqlQueryStatement = $"insert into [Db].[dbo].[MarketSummary] " +
                $"values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";
            foreach (var tsx in tsxList)
            {
                throw new Exception("todo - update db to match new DTO, aka marketmover");
               //await base.Insert(sqlQueryStatement,
               //    new List<SqlParameter>()
               //    {
               //         new SqlParameter() {ParameterName = "@date", DbType = DbType.DateTime2, Value=tsx.Date},
               //         new SqlParameter() {ParameterName = "@name", DbType = DbType.String, Size=50, Value=tsx.Name},
               //         new SqlParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = tsx.Volume},
               //         new SqlParameter() {ParameterName = "@value", DbType = DbType.Int64, Value = tsx.Value},
               //         new SqlParameter() {ParameterName = "@traded", DbType = DbType.Int32, Value = tsx.IssuesTraded},
               //         new SqlParameter() {ParameterName = "@advancers", DbType = DbType.Int32, Value = tsx.Advancers},
               //         new SqlParameter() {ParameterName = "@unchanged", DbType = DbType.Int32, Value = tsx.Unchanged},
               //         new SqlParameter() {ParameterName = "@decliners", DbType = DbType.Int32, Value = tsx.Decliners},
               //    });
            }
        }
        public async Task InsertMarketSummary(TMX.Models.MarketMoverItem tsx)
        {
            var sqlQueryStatement = $"insert into [Db].[dbo].[MarketSummary] values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";
            throw new Exception("todo - update db to match new DTO, aka marketmover");
            //await base.Insert(sqlQueryStatement,
            //   new List<SqlParameter>()
            //   {
            //        new SqlParameter() {ParameterName = "@date", DbType = DbType.DateTime2, Value=tsx.Date},
            //        new SqlParameter() {ParameterName = "@name", DbType = DbType.String, Size=50, Value=tsx.Name},
            //        new SqlParameter() {ParameterName = "@volume", DbType = DbType.Int64, Value = tsx.Volume},
            //        new SqlParameter() {ParameterName = "@value", DbType = DbType.Int64, Value = tsx.Value},
            //        new SqlParameter() {ParameterName = "@traded", DbType = DbType.Int32, Value = tsx.IssuesTraded},
            //        new SqlParameter() {ParameterName = "@advancers", DbType = DbType.Int32, Value = tsx.Advancers},
            //        new SqlParameter() {ParameterName = "@unchanged", DbType = DbType.Int32, Value = tsx.Unchanged},
            //        new SqlParameter() {ParameterName = "@decliners", DbType = DbType.Int32, Value = tsx.Decliners},
            //   });
        }

        public async Task<List<MarketValues>> GetFullMarketSummary(Markets market = Markets.TSX)
        {
            string query = $"SELECT [Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]" +
                    $" FROM [Db].[dbo].[MarketSummary]" +
                    $" WHERE [Name] = '" + EnumToString(market) + "'" +
                    $" ORDER BY [Date]";

            var lst = new List<MarketValues>();
            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con))
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
                    $" FROM [Db].[dbo].[MarketSummary]" +
                    $" WHERE [Name] = '" + EnumToString(market) + "'" +
                    $" ORDER BY [Date] DESC";


            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con))
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
