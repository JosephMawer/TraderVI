using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace wstrade.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
    //public class Stock
    //{
    //    public List<string> allowed_account_types { get; set; }
    //    public string asset_category { get; set; }
    //    public string avg_daily_volume_last_month { get; set; }
    //    public string country_of_issue { get; set; }
    //    public object description { get; set; }
    //    public string name { get; set; }
    //    public string primary_exchange { get; set; }
    //    public string primary_exchange_country { get; set; }
    //    public object reuters_attributes { get; set; }
    //    public List<object> secondary_exchanges { get; set; }
    //    public string security_type { get; set; }
    //    public string symbol { get; set; }
    //    public string primary_mic { get; set; }
    //}

    //public class Group
    //{
    //    public string created_by { get; set; }
    //    public string description_fr { get; set; }
    //    public string id { get; set; }
    //    public string name_en { get; set; }
    //    public string name_fr { get; set; }
    //    public string updated_by { get; set; }
    //}

    public class Security
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
    }

    public class SecurityRequest
    {
        public int offset { get; set; }
        public int total_count { get; set; }
        [JsonPropertyName("results")]
        public List<Security> Securities { get; set; }
    }


}
