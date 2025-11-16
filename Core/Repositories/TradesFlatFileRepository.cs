////using AngleSharp.Html;
////using AngleSharp.Text;
//using System;
//using System.Collections.Generic;
//using System.Collections.ObjectModel;
//using System.ComponentModel;
//using System.IO;
//using System.Linq;

//namespace TraderVI.Core.Repositories
//{
//    public class NotifyChange : INotifyPropertyChanged
//    {
//        public event PropertyChangedEventHandler PropertyChanged;
//        protected void OnPropertyChanged(string propertyname)
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
//        }
//    }
//    // rich enough domain entity
//    public class Trade : NotifyChange
//    {

//        public Trade(string Symbol, double BoughtAt, int Quantity, DateTime TimeOfPurchase, bool IsRobot = false)
//        {
//            this.Symbol = Symbol;
//            this.BoughtAt = BoughtAt;
//            this.Quantity = Quantity;
//            this.TimeOfPurchase = TimeOfPurchase;
//            this.IsRobot = IsRobot;

//            // domain logic initialization
//            Sell = false;
//            //_minimumSellPrice = BoughtAt - (BoughtAt * 0.015);
//            StopLimit = BoughtAt - (BoughtAt * 0.01);
//        }

//        private List<double> _priceHistory = new List<double>();

//        public string Symbol { get; }
//        public double BoughtAt { get; }
//        public int Quantity { get; }
//        public DateTime TimeOfPurchase { get; }
//        public bool IsRobot { get; }

//        // leading value
//        private double _price;
//        public double Price
//        {
//            get => _price;
//            set 
//            {
//                _price = value;
//                OnPropertyChanged(nameof(Price));


//                // price change should update the model
//                Update();
//            }
//        }
//        // trailing value
//        private double _stopLimit;
//        public double StopLimit
//        {
//            get => _stopLimit;
//            set { _stopLimit = value; OnPropertyChanged(nameof(StopLimit)); }
//        }

//        private double _totalProfit;
//        public double TotalProfit
//        {
//            get => _totalProfit;
//            set { _totalProfit = value; OnPropertyChanged(nameof(TotalProfit)); }
//        }

        
//        // flag indicating it's time to sell
//        public bool Sell { get; set; }
        
//        // cut your losses
//        //private double _minimumSellPrice;
        
//        private void Update()
//        {
//            var highestPrice = _priceHistory.Count > 0 ? _priceHistory.Max() : Price;
//            if (Price > highestPrice)
//            {
//                StopLimit = Math.Round(Price - (Price * 0.0075), 2, MidpointRounding.AwayFromZero);
//            }
//            else
//            {
//                if (Price <= StopLimit) Sell = true;
//            }
   

//            TotalProfit = (StopLimit - BoughtAt) * Quantity;
//            _priceHistory.Add(_price);
//        }
//    }

//    public interface ITradesRepository
//    {
//        ObservableCollection<Trade> Add(string Symbol, double BoughtAt, int Quantity, DateTime TimeOfPurchase, bool IsRobot = false);
//        ObservableCollection<Trade> Get();
//        ObservableCollection<Trade> Remove(string symbol);
//    }

//    public class TradesFlatFileRepository : ITradesRepository
//    {
//        private ObservableCollection<Trade> _trades = new();
//        private readonly string _tradesFile = @"C:\src\active_trades.txt";
//        public TradesFlatFileRepository()
//        {
//            if (File.Exists(_tradesFile))
//            {
//                var lines = File.ReadAllLines(_tradesFile);
//                foreach (var line in lines)
//                {
//                    var props = line.SplitCommas();
//                    var trade = new Trade(props[0], double.Parse(props[1]), int.Parse(props[2]), DateTime.Parse(props[3]), bool.Parse(props[4]));
//                    _trades.Add(trade);
//                }
//            }
//            else
//            {
//                File.Create(_tradesFile);
//            }
//        }

//        public ObservableCollection<Trade> Get() => _trades;

//        public ObservableCollection<Trade> Add(string Symbol, double BoughtAt, int Quantity, DateTime TimeOfPurchase, bool IsRobot = false)
//        {
//            // todo, check if the trade already exists to avoid weird behaviour on delete
//            var trade = new Trade(Symbol, BoughtAt, Quantity, TimeOfPurchase, IsRobot);
//            var line = $"{trade.Symbol},{trade.BoughtAt},{Quantity},{trade.TimeOfPurchase},{trade.IsRobot}";
//            File.AppendAllLines(_tradesFile, new[] { line });
//            _trades.Add(trade);
//            return _trades;
//        }

//        public ObservableCollection<Trade> Remove(string symbol)
//        {
            
//            //_trades.RemoveAll(x => x.Symbol == symbol);
//            var lines = File.ReadAllLines(_tradesFile);
//            File.Delete(_tradesFile);
//            var trades = new ObservableCollection<Trade>();
//            // read in the list of trades, removing the specified item, then write trades back to disk
//            foreach (var line in lines)
//            {
//                var props = line.SplitCommas();
//                if (props[0] == symbol) continue;   // don't add this back to the file
//                var trade = new Trade(props[0], double.Parse(props[1]), int.Parse(props[2]), DateTime.Parse(props[3]), bool.Parse(props[4]));
//                File.AppendAllLines(_tradesFile, new[] { line });
//                trades.Add(trade);
//            }
//            _trades = trades;
//            return trades;
 
//        }
//    }
//}
