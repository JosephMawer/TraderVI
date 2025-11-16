using Core.Rules;
using Core.TMX;
using Core.TMX.Models;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TraderVI.Core.Repositories;
using TraderVI.Extensions;
using TraderVI.WPF.Helpers;
using TraderVI.WPF.Viewmodels;
using wstrade;
using wstrade.Models;

namespace TraderVI.WPF
{
    public record Security(string symbol, string securityName, string securityId);

 

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        private readonly WSTrade wstrade;
        private readonly TMX tmx;
        private readonly string token_path = @"C:\src\ws_tokens.txt";
        private readonly string watchlistPath = @"C:\src\watchlist.txt";
        private List<Account> accountsList;
        private List<Order> ordersList;
        private Account selectedAccount;
        private List<OrdersViewModel> ordersTable;
        private readonly ITradesRepository tradesRepository;// = new TradesFlatFileRepository();

        #region View Bindings (Properties)
        private ObservableCollection<MarketMoverItem> _marketMoversList;
        public ObservableCollection<MarketMoverItem> MarketMoversList
        {
            get => _marketMoversList;
            set { _marketMoversList = value; OnPropertyRaised(nameof(MarketMoversList)); }
        }

        private ObservableCollection<WatchListViewModel> _watchList;
        public ObservableCollection<WatchListViewModel> WatchList
        {
            get => _watchList;
            set { _watchList = value; OnPropertyRaised(nameof(WatchList)); }
        }

        private ObservableCollection<Trade> _activeTrades;
        public ObservableCollection<Trade> ActiveTrades
        {
            get => _activeTrades;
            set { _activeTrades = value; OnPropertyRaised(nameof(ActiveTrades)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyRaised(string propertyname)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyname));
            }
        }

        private bool _enableLoginForm;
        public bool EnableLoginForm 
        {
            get => _enableLoginForm;
            set { _enableLoginForm = value; OnPropertyRaised(nameof(EnableLoginForm)); }
        }

        private WatchListViewModel _watchListSelectedItem;
        public WatchListViewModel WatchListSelectedItem
        {
            get => _watchListSelectedItem;
            set
            {
                _watchListSelectedItem = value;
                if (WatchListSelectedItem is null)
                {
                    txtSelectedWatchListItemSymbol.Text = "";
                    txtSelectedWatchListItemDescription.Text = "";
                    btnSell.IsEnabled = false;
                }
                else
                {
                    txtSelectedWatchListItemSymbol.Text = WatchListSelectedItem.Symbol;
                    txtSelectedWatchListItemDescription.Text = WatchListSelectedItem.Name;
                    if (ActiveTrades.Any(x => x.Symbol == txtSelectedWatchListItemSymbol.Text))
                        btnSell.IsEnabled = true;
                    else btnSell.IsEnabled = false;
                }
                OnPropertyRaised(nameof(WatchListSelectedItem));
            }
        }
        #endregion

        // default ctor
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            WatchList = new AsyncObservableCollection<WatchListViewModel>();
            wstrade = new WSTrade();
            tmx = new TMX();
            SetGUIDefaults();

            // kick off the long running background processing thread
            Task.Factory.StartNew(() => BackgroundPolling(), TaskCreationOptions.LongRunning).Await();

            if (File.Exists(token_path))
            {
                var tokens = File.ReadAllLines(token_path);
                wstrade.SetTokens(tokens[0], tokens[1]);
                Initialize().Await();
            }

         

            tradesRepository = new TradesFlatFileRepository();
            ActiveTrades = tradesRepository.Get();
        }

        // sets the GUI defaults
        private void SetGUIDefaults()
        {
            //https://stackoverflow.com/questions/979876/set-background-color-of-wpf-textbox-in-c-sharp-code
            loginborder.BorderBrush = Brushes.Red;
            txtAvailableToTrade.Text = "$0.00";
            txtDisplayCount.Text = "20";  // sets the number of rows to be displayed for the 'orders' tab
        }

        // sets all relevant account information
        private async Task Initialize()
        {
            try
            {
                accountsList = await wstrade.GetAccount();
                selectedAccount = accountsList.Single(x => x.AccountType == "ca_tfsa");
                txtAvailableToTrade.Text = $"${selectedAccount.BuyingPower.amount}";
                await SetOrdersList();
                //var currentOrder = await wstrade.GetLatestOrder();
                //trade = new Trade(ticker, currentOrder.limit_price.amount);
                loginborder.BorderBrush = Brushes.Green;
            }
            catch(Exception ex)
            {
                //MessageBox.Show($"There was an error setting retrieving your Wealth Simple Trade Account information, perhaps the token is expired. Try logging in manually. {Environment.NewLine}{ex.Message}");
            }
        }


        #region Buy & Sell Operations
        // this object can be enriched later with more trade information and perhaps become another rich domain model
        public class BuyTransaction
        {
            public string Symbol { get; set; }
            public int Quantity { get; set; } = 10; // for now we set 10.. in the future make this object smarter
        }
        // this object can be enriched later with more trade information and perhaps become another rich domain model
        public class SellTransaction
        {
            public string Symbol { get; set; }
            public int Quantity { get; set; } = 10; // for now we set 10.. in the future make this object smarter
        }

        private bool tradeInProgess = false;
        private async void QuickOrderButtonClick(object sender, RoutedEventArgs e)
            => await Buy(new BuyTransaction() { Symbol = txtSelectedWatchListItemSymbol.Text });

        private async Task Buy(BuyTransaction trade)
        {
            if (tradeInProgess)
            {
                // write to GUI console
                return;
            }

            tradeInProgess = true;
            try
            {
                var symbol = trade.Symbol; //txtSelectedWatchListItemSymbol.Text;
                var security = await wstrade.GetSecurity(symbol);
                var securityId = security.id;

                var stock = await tmx.GetQuoteBySymbol(symbol);

                // todo: use the account object to request if we have available funds...avoid accessing the GUI objects 
                var availableAccountFunds = double.Parse(txtAvailableToTrade.Text[1..]);

                var quantity = trade.Quantity;  //10;   //int.Parse(txtQuickOrderQty.Text);
                var requiredAmount = stock.price * quantity;
                if (requiredAmount > availableAccountFunds)
                {
                    MessageBox.Show("You do not have enough funds to purchase the default quantity (10) of stocks for this company");
                    return;
                }

                // place the order
                var limitOrder = new LimitOrder(OrderSubType.buy_quantity, stock.price, quantity, securityId);
                wstrade.PlaceOrder(limitOrder);

                // start polling for the order status
                bool orderComplete = false;
                while (!orderComplete)
                {
                    var currentOrder = await wstrade.GetLatestOrder();

                    // may need more checks here to ensure we are working with the correct order
                    if (currentOrder.status == "posted" || currentOrder.status == "filled")
                    {
                        var amount = currentOrder.limit_price.amount;
                        ActiveTrades = tradesRepository.Add(symbol, amount, quantity, (DateTime)currentOrder.filled_at);
                        orderComplete = true;
                    }

                    if (currentOrder.status == "rejected")
                    {
                        tradeInProgess = false;
                        return;
                    }
                    await Task.Delay(2000);
                }

                tradeInProgess = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                tradeInProgess = false;
            }
            finally
            {
                await Initialize(); // perhaps think of a better name for this now that it's getting reused; setDefaultGUI ?
            }
        }

        private async void QuickOrderSellButotnClick(object sender, RoutedEventArgs e)
            => await Sell(new SellTransaction() { Symbol = txtSelectedWatchListItemSymbol.Text });
        
        private async Task Sell(SellTransaction trade)
        {
            try
            {
                var positions = await wstrade.GetPositions(); 

                // currently we only use 1 trade at a time
                var position = positions.Single(x => x.stock.symbol == trade.Symbol);
                var securityId = position.quote.security_id;
                var symbol = position.stock.symbol;
                var quantity = position.quantity; // or, sellable_quantity?

                var stock = await tmx.GetQuoteBySymbol(symbol);

                var limitOrder = new LimitOrder(OrderSubType.sell_quantity, stock.price, quantity, securityId);
                wstrade.PlaceOrder(limitOrder);

                ActiveTrades = tradesRepository.Remove(symbol);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                // log to output console//file
            }
            finally
            {
                //await Initialize(); // perhaps think of a better name for this now that it's getting reused; setDefaultGUI ?
                accountsList = await wstrade.GetAccount();
                selectedAccount = accountsList.Single(x => x.AccountType == "ca_tfsa");
                Dispatcher.Invoke(() => txtAvailableToTrade.Text = $"${selectedAccount.BuyingPower.amount}");
            }
        }
        #endregion

        #region Login Form
        // submit login credentials
        private async void loginSubmitClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLoginEmail.Text) || string.IsNullOrWhiteSpace(txtLoginPassword.Text))
            {
                MessageBox.Show("Email AND Password required");
                return;
            }

            // login to ws trade
            var result = await wstrade.Login(txtLoginEmail.Text, txtLoginPassword.Text, txtLoginOtp.Text);
            //var result = await wstrade.OauthLogin(txtLoginEmail.Text, txtLoginPassword.Text); //, txtLoginOtp.Text);
            if (string.IsNullOrWhiteSpace(txtLoginOtp.Text)) {
                MessageBox.Show("Please enter the OTP");
            }
            else
            {
                if (result) {
                    // save the tokens to local storage
                    if (File.Exists(token_path)) File.Delete(token_path);
                    File.WriteAllLines(token_path, new[] { wstrade.access_token, wstrade.refresh_token });

                    await Initialize();

                    loginborder.BorderBrush = Brushes.Green;
                }
                //loginForm.IsOpen = false;
            }
        }
        #endregion

        #region Orders Tab
        // 'Refresh' orders, queries for the orders of the wstrade account
        private async void searchOrdersClick(object sender, RoutedEventArgs e) 
            => await SetOrdersList(int.Parse(txtDisplayCount.Text));

        private async Task SetOrdersList(int orderCount = 20)
        {
            ordersList = await wstrade.GetOrders(orderCount);
            if (ordersList is null)
            {
                // log message
                return;
            }   
            
            var orders = new List<OrdersViewModel>();

            foreach (var order in ordersList)
            {
                if (!(bool)chkShowCancelled.IsChecked && order.status == "cancelled") continue;
                if (order.status == "accepted") continue; // accepted status is used for deposits it seems
                if (!(bool)chkShowRejected.IsChecked && order.status == "rejected") continue;
                
                try
                {
                    var ovm = new OrdersViewModel()
                    {
                        Symbol = order.symbol,
                        Type = order.order_type.Contains("sell") ? "SELL" : "BUY",
                        Status = order.status,
                        Qty = order.fill_quantity == null ? order.quantity : (int)order.fill_quantity,
                        Date = order.filled_at == null ? order.updated_at.ToShortDateString() : ((DateTime)order.filled_at).ToShortDateString()
                    };
                    if (order.order_sub_type == "market")
                    {
                        ovm.Amount = order.market_value.amount / order.quantity;
                    }
                    else
                    {
                        ovm.Amount = (order.limit_price == null) ? 0 : order.limit_price.amount;
                    }
                    ovm.Total = ovm.Amount * ovm.Qty;
                    orders.Add(ovm);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting order object {ex.Message}");
                }
             
            }
            ordersTable = orders;

            // calculate positive/negative trade count
            // var tmpLst = ordersTable;
            // if (tmpLst.First().Type == "BUY") tmpLst.RemoveAt(0);   // don't incude the first item if it's still an open trade
            // if (tmpLst.Last().Type == "SELL") tmpLst.RemoveAt(tmpLst.Count - 1);
            // var totalBuy = tmpLst.Where(x => x.Type == "BUY").Sum(x => x.Amount);
            // var totalSell = tmpLst.Where(x => x.Type == "SELL").Sum(x => x.Amount);
            // var totalEarnings = totalSell - totalBuy;
            // txtTotalEarnings.Text = totalEarnings.ToString("C");



            // set the view list
            lstOrders.ItemsSource = orders;
        }
        #endregion



        private bool afterHours = DateTime.Now.Hour < 9 || DateTime.Now.Hour >= 16;
        public async Task BackgroundPolling()
        {
            while (true)
            {
                try
                {
                    var tmx = new Market();
                    var marketMovers = await tmx.GetMarketSummary(print: false);
                    MarketMoversList = new ObservableCollection<MarketMoverItem>(marketMovers);

                    //var refreshTokenTask = RefreshTokens();
                    var watchListTask = UpdateWatchList();
                    var activeTradesTask = UpdateActiveTrades();
                    var marketActivityTrask = UpdateMarketData();
                    await Task.WhenAll(new[] { watchListTask, activeTradesTask, marketActivityTrask });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in background polling: {ex.Message}");
                }

                if (afterHours)
                {
                    Dispatcher.Invoke(() => txtMarketHours.Text = "Markets are closed!");
                    return;
                }
                await Task.Delay(4000);
            }
        }

     
        private async Task RefreshTokens()
        {
            // acquire new refresh token every 15 minutes or so to stay logged in
        }

        private async Task UpdateActiveTrades()
        {
            if (ActiveTrades is null) return;
            foreach (var trade in ActiveTrades)
            {
                var quote = await tmx.GetQuoteBySymbol(trade.Symbol);
                if (quote is null) continue;
                trade.Price = quote.price;
                if (trade.Sell)
                {
                    SystemSounds.Beep.Play();
                    await Sell(new SellTransaction() { Symbol = trade.Symbol });
                }
            }
        }
        private async Task UpdateMarketData()
        {
            var marketData = await tmx.GetMarketQuote();
            var tsxMarket = marketData.Single(x => x.symbol.Contains("^TSX"));

            Dispatcher.Invoke(() =>
            {
                txtTSX.Text = tsxMarket.longname.Substring(0, 7);
                txtTSXPrice.Text = tsxMarket.price.ToString();
                txtTSXDirection.Text = $"{tsxMarket.priceChange} ({tsxMarket.percentChange})";
                txtTSXDirection.Foreground = (tsxMarket.priceChange < 0) ? Brushes.Red : Brushes.Green;
            });
        }
        #region Watch List
   
        private async Task UpdateWatchList()
        {
            if (!File.Exists(watchlistPath)) return;
            var tickers = File.ReadAllLines(watchlistPath);
            var tmx = new TMX();
            var tasks = new List<Task<GetQuoteBySymbol>>();
            foreach (var ticker in tickers)
            {
                var task = tmx.GetQuoteBySymbol(ticker);
                tasks.Add(task);
            }

            var quotes = (await Task.WhenAll(tasks.AsEnumerable())).ToList();

            // initialize collection for the first time
            if (WatchList == null || WatchList.Count == 0) 
            {
                var tmp = new AsyncObservableCollection<WatchListViewModel>();
                quotes.ForEach(x => tmp.Add(new WatchListViewModel()
                {
                    Symbol = x.symbol,
                    Price = x.price.ToString("C"),
                    PriceChange = x.priceChange?.ToString("C"),
                    PercentChange = x.percentChange.ToString(),
                    Volume = x.volume.ToString(),
                    Name = x.name
                }));

                WatchList = new AsyncObservableCollection<WatchListViewModel>(tmp);
            }
            else // update the collection
            {
                foreach (var quote in quotes)
                {
                    if (quote is null) continue;
                    var item = WatchList.Single(x => x.Symbol == quote.symbol);
                    item.Symbol = quote.symbol;
                    item.Price = quote.price.ToString();
                    item.PriceChange = quote.priceChange.ToString();
                    item.PercentChange = quote.percentChange.ToString();
                    item.Volume = quote.volume.ToString();
                    item.Name = quote.name;
                }
            }
        }

        private async void addSymbolToWatchListClick(object sender, RoutedEventArgs e)
        {
            var symbol = txtSymbolForWatchList.Text;
            if (string.IsNullOrWhiteSpace(symbol)) return;

            // add the symbol to the flat file on disk (current database)
            if (File.Exists(watchlistPath))
            {
                var watchlist = File.ReadAllLines(watchlistPath).ToList();
                watchlist.Add(symbol);
                File.Delete(watchlistPath);
                File.WriteAllLines(watchlistPath, watchlist);
            }
            else
            {
                File.WriteAllLines(watchlistPath, new[] { symbol });
            }

            // update the collection
            var tmx = new TMX();
            var quote = await tmx.GetQuoteBySymbol(symbol);
            WatchList.Add(new WatchListViewModel()
            {
                Symbol = quote.symbol,
                Price = quote.price.ToString("C"),
                PriceChange = quote.priceChange?.ToString("C"),
                PercentChange = quote.percentChange.ToString(),
                Volume = quote.volume.ToString(),
                Name = quote.name
            });
        }

        private void DeleteWatchListItemClick(object sender, RoutedEventArgs e)
        {
            if (WatchListSelectedItem is null) return;
            var symbol = WatchListSelectedItem.Symbol;
            var watchlist = File.ReadAllLines(watchlistPath).ToList();
            watchlist.Remove(symbol);
            //lock (watchlistLock)
            //{
            WatchList.Remove(WatchList.Single(x => x.Symbol == symbol));
            //}
           // IsDeletingFromWatchlist = true;
            File.Delete(watchlistPath);
            File.WriteAllLines(watchlistPath, watchlist);
        }
        #endregion


    }
}
