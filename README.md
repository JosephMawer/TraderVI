This project has mostly started for me to work with TSX market data, though I am sure it can be used
with other markets as it relies on a forked version of AlphaVantage



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