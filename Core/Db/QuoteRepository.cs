using Core.TMX.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Core.Db
{
    public class TickerInfo
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Exchange { get; set; }
    }
    public class QuoteRepository : SQLiteBase
    {
        public QuoteRepository() : base("[dbo].[Ticker]", "[Ticker],[Name],[Exchange]") { }

        /// <summary>
        /// Gets a list of symbols
        /// </summary>
        /// <param name="Filter"></param>
        /// <returns>An ordered list of <see cref="TickerInfo"/></returns>
        public async Task<List<TickerInfo>> GetSymbols(string Filter)
        {
            var sql = $"select {Fields} from {Schema}";
            if (!string.IsNullOrEmpty(Filter))
                sql += $" {Filter}";
            sql += $" order by [Symbol]";


            var lst = new List<TickerInfo>();

            throw new NotImplementedException("need to implement reader for sqlite.");
            // todo - could use a data reader object here instead
            //var table = await base.GetDataTableFromSQL(sql);
            //foreach (DataRow row in table.Rows)
            //{
            //    var si = new SymbolInfo
            //    {
            //        Symbol = row[0].ToString(),
            //        Name = row[1].ToString(),
            //        Exchange = row[2].ToString()
            //    };
            //    lst.Add(si);
            //}
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

        // Convert TMX local string to UTC DateTime (assumes Toronto exchange time)
        private  DateTime ToUtcFromTmxLocal(string s)
        {
            // Try parse with invariant culture (handles "2025-10-24 3:55:00 PM")
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
                local = DateTime.Parse(s); // last resort
            var est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            var et = TimeZoneInfo.ConvertTimeToUtc(unspecified, est);
            return et; // UTC
        }
        public async Task InsertDailyAsync(string symbol, List<TimeSeriesPointItem> points)
        {
            using var conn = new SqlConnection(base.ConnectionString);
            await conn.OpenAsync();

            // Ensure the symbol exists in Market.Symbols
            using (var upsertSymbol = new SqlCommand(
                @"MERGE dbo.Symbols AS T
              USING (SELECT @Symbol AS Symbol) AS S
              ON T.Symbol = S.Symbol
              WHEN NOT MATCHED THEN INSERT (Symbol) VALUES (S.Symbol);", conn))
            {
                upsertSymbol.Parameters.AddWithValue("@Symbol", symbol);
                await upsertSymbol.ExecuteNonQueryAsync();
            }

            // Bulk insert into Market.Quotes using a DataTable (fast, PK prevents dupes)
            var dt = new DataTable();
            dt.Columns.Add("Symbol", typeof(string));
            dt.Columns.Add("CreatedUtc", typeof(DateTime));
            dt.Columns.Add("Price", typeof(decimal));
            dt.Columns.Add("PriceChange", typeof(decimal));
            dt.Columns.Add("PercentChange", typeof(decimal));
            dt.Columns.Add("DayHigh", typeof(decimal));
            dt.Columns.Add("DayLow", typeof(decimal));
            dt.Columns.Add("PrevClose", typeof(decimal));
            dt.Columns.Add("OpenPrice", typeof(decimal));
            dt.Columns.Add("Bid", typeof(decimal));
            dt.Columns.Add("Ask", typeof(decimal));
            dt.Columns.Add("Weeks52High", typeof(decimal));
            dt.Columns.Add("Weeks52Low", typeof(decimal));
            dt.Columns.Add("Volume", typeof(long));

            foreach (var p in points)
            {
                var createdUtc = ToUtcFromTmxLocal(p.dateTime);
                var row = dt.NewRow();
                row["Symbol"] = symbol;
                row["CreatedUtc"] = createdUtc;
                row["Price"] = p.close;     // map close as Price for EOD bars
                row["OpenPrice"] = p.open;
                row["DayHigh"] = p.high;
                row["DayLow"] = p.low;
                row["PrevClose"] = DBNull.Value;     // unknown for historical bar-by-bar (optional)
                row["PriceChange"] = DBNull.Value;   // compute later if needed
                row["PercentChange"] = DBNull.Value; // compute later if needed
                row["Bid"] = DBNull.Value;
                row["Ask"] = DBNull.Value;
                row["Weeks52High"] = DBNull.Value;
                row["Weeks52Low"] = DBNull.Value;
                row["Volume"] = p.volume;
                dt.Rows.Add(row);
            }

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "dbo.Quotes",
                BatchSize = 5000,
                BulkCopyTimeout = 120
            };
            bulk.ColumnMappings.Add("Symbol", "Symbol");
            bulk.ColumnMappings.Add("CreatedUtc", "CreatedUtc");
            bulk.ColumnMappings.Add("Price", "Price");
            bulk.ColumnMappings.Add("PriceChange", "PriceChange");
            bulk.ColumnMappings.Add("PercentChange", "PercentChange");
            bulk.ColumnMappings.Add("DayHigh", "DayHigh");
            bulk.ColumnMappings.Add("DayLow", "DayLow");
            bulk.ColumnMappings.Add("PrevClose", "PrevClose");
            bulk.ColumnMappings.Add("OpenPrice", "OpenPrice");
            bulk.ColumnMappings.Add("Bid", "Bid");
            bulk.ColumnMappings.Add("Ask", "Ask");
            bulk.ColumnMappings.Add("Weeks52High", "Weeks52High");
            bulk.ColumnMappings.Add("Weeks52Low", "Weeks52Low");
            bulk.ColumnMappings.Add("Volume", "Volume");

            try
            {
                await bulk.WriteToServerAsync(dt);
            }
            catch (SqlException ex) when (ex.Number == 2627 /* PK violation */)
            {
                // If re-running for the same date range, duplicates (Symbol,CreatedUtc) will be blocked by PK.
                // For idempotent loads, you can ignore or switch to MERGE-per-row if you need updates.
            }
        }

    }
}
