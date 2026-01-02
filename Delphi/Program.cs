using Core.ML;
using Delphi.Runtime;
using System;
using System.Collections.Generic;

Console.WriteLine("The Oracle Of Delphi");


var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();
//•	then feed it per-ticker List<DailyBar> history and call engine.Evaluate(history).


var history = new List<DailyBar>();



engine.Evaluate(history);

//// Use this app to load historical data and get a prediction

//HeadAndShouldersTrainer.TrainOnSampleData();


//// load our stock data
//// Called when you have the latest 30 bars (e.g., at end of day or intraday)


//var last30Bars = GetLast30BarsForSymbol("AAPL"); // your code
//var liveWindow = BuildLiveWindow(last30Bars);

//var pred = HsClassifierRuntime.Predict(liveWindow);

//Console.WriteLine($"Head-and-shoulders? {pred.PredictedLabel}, Prob={pred.Probability:0.###}");