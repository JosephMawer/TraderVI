namespace TraderVI.WPF.Viewmodels
{
    public class OrdersViewModel
    {
        public string Symbol { get; set; }
        public int Qty { get; set; }
        public double Amount { get; set; }
        public double Total { get; set; }
        public string Date { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
}
