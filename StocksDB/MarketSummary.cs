﻿using Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
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
    public class MarketSummary : SQLBase
    {
        public MarketSummary() : base("[StocksDB].[dbo].[MarketSummary]",
            "[Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]") { }


        public async Task InsertMarketSummary(IList<IMarketSummaryInfo> tsxList)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[MarketSummary] values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";
            foreach (var tsx in tsxList)
            {
               await base.Insert(sqlQueryStatement,
                   new List<SqlParameter>()
                   {
                        new SqlParameter() {ParameterName = "@date", SqlDbType = SqlDbType.DateTime2, Value=tsx.Date},
                        new SqlParameter() {ParameterName = "@name", SqlDbType = SqlDbType.VarChar, Size=50, Value=tsx.Name},
                        new SqlParameter() {ParameterName = "@volume", SqlDbType = SqlDbType.BigInt, Value = tsx.Volume},
                        new SqlParameter() {ParameterName = "@value", SqlDbType = SqlDbType.BigInt, Value = tsx.Value},
                        new SqlParameter() {ParameterName = "@traded", SqlDbType = SqlDbType.Int, Value = tsx.IssuesTraded},
                        new SqlParameter() {ParameterName = "@advancers", SqlDbType = SqlDbType.Int, Value = tsx.Advancers},
                        new SqlParameter() {ParameterName = "@unchanged", SqlDbType = SqlDbType.Int, Value = tsx.Unchanged},
                        new SqlParameter() {ParameterName = "@decliners", SqlDbType = SqlDbType.Int, Value = tsx.Decliners},
                   });
            }
        }
        public async Task InsertMarketSummary(IMarketSummaryInfo tsx)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[MarketSummary] values (@date,@name,@volume,@value,@traded,@advancers,@unchanged,@decliners)";

            await base.Insert(sqlQueryStatement,
               new List<SqlParameter>()
               {
                    new SqlParameter() {ParameterName = "@date", SqlDbType = SqlDbType.DateTime2, Value=tsx.Date},
                    new SqlParameter() {ParameterName = "@name", SqlDbType = SqlDbType.VarChar, Size=50, Value=tsx.Name},
                    new SqlParameter() {ParameterName = "@volume", SqlDbType = SqlDbType.BigInt, Value = tsx.Volume},
                    new SqlParameter() {ParameterName = "@value", SqlDbType = SqlDbType.BigInt, Value = tsx.Value},
                    new SqlParameter() {ParameterName = "@traded", SqlDbType = SqlDbType.Int, Value = tsx.IssuesTraded},
                    new SqlParameter() {ParameterName = "@advancers", SqlDbType = SqlDbType.Int, Value = tsx.Advancers},
                    new SqlParameter() {ParameterName = "@unchanged", SqlDbType = SqlDbType.Int, Value = tsx.Unchanged},
                    new SqlParameter() {ParameterName = "@decliners", SqlDbType = SqlDbType.Int, Value = tsx.Decliners},
               });
        }

        public async Task<List<MarketValues>> GetFullMarketSummary(Markets market = Markets.TSX)
        {
            string query = $"SELECT [Date],[Name],[Total Volume],[Total Value],[Issues Traded],[Advancers],[Unchanged],[Decliners]" +
                    $" FROM [StocksDB].[dbo].[MarketSummary]" +
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
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
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
