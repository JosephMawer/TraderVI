using Core.TMX.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace Core.Db
{

    /// <summary>
    /// Represents information about a single index for a given day
    /// </summary>
    public struct IndiceValue
    {
        public DateTime Date { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal Change { get; set; }
        public decimal PercentChange { get; set; }
    }
   

    public enum Indices
    {
        TSX,
        TSX_Venture,
        VIXC,
        Energy,
        Financials,
        Health_Care,
        Industrials,
        Info_Tech,
        Metals_Mining,
        Telecom,
        Utilities,
    }
    public class IndiceSummary : SQLiteBase
    {
        #region Inserts
        /// <summary>
        /// Inserts Index summary information into database table IndiceSummary
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public async Task InsertIndiceSummary(MarketIndices index)
        {
            var sqlQueryStatement = $"insert into [Db].[dbo].[IndiceSummary] values (@date,@name,@last,@changed,@percentchanged)";
            await base.Insert(sqlQueryStatement,
                new List<SQLiteParameter>()
                {
                    new SQLiteParameter() { ParameterName = "@date", DbType = DbType.DateTime2, Value=index.Date},
                    new SQLiteParameter() { ParameterName = "@name", DbType = DbType.String, Size = 25, Value = index.Name},
                    new SQLiteParameter() { ParameterName = "@last", DbType = DbType.Single, Value = index.Last},
                    new SQLiteParameter() { ParameterName = "@changed", DbType = DbType.Single, Value = index.Change},
                    new SQLiteParameter() { ParameterName = "@percentchanged", DbType = DbType.Single, Value = index.PercentChange}
                });
        }

     
        /// <summary>
        /// Inserts Index summary information into database table IndiceSummary
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public async Task InsertIndiceSummary(IList<MarketIndices> indexList)
        {
            var sqlQueryStatement = $"insert into [Db].[dbo].[IndiceSummary] values (@date,@name,@last,@changed,@percentchanged)";
            foreach (var index in indexList)
            {
                await base.Insert(sqlQueryStatement,
                    new List<SQLiteParameter>()
                    {
                        new SQLiteParameter() { ParameterName = "@date", DbType = DbType.DateTime2, Value=index.Date},
                        new SQLiteParameter() { ParameterName = "@name", DbType = DbType.String, Size = 25, Value = index.Name},
                        new SQLiteParameter() { ParameterName = "@last", DbType = DbType.Single, Value = index.Last},
                        new SQLiteParameter() { ParameterName = "@changed", DbType = DbType.Single, Value = index.Change},
                        new SQLiteParameter() { ParameterName = "@percentchanged", DbType = DbType.Single, Value = index.PercentChange}
                    });
            }
        }
        #endregion

    
        /// <summary>
        /// Gets the average price from database for the specified indice on the specified date
        /// </summary>
        /// <param name="indice">The indice (based on TSX)</param>
        /// <param name="date">The date of the value you want to see</param>
        /// <returns></returns>
        public async Task<decimal> GetDailyAverage(Indices indice, DateTime date)
        {
            string query = $"SELECT [Last] " +
                           $"FROM [Db].[dbo].[IndiceSummary] " +
                           $"WHERE [Name] = '{EnumToString(indice)}' " +
                           $"AND [Date] >= '" + date.ToShortDateString() + "' AND [Date] < '" + date.AddDays(1).ToShortDateString() + "'";


            using (SQLiteConnection con = new SQLiteConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (var cmd = new SQLiteCommand(query, con))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            try
                            {
                                if (!reader.HasRows) return 0m;
                                while (await reader.ReadAsync())
                                {
                                    var average = reader.GetDecimal(0);

                                    return average;
                                }
                            }
                            catch (Exception ex)
                            {
                                // should probably figure out a better way to handle this, but for now if we don't have
                                // the average for a particualr date just return 0
                                return 0m;
                            }
                          
                        }
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                }

            }

            throw new Exception("Where's bobs wife?");
        }
        public async Task<IndiceValue> GetDailyMarketAverage(Indices indice)
        {
            string query = $"SELECT TOP(1) [Date],[Name],[Last],[Change],[PercentChange] " +
                           $"FROM[Db].[dbo].[IndiceSummary] " +
                           $"where [Name] = '{EnumToString(indice)}' " +
                           $"Order By[Date] DESC";


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
                                var index = new IndiceValue();
                                index.Date = reader.GetDateTime(0);
                                index.Name = reader.GetString(1);
                                index.Price = reader.GetDecimal(2);
                                index.Change = reader.GetDecimal(3);
                                index.PercentChange = reader.GetDecimal(4);

                                return index;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                }

            }

            throw new Exception("Where's bob?");
            
        }

        private string EnumToString(Indices indice)
        {
            if (indice == Indices.Metals_Mining)
                return "Metals & Mining";   // special case for metals and mining
            else return indice.ToString().Replace("_", " "); // otherwise simply replace underscores with original spaces 
        }


    }
}
