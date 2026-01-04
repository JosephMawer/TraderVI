using Core.ML;
using Core.TMX.Models;
using Core.TMX.Models.Domain;
using Core.TMX.Models.Dto;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;

namespace Core.Db
{
    public class TickerInfo
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Exchange { get; set; }
    }

    public class QuoteRepository : SQLBase
    {
        public QuoteRepository() : base("[dbo].[Ticker]", "[Ticker],[Name],[Exchange]") { }

        /// <summary>
        /// Gets a list of symbols
        /// </summary>
        /// <param name="Filter">Optional WHERE clause filter (e.g., "WHERE Exchange = 'TSX'")</param>
        /// <returns>An ordered list of <see cref="TickerInfo"/></returns>
        public async Task<List<TickerInfo>> GetSymbols(string Filter = null)
        {
            var sql = $"SELECT {Fields} FROM {DbName}";
            if (!string.IsNullOrEmpty(Filter))
                sql += $" {Filter}";
            sql += " ORDER BY [Symbol]";

            var lst = new List<TickerInfo>();

            using (var con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (var cmd = new SqlCommand(sql, con))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lst.Add(new TickerInfo
                            {
                                Symbol = reader.GetString(0),
                                Name = reader.GetString(1),
                                Exchange = reader.GetString(2)
                            });
                        }
                    }
                }
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
            var query = $"INSERT INTO {DbName} VALUES (@Symbol, @Name, @Exchange)";
            using (var con = new SqlConnection(ConnectionString))
            {
                await con.OpenAsync();
                using (var cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@Symbol", symbol);
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@Exchange", exchange);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Convert TMX local string to UTC DateTime (assumes Toronto exchange time)
        /// </summary>
        private DateTime ToUtcFromTmxLocal(string s)
        {
            // Try parse with invariant culture (handles "2025-10-24 3:55:00 PM")
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
                local = DateTime.Parse(s); // last resort
            var est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            var et = TimeZoneInfo.ConvertTimeToUtc(unspecified, est);
            return et; // UTC
        }

        /// <summary>
        /// Inserts TMX time series data into dbo.Quotes table
        /// </summary>
        public async Task InsertDailyAsync(string symbol, List<TmxTimeSeriesPointDto> points)
        {
            using var conn = new SqlConnection(base.ConnectionString);
            await conn.OpenAsync();

            // Ensure the symbol exists in dbo.Symbols
            using (var upsertSymbol = new SqlCommand(
                @"MERGE dbo.Symbols AS T
                  USING (SELECT @Symbol AS Symbol) AS S
                  ON T.Symbol = S.Symbol
                  WHEN NOT MATCHED THEN INSERT (Symbol) VALUES (S.Symbol);", conn))
            {
                upsertSymbol.Parameters.AddWithValue("@Symbol", symbol);
                await upsertSymbol.ExecuteNonQueryAsync();
            }

            // Bulk insert into dbo.Quotes using a DataTable (fast, PK prevents dupes)
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

        /// <summary>
        /// Inserts daily OHLCV bars into dbo.DailyBars table using MERGE for idempotent upsert
        /// </summary>
        public async Task InsertDailyBarsAsync(string symbol, List<OhlcvBar> bars)
        {
            if (bars == null || bars.Count == 0)
                return;

            using var conn = new SqlConnection(base.ConnectionString);
            await conn.OpenAsync();

            // Use MERGE for upsert behavior (SQL Server equivalent of INSERT OR REPLACE)
            const string mergeSql = @"
                MERGE dbo.DailyBars AS target
                USING (SELECT @Symbol AS Symbol, @Date AS Date, @Open AS [Open], 
                              @High AS High, @Low AS Low, @Close AS [Close], @Volume AS Volume) AS source
                ON (target.Symbol = source.Symbol AND target.Date = source.Date)
                WHEN MATCHED THEN 
                    UPDATE SET [Open] = source.[Open], High = source.High, Low = source.Low, 
                               [Close] = source.[Close], Volume = source.Volume
                WHEN NOT MATCHED THEN
                    INSERT (Symbol, Date, [Open], High, Low, [Close], Volume)
                    VALUES (source.Symbol, source.Date, source.[Open], source.High, source.Low, source.[Close], source.Volume);";

            using (var cmd = new SqlCommand(mergeSql, conn))
            {
                // Prepare parameters once, reuse for each bar
                cmd.Parameters.Add("@Symbol", SqlDbType.VarChar, 10);
                cmd.Parameters.Add("@Date", SqlDbType.Date);
                cmd.Parameters.Add("@Open", SqlDbType.Real);
                cmd.Parameters.Add("@High", SqlDbType.Real);
                cmd.Parameters.Add("@Low", SqlDbType.Real);
                cmd.Parameters.Add("@Close", SqlDbType.Real);
                cmd.Parameters.Add("@Volume", SqlDbType.BigInt);

                foreach (var bar in bars)
                {
                    cmd.Parameters["@Symbol"].Value = symbol;
                    cmd.Parameters["@Date"].Value = bar.TimestampUtc.Date;
                    cmd.Parameters["@Open"].Value = bar.Open;
                    cmd.Parameters["@High"].Value = bar.High;
                    cmd.Parameters["@Low"].Value = bar.Low;
                    cmd.Parameters["@Close"].Value = bar.Close;
                    cmd.Parameters["@Volume"].Value = bar.Volume;

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Retrieves daily bars for a specific symbol, optionally filtered by start date.
        /// </summary>
        public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime? startDate = null)
        {
            var sql = @"
SELECT [Date], [Open], High, Low, [Close], Volume
FROM [dbo].[DailyBars]
WHERE [Symbol] = @Symbol";

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol }
            };

            if (startDate.HasValue)
            {
                sql += " AND [Date] >= @StartDate";
                parameters.Add(new SqlParameter("@StartDate", SqlDbType.Date) { Value = startDate.Value.Date });
            }

            sql += " ORDER BY [Date] ASC";

            return await ExecuteReaderAsync(sql, parameters, reader => new DailyBar
            {
                Date = reader.GetDateTime(0),
                Open = reader.GetFloat(1),
                High = reader.GetFloat(2),
                Low = reader.GetFloat(3),
                Close = reader.GetFloat(4),
                Volume = reader.GetInt64(5)
            });
        }

    }
}