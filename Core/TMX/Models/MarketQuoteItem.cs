using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.TMX.Models
{
    // ---------- Models ----------
    public class QuoteResponse
    {
        public List<Quote> marketActivity { get; set; } = new();
        public List<Quote> futures { get; set; } = new();
        public List<Quote> commodities { get; set; } = new();
    }

    public class Quote
    {
        public int QuoteId { get; set; }
        public string Symbol { get; set; } = "";
        //public string currency { get; set; } = "";
        //public string exchange { get; set; } = "";
        public string longname { get; set; } = "";
        public string shortName { get; set; } = "";
        public decimal? price { get; set; }
        public decimal? priceChange { get; set; }
        public decimal? percentChange { get; set; }
        public decimal? dayHigh { get; set; }
        public decimal? dayLow { get; set; }
        public decimal? prevClose { get; set; }
        public decimal? openPrice { get; set; }
        public decimal? bid { get; set; }
        public decimal? ask { get; set; }
        public decimal? weeks52high { get; set; }
        public decimal? weeks52low { get; set; }

        public override string ToString()
            => $"{Symbol,-8} {price,10:C} ({percentChange,6:N2}%)";
    }
}
