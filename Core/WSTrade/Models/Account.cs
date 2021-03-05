using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace wstrade.Models
{
    public class BuyingPower
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class CurrentBalance
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class WithdrawnEarnings
    {
        public int amount { get; set; }
        public string currency { get; set; }
    }

    public class NetDeposits
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class AvailableToWithdraw
    {
        public double amount { get; set; }
        public string currency { get; set; }
    }

    public class PositionQuantities
    {
    }

    
    public class Account
    {
        //public string @object { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("opened_at")]
        public DateTime OpenedAt { get; set; }

        [JsonPropertyName("deleted_at")]
        public object DeletedAt { get; set; }

        [JsonPropertyName("buying_power")]
        public BuyingPower BuyingPower { get; set; }

        [JsonPropertyName("current_balance")]
        public CurrentBalance CurrentBalance { get; set; }

        [JsonPropertyName("withdrawn_earnings")]
        public WithdrawnEarnings WithdrawnEarnings { get; set; }

        [JsonPropertyName("net_deposits")]
        public NetDeposits NetDeposits { get; set; }

        [JsonPropertyName("available_to_withdraw")]
        public AvailableToWithdraw AvailableToWithdraw { get; set; }

        [JsonPropertyName("base_currency")]
        public string BaseCurrency { get; set; }
        
        [JsonPropertyName("custodian_account_number")]
        public string CustodianAccountNumber { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("last_synced_at")]
        public DateTime LastSyncedAt { get; set; }

        [JsonPropertyName("read_only")]
        public object ReadOnly { get; set; }

        [JsonPropertyName("external_esignature_id")]
        public string ExternalEsignatureId { get; set; }

        [JsonPropertyName("account_type")]
        public string AccountType { get; set; }

        [JsonPropertyName("position_quantities")]
        public PositionQuantities PositionQuantities { get; set; }
    }

    public class AccountList
    {
        [JsonPropertyName("results")]
        public List<Account> Accounts { get; set; }
    }
}
