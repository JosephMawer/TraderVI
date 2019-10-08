/****** Script for SelectTopNRows command from SSMS  ******/

select * from DailyStock where Ticker = 'SU' order by [Date] desc

-- get list of industries
select distinct([Name]) from IndiceSummary

-- get 52 week high
select max([Close]) 
from (select [Close] from [StocksDB].[dbo].[DailyStock]
      where [Ticker] = 'ARE' and [Date] >=dateadd(week,-52, '2019-10-5') and [Date] <= '2019-10-5') as d

  -- returns the number of stocks that have more than 100 data points
  select count(*) from 
		  (select count(Ticker) as count,Ticker
		   from DailyStock
		   group by Ticker) as tm
  where tm.count > 100


  -- returns top 5 movers in terms of volume
  select top(5) stock.[Date],stock.[Ticker],stock.[Open],stock.[Close],
				stock.[Volume],stock.[High],stock.[Low],symbol.[Name] from DailyStock as stock
	inner join Symbols as symbol on stock.Ticker = symbol.Symbol
  where [Date] >= '2019-10-7'
  order by Volume desc