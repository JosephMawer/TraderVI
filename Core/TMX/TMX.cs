using Core.TMX.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Core.TMX
{
    public class TMX
    {


        public async Task<GetQuoteBySymbol> GetStockQuote(string ticker)
        {
            try
            {
                var stockTickerRequest = new GraphQLRequest
                {
                    Query = @"query getQuoteBySymbol($symbol: String, $locale: String) {getQuoteBySymbol(symbol: $symbol, locale: $locale) {   symbol    name    price    priceChange    percentChange    exchangeName    exShortName    exchangeCode    marketPlace    sector    industry    volume    openPrice   dayHigh    dayLow    MarketCap   MarketCapAllClasses   peRatio    prevClose    dividendFrequency    dividendYield    dividendAmount    dividendCurrency    beta    eps    exDividendDate    shortDescription    longDescription   website  email    phoneNumber    fullAddress    employees    shareOutStanding    totalDebtToEquity   totalSharesOutStanding    sharesESCROW    vwap    dividendPayDate    weeks52high    weeks52low    alpha   averageVolume10D    averageVolume30D    averageVolume50D   priceToBook    priceToCashFlow    returnOnEquity    returnOnAssets    day21MovingAvg    day50MovingAvg    day200MovingAvg    dividend3Years    dividend5Years    datatype    __typename  }}",
                    OperationName = "getQuoteBySymbol",
                    Variables = new
                    {
                        symbol = $"{ticker}",
                        locale = "en"
                    }
                };

                // To use NewtonsoftJsonSerializer, add a reference to NuGet package GraphQL.Client.Serializer.Newtonsoft
                var client = new GraphQLHttpClient("https://app-money.tmx.com/graphql", new NewtonsoftJsonSerializer());
                var response = await client.SendQueryAsync<Data>(stockTickerRequest);

                return response.Data.getQuoteBySymbol;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
            }

            return default;
        }

        public async Task<List<Models.Market.TMXMarket>> GetMarketQuote()
        {
            //{ "operationName":"getQuoteForSymbols","variables":{ "symbols":["^TSX","^JX:CA","^COMPX:US","^NYA:US"]},"query":"query getQuoteForSymbols($symbols: [String]) {getQuoteForSymbols(symbols: $symbols) {   symbol    longname   price    volume    openPrice    priceChange    percentChange    dayHigh    dayLow    prevClose    __typename  }}"}
            try
            {
                var stockTickerRequest = new GraphQLRequest
                {
                    Query = @"query getQuoteForSymbols($symbols: [String]) {getQuoteForSymbols(symbols: $symbols) {   symbol    longname   price    volume    openPrice    priceChange    percentChange    dayHigh    dayLow    prevClose    __typename  }}",
                    OperationName = "getQuoteForSymbols",
                    Variables = new
                    {
                        symbols = new[] { "^TSX", "^JX:CA", "^COMPX:US", "^NYA:US" }
                    }
                };

                // To use NewtonsoftJsonSerializer, add a reference to NuGet package GraphQL.Client.Serializer.Newtonsoft
                var client = new GraphQLHttpClient("https://app-money.tmx.com/graphql", new NewtonsoftJsonSerializer());
                var response = await client.SendQueryAsync<Core.TMX.Models.Market.Data>(stockTickerRequest);

                return response.Data.getQuoteForSymbols;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
            }

            return default;
        }


    }
}
