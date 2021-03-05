using System.ComponentModel;
using System.Windows.Controls;

namespace TraderVI.WPF.Viewmodels
{
    public class WatchListViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyRaised(string propertyname)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyname));
            }
        }

        private string _symbol;
        public string Symbol
        {
            get => _symbol;
            set { _symbol = value; OnPropertyRaised("Symbol"); }
        }

        private string _price;
        public string Price
        {
            get => _price;
            set { _price = value; OnPropertyRaised(nameof(Price)); }
        }
        private string _priceChange;
        public string PriceChange
        {
            get => _priceChange;
            set { _priceChange = value; OnPropertyRaised(nameof(PriceChange)); }
        }
        private string _percentChange;
        public string PercentChange
        {
            get => _percentChange;
            set { _percentChange = value; OnPropertyRaised(nameof(PercentChange)); }
        }
        private string _volume;
        public string Volume
        {
            get => _volume;
            set { _volume = value; OnPropertyRaised(nameof(Volume)); }
        }
        public string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyRaised(nameof(Name)); }
        }
    }
}
