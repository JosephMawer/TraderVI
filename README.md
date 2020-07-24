# TraderVI

## Resources To Get You Started  

This project started when I found out that wealthsimple is offering zero commission stock trading.  
https://www.wealthsimple.com/en-ca/product/trade/  
 _If you haven't checked out wealthsimple, I highly recommend them_  

I leveraged alpha vantage's free stock market API's and built on top of that (by forking someone elses work).  
https://www.alphavantage.co/  

There are other APIs worth looking into as well, that I have not yet used, such as:  
https://alpaca.markets/  

As a bonus point, wealthsimple is now offering commission free bit coin trading, and alpha vantage has API's that support bitcoin/cryptocurrency. please sign up using my referal link:  
https://www.wealthsimple.com/en-ca/product/crypto?r=UrKDi  
_I fully expect to get into this in this library and add support for cypto trading_  

Another exceptional resource has been tmx money which offers free real time stock data, (for Toronto Stock Exchange).  
https://tmxmoney.com/en/index.html  
_This is the site I 'scrap' to grab real time data for free_

## Goals and Intentions

* Learn and implement algoritms for time series data
* Back test our algorithms to prove they work  
  * _In the context of time-series forecasting, the notion of backtesting refers to the process of assessing the accuracy of a forecasting method using existing historical data. The process is typically iterative and repeated over multiple dates present in the historical data._
* Make money
* Build a system that supports ad hoc data analysis, both historical and real time (streamed data), using a fluent system.
* Build a system that

## Birds Eye View Of The System

There are a couple of moving parts to this system as it needs to do a few different things

* **THE DATABASE**
  * Each night we need gather 'daily' stock data by using the Alpha Vantage API  
  * Remember, this can only run 5 request per min on the free version, and there are over 200 constituents on the TSX market
  * I am thinking of having this implemented as an **Azure function** and have the database live in the cloud; cosmos db?
  * Why store all this data in a database? to support the ad hoc data analysis required to build systems of indicators, the free API key just won't cut it.
    * yes, I could pay for the Alpha Vantage API keys that allow me to query... but, I'm cheap... or poor, or don't really have the code to support paying for that, yet.
  * There will be another table stored for 'intraday' stock data.  This will requested every minute by scraping the tmx money site
    * intraday data is useful to run our algorithms/indicators on short term data to see if the patterns still apply
* **TMX**
  * This is a folder in 'core' that basically scrapes the tmx money site to gather intraday data and store it in our database
  * It will also be used to run algorithms/indicators/systems on live data
  * We will need other methods for collecting real time data on different stock markets, perhaps this is when we justify paying for
  Alpha Vantage API key, or finding a new API, such as Alpaca?
  * Currently, I have only really be interested in trading on TSX because they are companies I know and understand, no exchange fees, and
  tmx site offers free real time data.
* **ALGORITHMS**
  * These will be domain specific algorithms that operate on time series data, you will need to become accustom to technical indicators such as:
    * Support And Resistance Trendlines
	* Reversal Patterns
	* Head And Shoulders
	* Moving Averages
	* Volume
	* Trailing Stop Losses
* **AD HOC ANALYSIS**
  * Okay, so to build up our indicators we need to be able to run what I call 'ad hoc analysis', actually I think that's probably a common term.
  * Query





## Show Me Some Code

Get Started by importing stock data into a local sqlite database so you can perform ad hoc analysis

```csharp
// this shows how to download historical stock data into a local sqlite database which
// can then be used for further analysis with the library. This would typically be the first
// thing you run when you initially try to set up this library.
var request = Core.Utilities.Import.TimeSeries.Daily;
await Core.Utilities.Import.Import.ImportStockData(request);
```

Load all the constituents into memory
_constituents mean the stocks that make up the indice, in this case, TSX_

```csharp
var constituents = await Core.Db.Constituents.GetConstituents();
```

Let's start doing some ad-hoc analysis

```csharp
 // searching all stocks for the head and shoulders pattern using various input
// parameters to define the size and sample frequency of how often we look for the pattern
foreach (var constituent in constituents)
{
   Console.WriteLine($"{constituent.Symbol} : Searching for {constituent.Name}...");
   var stocks = await TimeSeries.GetAllStockDataFor(constituent.Symbol);

   Console.WriteLine("Searching for pattern: Head And Shoulders");
   var result = stocks.RunWindowBasedSampling(SearchPatterns.HeadAndShoulders, windowSize: 7);
   if (result != null)
   {
       ConsoleTable.From(result).Write();
   }

   Console.ReadLine();
   Console.Clear();
}
```









The library uses alpha vantage stuff mostly just to pull in lots of stock data into a local database, at
which point you can run all sorts of analysis on the data. Because I only have a free API key I have limitations imposed on how often I can run queries, I think it's 5 API requests per minute.  
To get around this limitation, I think I will set up some sort of Azure Function that runs at specified times, and during the night, will download
the days stock data for each constituent in TSX.

It uses a TMX library, which is basically a web scraper to grab real time data from tmxmoney.com, a website
that provides real time stock market data for TSX..

GUI stuff is still yet to come.. might go with UWP?? might go web..

High level parts of the system

 - TMX: the web scraper, gathers daily financial info 
 - Daily trader: monitors stocks I am actually invested in to ensure 
 - Alert System: this system scans stocks (from db) daily to check for emerging patterns
 - Watch List: 
 - Watcher:	monitors intraday activity for stocks on the watch list for triggers/buy signals
 - Funds:	monitors the amount of money I have made from investments (kind of like pennywise)
 - Db: the data layer; deals directly with the database

Alerts vs Triggers

Alert: indicates it is time to watch a stock, i.e. add it to the watchlist

trigger: indicates it is time to buy





We need a way to input data (i.e. feed/stream data into our program)
 -- maybe use command line utils? from McMaster

 -- inputs
	-- list of tickers (separated by spaces), ex: lun exa fm
	-- list of switches corresponding to technical indicators to run (as well as parameters for each indicator)



	ALso.. along with some of the other points above/below... this library aims
	to assist in ad hoc data analysis/exploration of stock data...  I know.. R is way better
	at this... but I don't know R...


Ultimately, I would like to feed in a bunch of tickers: lun, eca, appl, etc.
The program then runs a series of indicators on them to determine which is most likely to 


// we need to be able to determine a few things on time series data
1) Trend
2) Supprt levels: bottom/top
3) Reversal Patterns
	- Double Tops and Bottoms


AT a high level, this project is to create a bunch of moving pieces (indicators)
that gather information and report back to some master hub, which than assigns values
to the result of each indicator. Once each indicator has reported back it can tally the results
and try and reach some general positive or negative value indicating if we should buy or not.

-- note this is easy to back test, we just provide time series data between two points in time and let
the engine run all it's indicator tests


IDEAS
- Find recurring price action patterns with known outcomes and train models with that data ??



Core projects involved

ML.NET
AlphaVantage
Abot.WebCrawler
HttpAgilityPack


- Import stock data into local database

- Would it be possible to build a constant feedback loop? i.e. where the model is constantly being
trained with feedback from realtime data - feedback would need to include a multitude of indicators  
**feedback loop with only one indicator would just be guessing

- Need to understand how to prepare data; then I can automate the task of gathering
the data into the appropriate forms

- I will need to build comsumers of stock data; I can feed my data trainer thing a 
large assortment of data from various indices, not just tsx

- I might need to work with the command line trainer tooling to actually build out
my live training engine

What are our goals
	- predict price based on x factors
	- predict trend based on x factors
	- predict trend reversals based on x factors

Predictors ---------------------------- i.e. things we want to predict
Trend | Momentum | Volume? | Volatility 

Indactors -----------------------------
Price | Volume | OBV | RSI | EMA | SMA 
|

 - I need to work with building some models and training them
 - have a core set of functionality capable of reading stock data,
 i.e. price, volume, actions(200sma, ema, rsi, obv, etc.);
 - this data needs to be read in and formatted in a way that we can
 feed our training models
 - likely will need many models predicating many things, i.e. a 
 prediction model for each of granvilles indicators

 Download the visual studio model builder
 https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet/model-builder



 simple backfitting test
 read in stock data for 