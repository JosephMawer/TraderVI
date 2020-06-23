using System;
using System.Collections.Generic;
using System.Text;
using AlphaVantage.Net.Core;
using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.Indicators;
using AlphaVantage.Net.Stocks.TimeSeries;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Console;

namespace Sandbox
{
    // this class has been copied and pasted from an old console app...
    // to run this, simple use the 'RunMain' function call
    public class GranvillesGame
    {
            private static DataTable dt;
            private const string apiKey = "6IQSWE3D7UZHLKTB";
            private static readonly AlphaVantageStocksClient client = new AlphaVantageStocksClient(apiKey);
            private static readonly AlphaVantageIndicatorClient indicator = new AlphaVantageIndicatorClient(apiKey);
            private static readonly string OutputFilePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            #region Helper methods
            private static void PauseAndClear()
            {
                ReadLine();
                Clear();
            }
            private static DataTable MakeParentTable()
            {
                // Create a new DataTable.
                System.Data.DataTable table = new DataTable("ParentTable");
                // Declare variables for DataColumn and DataRow objects.
                DataColumn column;
                DataRow row;

                // Create new DataColumn, set DataType, 
                // ColumnName and add to DataTable.    
                //column = new DataColumn();
                //column.DataType = System.Type.GetType("System.Int32");
                //column.AutoIncrement = true;
                //column.AutoIncrementSeed = 1;
                //column.ColumnName = "id";
                //column.ReadOnly = true;
                //column.Unique = true;
                //// Add the Column to the DataColumnCollection.
                //table.Columns.Add(column);

                // Create second column.
                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "Date",
                    Unique = true,
                    AutoIncrement = false,
                    ReadOnly = false,

                };
                table.Columns.Add(column);

                // Create third column
                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "Close",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "SMA200",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "SMA50",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "SMA9",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "OBV",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "RSI",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                column = new DataColumn
                {
                    DataType = System.Type.GetType("System.String"),
                    ColumnName = "EMA",
                    ReadOnly = false
                };
                table.Columns.Add(column);

                // Make the ID column the primary key column.
                DataColumn[] PrimaryKeyColumns = new DataColumn[1];
                PrimaryKeyColumns[0] = table.Columns["Date"];
                table.PrimaryKey = PrimaryKeyColumns;


                return table;
            }

            /// <summary>
            /// Helper method to create a csv file from a data table
            /// </summary>
            /// <param name="table"></param>
            /// <returns></returns>
            private static string DumpDataTable(DataTable table)
            {
                string data = string.Empty;
                StringBuilder sb = new StringBuilder();

                if (null != table && null != table.Rows)
                {
                    foreach (DataRow dataRow in table.Rows)
                    {
                        foreach (var item in dataRow.ItemArray)
                        {
                            sb.Append(item);
                            sb.Append(',');
                        }
                        sb.AppendLine();
                    }

                    data = sb.ToString();
                }
                return data;
            }

            /// <summary>
            /// Helper method that adds a field to the data table based on the column name you pass and the date of the indicator point.  If the date
            /// does not exist in the table, nothing is added.
            /// </summary>
            /// <param name="colName"></param>
            /// <param name="s"></param>
            private static void AddField(string colName, IndicatorPoint s)
            {
                var row = dt.Rows.Find(((DateTime)s.Time).ToShortDateString());
                if (row == null) return;
                row[colName] = s.Value;
            }

            /// <summary>
            /// Helper method that writes the data table to the console
            /// </summary>
            private static void WriteToConsole()
            {
                Write($"{"Date",-20}{"Close",-20}{"SMA200",-20}{"SMA50",-20}{"SMA9",-20}{"OBV",-20}{"RSI",-20}{Environment.NewLine}");
                foreach (DataRow dataRow in dt.Rows)
                {
                    foreach (var item in dataRow.ItemArray)
                    {
                        Write($"{item,-20}");
                    }
                    Write(Environment.NewLine);
                }
            }
            #endregion


            /// <summary>
            /// This program is used to calculate Granvilles 56 daily indicates plus maybe some other stuff.. depending on what I decide :)
            /// </summary>
            /// <param name="args"></param>
            static async Task RunMain(string[] args)
            {
                WriteLine("https://web.tmxmoney.com/marketsca.php?locale=EN" + Environment.NewLine);
                var ticker = "tsx:lun";
                WriteLine($"Collecting time-series information for: {ticker}{Environment.NewLine}");
                dt = MakeParentTable();

                var prices = await client.RequestDailyTimeSeriesAsync($"{ticker}", TimeSeriesSize.Compact);
                foreach (var currentStock in prices.DataPoints)
                {
                    DataRow row = dt.NewRow();
                    row["Date"] = currentStock.Time.ToShortDateString();
                    row["Close"] = currentStock.ClosingPrice.ToString();
                    dt.Rows.Add(row);
                }
                try
                {
                    //StockIndicator sma200 = await indicator.RequestTechnicalIndicatorAsync(ApiFunction.SMA, $"{ticker}", IndicatorSize.Daily, 200, IndicatorSeriesType.Close);
                    //foreach (var s in sma200.DataPoints)
                    //{
                    //    AddField("SMA200", s);
                    //}

                    StockIndicator sma50 = await indicator.RequestTechnicalIndicatorAsync(ApiFunction.SMA, $"{ticker}", IndicatorSize.Daily, 50, IndicatorSeriesType.Close);
                    foreach (var s in sma50.DataPoints)
                    {
                        AddField("SMA50", s);
                    }

                    StockIndicator sma9 = await indicator.RequestTechnicalIndicatorAsync(ApiFunction.SMA, $"{ticker}", IndicatorSize.Daily, 9, IndicatorSeriesType.Close);
                    foreach (var s in sma9.DataPoints)
                    {
                        AddField("SMA9", s);
                    }


                    StockIndicator rsi = await indicator.RequestTechnicalIndicatorAsync(
                        ApiFunction.RSI, $"{ticker}", IndicatorSize.Daily, 14, IndicatorSeriesType.Close);

                    foreach (var s in rsi.DataPoints)
                    {
                        AddField("RSI", s);
                    }

                    StockIndicator obv = await indicator.RequestTechnicalIndicatorAsync(ApiFunction.OBV, $"{ticker}", IndicatorSize.Daily, 200, IndicatorSeriesType.Close);
                    foreach (var s in obv.DataPoints)
                    {
                        AddField("OBV", s);
                    }

                    StockIndicator ema = await indicator.RequestTechnicalIndicatorAsync(ApiFunction.EMA, $"{ticker}", IndicatorSize.Daily, 9, IndicatorSeriesType.Close);
                    foreach (var s in ema.DataPoints)
                    {
                        AddField("EMA", s);
                    }

                    var results = FindCrossOver(sma9.DataPoints.ToArray(), sma50.DataPoints.ToArray()).Take(1);
                    foreach (var result in results)
                        WriteLine(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }








                // Write to console
                WriteToConsole();

                // Write to file
                //var path = Path.Combine(OutputFilePath, $"{ticker.Replace(":","-")}.csv");
                //File.WriteAllText(path, DumpDataTable(dt));


                WriteLine("Finished...");
                ReadLine();
            }





            static StockDataPoint previous = null;
            private static long GetOnBalanceVolume(StockDataPoint current)
            {
                if (previous == null) return 0;

                if (current.ClosingPrice > previous.ClosingPrice)
                {
                    // total daily volume is added to a cumulative total whenever the stock price closes higher than the day before
                    return current.Volume + previous.Volume;
                }
                else if (current.ClosingPrice < previous.ClosingPrice)
                {
                    //                ... is subtracted whenever stock price closes lower than day before
                    return current.Volume - previous.Volume;
                }
                else
                {
                    // If the 
                    return current.Volume;
                }
            }

            private struct XOver
            {
                /// <summary>
                /// Indicates if the crossover is going up or down
                /// </summary>
                public bool Direction { get; set; }

                /// <summary>
                /// Indicates the date the crossover occured
                /// </summary>
                public DateTime Date { get; set; }

                // expose an array of point types, to contain the actual price data of both sma's approximate to where the cross over occured
            }

            /// <summary>
            /// when passing data for the sma, be sure to call the reverse method so that data is passed oldest to newest
            /// </summary>
            /// <param name="sma1"></param>
            /// <param name="sma2"></param>
            /// <returns></returns>
            private static string[] FindCrossOver(IndicatorPoint[] sma1, IndicatorPoint[] sma2)
            {
                // some considerations
                // 1) we need to traverse the time series in the correct order, i.e. ensuring we go from the oldest to the newest
                // 2) we need to consider what type of crossover we're looking for, i.e. is sma1 a, ex. 9 day moving average, and we are watching to see when this crosses over sma2? or vise versa
                // 3) space/time complexity = O(n)  
                // 4) ideally, this will work with a stream of data and constantly be able ot update itself as new data comes in

                List<string> results = new List<string>();
                // take the smaller of the two lengths
                int length = (sma1.Length > sma2.Length) ? sma2.Length : sma1.Length;

                // step 1 - determine if the short term indicator is currently lower or higher
                bool isBelow = !(sma1[0].Value > sma2[0].Value);
                for (int i = 0; i < length; i++)
                {
                    if (isBelow)
                    {
                        // if the short term indicator is lower than long term, we're only need to check when it increases past the sma2
                        if (sma1[i].Value > sma2[i].Value)
                        {
                            results.Add($"SMA9 crossed the SMA50 at {((DateTime)sma1[i].Time).ToShortDateString()}, {(int)DateTime.Now.Subtract(((DateTime)sma1[i].Time)).TotalDays} ago, going down!");
                            isBelow = false;
                        }
                    }
                    else
                    {
                        // look for when it crosses below
                        if (sma1[i].Value < sma2[i].Value)
                        {
                            results.Add($"SMA9 crossed the SMA50 at {((DateTime)sma1[i].Time).ToShortDateString()}, {(int)DateTime.Now.Subtract(((DateTime)sma1[i].Time)).TotalDays} ago,  going up!");
                            isBelow = true;
                        }
                    }
                }

                return results.ToArray();
            }


            /// <summary>
            /// tries to determine the trend of a collection of points
            /// </summary>
            /// <param name="points"></param>
            /// <returns></returns>
            private static string GetTrend(float[] points)
            {
                // 1) the direction of the trend, i.e. up or down
                // 2) the rate of increase?
                // 3) the length of time, i.e. duration of the trend
                // 4) 


                // best is to pass in the data table, specifying the index



                return "";
            }

            public static void GetSMA()
            {
                // this is the old way of getting the SMA...


                var coreClient = new AlphaVantageCoreClient();

                string key = apiKey;

                // retrieve stocks batch quoutes of Apple Inc. and Facebook Inc.:
                var query = new Dictionary<string, string>()
            {
                { "symbol", "MSFT" },
                { "interval", "weekly" },
                { "time_period", "10" },
                { "series_type", "open" }
            };
                var deserialisedResponse = coreClient.RequestApiAsync(key, ApiFunction.SMA, query).Result;
                foreach (var obj in deserialisedResponse)
                {
                    Console.WriteLine($"{obj.Key} - {obj.Value}");
                }
            }

            public static void GetIndicesAverages()
            {
                // Start by getting the averages
                Console.Write("Enter TSX: ");
                var tsx = float.Parse(Console.ReadLine());

                Console.Write("Enter TSXV: ");
                var tsxv = float.Parse(Console.ReadLine());

                Console.Write("Enter DJIA: ");

                Console.Write("Enter NASDAQ: ");
                var nasdaq = float.Parse(Console.ReadLine());

                Console.Write("Enter the NYSE closing pric: ");
                var nyse = float.Parse(Console.ReadLine());



            }

            //public static Task CalculateOnBalanceVolume(string ticker)
            //{
            //    StockTimeSeries timeSeries = await client.RequestDailyTimeSeriesAsync($"tsx:{ticker}", TimeSeriesSize.Compact, adjusted: true);
            //    Print(timeSeries.DataPoints);
            //    foreach (var current in timeSeries.DataPoints.Reverse().Take(2))
            //    {
            //        var obv = GetOBV(current);

            //        Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

            //        Console.WriteLine($"{current.Time.ToShortDateString(),-20} {current.ClosingPrice,-20} {obv}");

            //        // Set the previous data point
            //        previous = current;
            //    }
            //}
        
    }
}
