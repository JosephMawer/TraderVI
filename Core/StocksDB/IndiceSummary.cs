using Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace StocksDB
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
    public class IndiceSummary : SQLBase
    {
        #region Inserts
        /// <summary>
        /// Inserts Index summary information into database table IndiceSummary
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public async Task InsertIndiceSummary(IIndexSummary index)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[IndiceSummary] values (@date,@name,@last,@changed,@percentchanged)";
            await base.Insert(sqlQueryStatement,
                new List<SqlParameter>()
                {
                    new SqlParameter() { ParameterName = "@date", SqlDbType = SqlDbType.DateTime2, Value=index.Date},
                    new SqlParameter() { ParameterName = "@name", SqlDbType = SqlDbType.VarChar, Size = 25, Value = index.Name},
                    new SqlParameter() { ParameterName = "@last", SqlDbType = SqlDbType.Float, Value = index.Last},
                    new SqlParameter() { ParameterName = "@changed", SqlDbType = SqlDbType.Float, Value = index.Change},
                    new SqlParameter() { ParameterName = "@percentchanged", SqlDbType = SqlDbType.Float, Value = index.PercentChange}
                });
        }

     
        /// <summary>
        /// Inserts Index summary information into database table IndiceSummary
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public async Task InsertIndiceSummary(IList<IIndexSummary> indexList)
        {
            var sqlQueryStatement = $"insert into [StocksDB].[dbo].[IndiceSummary] values (@date,@name,@last,@changed,@percentchanged)";
            foreach (var index in indexList)
            {
                await base.Insert(sqlQueryStatement,
                    new List<SqlParameter>()
                    {
                        new SqlParameter() { ParameterName = "@date", SqlDbType = SqlDbType.DateTime2, Value=index.Date},
                        new SqlParameter() { ParameterName = "@name", SqlDbType = SqlDbType.VarChar, Size = 25, Value = index.Name},
                        new SqlParameter() { ParameterName = "@last", SqlDbType = SqlDbType.Float, Value = index.Last},
                        new SqlParameter() { ParameterName = "@changed", SqlDbType = SqlDbType.Float, Value = index.Change},
                        new SqlParameter() { ParameterName = "@percentchanged", SqlDbType = SqlDbType.Float, Value = index.PercentChange}
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
                           $"FROM [StocksDB].[dbo].[IndiceSummary] " +
                           $"WHERE [Name] = '{EnumToString(indice)}' " +
                           $"AND [Date] >= '" + date.ToShortDateString() + "' AND [Date] < '" + date.AddDays(1).ToShortDateString() + "'";


            using (SqlConnection con = new SqlConnection(ConnectionString))
            {
                try
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
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
                           $"FROM[StocksDB].[dbo].[IndiceSummary] " +
                           $"where [Name] = '{EnumToString(indice)}' " +
                           $"Order By[Date] DESC";


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
