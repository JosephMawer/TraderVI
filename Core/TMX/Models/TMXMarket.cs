using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.TMX.Models.Market
{
    public class TMXMarket
    {
        public string symbol { get; set; }
        public string longname { get; set; }
        public double price { get; set; }
        public int volume { get; set; }
        public double? openPrice { get; set; }
        public double? priceChange { get; set; }
        public double? percentChange { get; set; }
        public double? dayHigh { get; set; }
        public double? dayLow { get; set; }
        public double? prevClose { get; set; }
        public string __typename { get; set; }
    }

    public class Data
    {
        public List<TMXMarket> getQuoteForSymbols { get; set; }
    }

    public class Root
    {
        public Data data { get; set; }
    }
}
