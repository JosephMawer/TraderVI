using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace wstrade.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
    public class Stock
    {
        public List<string> allowed_account_types { get; set; }
        public string asset_category { get; set; }
        public string avg_daily_volume_last_month { get; set; }
        public string country_of_issue { get; set; }
        public object description { get; set; }
        public string name { get; set; }
        public string primary_exchange { get; set; }
        public string primary_exchange_country { get; set; }
        public object reuters_attributes { get; set; }
        public List<object> secondary_exchanges { get; set; }
        public string security_type { get; set; }
        public string symbol { get; set; }
        public string primary_mic { get; set; }
    }

    public class Group
    {
        public string created_by { get; set; }
        public string description_fr { get; set; }
        public string id { get; set; }
        public string name_en { get; set; }
        public string name_fr { get; set; }
        public string updated_by { get; set; }
    }

    public class StartOfDayBookValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class StartOfDayMarketBookValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class BookValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class MarketBookValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class Sparkline
    {
        public string date { get; set; }
        public string time { get; set; }
        public string currency { get; set; }
        public string adjusted_price { get; set; }
        public string security_id { get; set; }
        public string data_source { get; set; }
        public string close { get; set; }
    }

    public class Quote
    {
        public string @object { get; set; }
        public string security_id { get; set; }
        public string amount { get; set; }
        public string currency { get; set; }
        public string ask { get; set; }
        public int ask_size { get; set; }
        public string bid { get; set; }
        public int bid_size { get; set; }
        public string high { get; set; }
        public int last_size { get; set; }
        public string low { get; set; }
        public string open { get; set; }
        public int volume { get; set; }
        public string previous_close { get; set; }
        public DateTime previous_closed_at { get; set; }
        public DateTime quote_date { get; set; }
    }

    public class TodaysEarningsBaselineValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class Position
    {
        public string @object { get; set; }
        public string id { get; set; }
        public string currency { get; set; }
        public string security_type { get; set; }
        public bool ws_trade_eligible { get; set; }
        public object ws_trade_ineligibility_reason { get; set; }
        public bool cds_eligible { get; set; }
        public string active_date { get; set; }
        public object inactive_date { get; set; }
        public bool active { get; set; }
        public bool buyable { get; set; }
        public bool sellable { get; set; }
        public object status { get; set; }
        public Stock stock { get; set; }
        public List<Group> groups { get; set; }
        public List<string> allowed_order_subtypes { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string external_security_id { get; set; }
        public int user_id { get; set; }
        public string account_id { get; set; }
        public string book_value_currency { get; set; }
        public int start_of_day_quantity { get; set; }
        public StartOfDayBookValue start_of_day_book_value { get; set; }
        public string start_of_day_book_value_currency { get; set; }
        public StartOfDayMarketBookValue start_of_day_market_book_value { get; set; }
        public string start_of_day_market_book_value_currency { get; set; }
        public int quantity { get; set; }
        public int sellable_quantity { get; set; }
        public BookValue book_value { get; set; }
        public MarketBookValue market_book_value { get; set; }
        public string market_book_value_currency { get; set; }
        public List<Sparkline> sparkline { get; set; }
        public Quote quote { get; set; }
        public TodaysEarningsBaselineValue todays_earnings_baseline_value { get; set; }
    }

    public class PositionRequest
    {
        [JsonPropertyName("results")]
        public List<Position> Positions { get; set; }
    }
}
