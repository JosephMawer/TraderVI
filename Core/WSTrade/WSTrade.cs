using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using wstrade.Models;

namespace wstrade
{
    public enum OrderSubType
    {
        buy_quantity,
        sell_quantity,
        limit
    }
    public static class TradeConstants
    {
        public static string AccountId = "tfsa_nfjdykg2";
    }

    public class LimitOrder
    {
        public LimitOrder(OrderSubType orderType, double limitPrice, int quantity, string securityId)
        {
            limit_price = limitPrice;
            this.quantity = quantity;
            order_type = orderType.ToString();
            order_sub_type = "limit";
            market_value = decimal.Round((decimal)limitPrice * (decimal)quantity, 2);
            time_in_force = "day";
            security_id = securityId;
        }
        public string account_id { get; } = TradeConstants.AccountId;   //"tfsa_nfjdykg2";
        public int quantity { get; } //= 10;
        public string security_id { get; } //= "sec-s-438efd2a533241c98f42c8f33a5d9e2e";
        public string order_type { get; } //= "sell_quantity";
        public string order_sub_type { get; }
        public string time_in_force { get; } //= "day";
        public decimal market_value { get; } //109.4;
        public double limit_price { get; } //= 10.94;
    }

    public class Credentials
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string OTP { get; set; }
    }

    public class Credentials2
    {
        public string email { get; set; }
        public string password { get; set; }
        public string otp_claim { get; set; }
        public string grant_type { get; set; } = "password";
        public bool skip_provision { get; set; } = true;
        public string scope { get; set; } = "invest.read invest.write mfda.read mfda.write mercer.read mercer.write trade.read trade.write empower.read empower.write tax.read tax.write";
        public string client_id { get; set; } = "4da53ac2b03225bed1550eba8e4611e086c7b905a3855e6ed12ea08c246758fa";
    }

    public class WSTrade
    {
        private const string AccountId = "tfsa_nfjdykg2";
        private const string base_api = "https://trade-service.wealthsimple.com";
        private readonly HttpClient http;

        public string access_token { get; set; }
        public string refresh_token { get; set; }

        // default ctor
        public WSTrade()
        {
            http = new HttpClient();
            http.BaseAddress = new Uri(base_api);
            
        }

        // allows reusing existing tokens to avoid logging in every time
        public void SetTokens(string accessToken, string refreshToken)
        {
            access_token = accessToken;
            refresh_token = refreshToken;
            //http.DefaultRequestHeaders.Add("Authorization", access_token);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
            //http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "__cfduid=dcd3acd17a07f4c403768b6c81db8be311613975886; __cfruid=3e49986012264fd0b90c5fd589652e207800de99-1614363696; wssdi=6140a52756d635bbee98b4019a4431b1");

        }

        // call this method a second time once you get the otp
        public async Task<bool> Login(string email, string password, string otp = "")
        {
            var credentials = new Credentials() { Email = email, Password = password, OTP = otp };
            var result = await http.PostAsJsonAsync("/auth/login", credentials);
            
            // extract the access token and refresh token from headers
            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                access_token = result.Headers.GetValues("X-Access-Token").First();
                refresh_token = result.Headers.GetValues("X-Refresh-Token").First();
                http.DefaultRequestHeaders.Clear();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}");
                //http.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}");
                //http.DefaultRequestHeaders.Add("Content-Type", "application/json");
                //http.DefaultRequestHeaders.Add("Host", "trade-service.wealthsimple.com");
                //http.DefaultRequestHeaders.Add("Cookie", "__cfduid = dcd3acd17a07f4c403768b6c81db8be311613975886; __cfruid = 3e49986012264fd0b90c5fd589652e207800de99 - 1614363696; wssdi = 6140a52756d635bbee98b4019a4431b1");

                return true;
            }
            return false;
        }

        public async Task<bool> OauthLogin(string email, string password)
        {
            var body = new Credentials2() { email = email, password = password };
            var result = await http.PostAsJsonAsync("https://api.production.wealthsimple.com/v1/oauth/token", body);

            // extract the access token and refresh token from headers
            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                access_token = result.Headers.GetValues("X-Access-Token").First();
                refresh_token = result.Headers.GetValues("X-Refresh-Token").First();
                http.DefaultRequestHeaders.Clear();
                http.DefaultRequestHeaders.Add("Authorization", access_token);
                return true;
            }
            return false;
        }

        public async Task<List<Account>> GetAccount()
        {
            // GET https://trade-service.wealthsimple.com/account/list
            try
            {
                var account = await http.GetFromJsonAsync<AccountList>("/account/list");
                return account.Accounts;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                return null;
            }
           
        }

        public async Task<Order> GetLatestOrder()
        {
            var orders = await GetOrders();
            return orders.First();
        }

        public async Task<List<Order>> GetOrders(int limit = 20)
        {
            try
            {
                // the api only seems to accept a limit of 20.. so we have to work with that.
                var accountList = new List<Order>();
                var account = await http.GetFromJsonAsync<OrderList>($"/account/activities?account_ids=&limit=20");//("/orders");

                accountList.AddRange(account.Orders);

                var bookmark = $"&bookmark={account.bookmark}";
                if (limit > 20)
                {
                    var result = (decimal)limit / (decimal)20;
                    var loops = (int)Math.Round(result) == 1 ? 1 : (int)Math.Round(result);
                    var remainingLimit = limit - 20;
                    for (int i = 0; i < loops; i++)
                    {
                        var remaining = await http.GetFromJsonAsync<OrderList>($"/account/activities?account_ids={bookmark}&limit=20");//("/orders");
                        accountList.AddRange(remaining.Orders.Take(remainingLimit));
                    }
                }

                return accountList;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                return null;
            }
        }

        public async Task<List<Position>> GetPositions()
        {
            //https://trade-service.wealthsimple.com/account/positions
            var request = await http.GetFromJsonAsync<PositionRequest>("/account/positions");
            return request.Positions;
        }
     

        public void PlaceOrder(LimitOrder order)
        {

            if (DateTime.Now.Year < 9 && DateTime.Now.Year > 16)
            {
                return;
            }
            var jsonBody = "{\"account_id\":\"tfsa-nfjdykg2\",\"quantity\":" + order.quantity + ",\"security_id\":\"" + order.security_id + "\",\"order_type\":\"" + order.order_type + "\",\"order_sub_type\":\"limit\",\"time_in_force\":\"day\",\"market_value\":" + order.market_value + ",\"limit_price\":" + order.limit_price + "}";


            var objAsJson = JsonConvert.SerializeObject(order);
            var client = new RestClient("https://trade-service.wealthsimple.com/orders");
            
            // 2025 Oct 31 - temporarily commented this out
            
            //client.Timeout = -1;
            //var request = new RestRequest(Method.POST);
            //request.AddHeader("Authorization", $"Bearer {access_token}");
            //request.AddHeader("Content-Type", "application/json");
            ////request.AddHeader("Cookie", "__cfduid=dcd3acd17a07f4c403768b6c81db8be311613975886; __cfruid=3e49986012264fd0b90c5fd589652e207800de99-1614363696; wssdi=6140a52756d635bbee98b4019a4431b1");
            //request.AddParameter("application/json", jsonBody, ParameterType.RequestBody);
            //IRestResponse response = client.Execute(request);


            return;

            //var response = await http.PostAsJsonAsync("https://trade-service.wealthsimple.com/orders", order);
            //http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            //http.DefaultRequestHeaders.Add("Content-Type", "application/json");
            //http.DefaultRequestHeaders.Add("Cookie", "__cfduid = dcd3acd17a07f4c403768b6c81db8be311613975886; __cfruid = 3e49986012264fd0b90c5fd589652e207800de99 - 1614363696; wssdi = 6140a52756d635bbee98b4019a4431b1");

            
            //var jsonString = "{\"account_id\":\"tfsa - nfjdykg2\",\"quantity\":10,\"security_id\":\"sec - s - 438efd2a533241c98f42c8f33a5d9e2e\",\"order_type\":\"buy_quantity\",\"order_sub_type\":\"limit\",\"time_in_force\":\"day\",\"market_value\":116.7,\"limit_price\":11.67}";

            //var content = new StringContent(objAsJson, Encoding.UTF8, "application/json");
            //var jsonString = "{\"account_id\":\"tfsa - nfjdykg2\",\"quantity\":10,\"security_id\":\"sec - s - 438efd2a533241c98f42c8f33a5d9e2e\",\"order_type\":\"buy_quantity\",\"order_sub_type\":\"limit\",\"time_in_force\":\"day\",\"market_value\":116.7,\"limit_price\":11.67}";

            //HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://trade-service.wealthsimple.com/orders");
            //request.Content.Headers.Remove("Content-Type");
            //request.Content = new StringContent(objAsJson,
                                                //Encoding.UTF8,
                                                //"application/json");//CONTENT-TYPE header

            //await http.SendAsync(request)
            //      .ContinueWith(responseTask =>
            //      {
            //          Debug.WriteLine("Response: {0}", responseTask.Result);
            //      });
            //var response = await http.PostAsJsonAsync("https://trade-service.wealthsimple.com/orders", content);
            // Do the actual request and await the response
            //var httpResponse = await http.PostAsync("https://trade-service.wealthsimple.com/orders", content);

            // POST https://trade-service.wealthsimple.com/orders

            //        {
            //            "security_id": "sec-s-76a7155242e8477880cbb43269235cb6",
            //    "limit_price": 5.00,
            //    "quantity": 100,
            //    "order_type": "buy_quantity",
            //    "order_sub_type": "limit",
            //    "time_in_force": "day"
            //} 
        }

        public async Task<Security> GetSecurity(string symbol)
        {
            //https://trade-service.wealthsimple.com/securities?allow_ineligible_security=false&query=wprt
            var request = await http.GetFromJsonAsync<SecurityRequest>($"/securities?allow_ineligible_security=false&query={symbol.ToLower()}");
            return request.Securities.Single(x => x.stock.symbol == symbol);
        }
    }
}
