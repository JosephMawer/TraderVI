using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace wstrade.Models
{
    public class LimitPrice
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class AccountValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class MarketValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class AccountHoldValue
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class StopPrice
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class Value
    {
        public int amount { get; set; }
        public string currency { get; set; }
    }

    public class InstantValue
    {
        public int amount { get; set; }
        public string currency { get; set; }
    }

    public class Order
    {
        public string @object { get; set; }
        public int user_id { get; set; }
        public string account_id { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string external_order_batch_id { get; set; }
        public string external_order_id { get; set; }
        public string order_type { get; set; }
        public string status { get; set; }
        public int quantity { get; set; }
        public string external_security_id { get; set; }
        public LimitPrice limit_price { get; set; }
        public DateTime? filled_at { get; set; }
        public AccountValue account_value { get; set; }
        public MarketValue market_value { get; set; }
        public string symbol { get; set; }
        public string account_currency { get; set; }
        public string market_currency { get; set; }
        public AccountHoldValue account_hold_value { get; set; }
        public string security_name { get; set; }
        public string order_sub_type { get; set; }
        public string time_in_force { get; set; }
        public int? fill_fx_rate { get; set; }
        public int? fill_quantity { get; set; }
        public DateTime? perceived_filled_at { get; set; }
        public object completed_at { get; set; }
        public StopPrice stop_price { get; set; }
        public DateTime last_event_time { get; set; }
        public string ip_address { get; set; }
        public bool? use_kafka_consumer { get; set; }
        public bool settled { get; set; }
        public string id { get; set; }
        public string order_id { get; set; }
        public string security_id { get; set; }
        public string bank_account_id { get; set; }
        public object rejected_at { get; set; }
        public object cancelled_at { get; set; }
        public DateTime? accepted_at { get; set; }
        public Value value { get; set; }
        public bool? cancellable { get; set; }
        public InstantValue instant_value { get; set; }
    }

    public class OrderList
    {
        [JsonPropertyName("results")]
        public List<Order> Orders { get; set; }
        public string bookmark { get; set; }
        public List<object> errors { get; set; }
    }
}
