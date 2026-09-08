#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

public sealed class ReportedPurchaseDialog : Window
{
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }
    public DateTime FillUtc { get; private set; }
    public string Notes { get; private set; } = "";
    public ReportedPurchaseDialog(Window owner, string symbol)
    {
        Owner = owner; Title = "Report completed purchase"; Width = 500; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White; Foreground = Brushes.Black;
        var body = new StackPanel { Margin = new Thickness(24) }; Content = body;
        body.Children.Add(new TextBlock { Text = $"Report the actual {symbol} purchase already completed at your broker, with zero commission. Enter the actual quantity and price; these are not filled from the recommendation. No order is sent.", TextWrapping = TextWrapping.Wrap });
        TextBox Field(string label, string initial = "") { body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) }); var box = new TextBox { Text = initial }; body.Children.Add(box); return box; }
        var quantity = Field("Actual shares"); var price = Field("Actual price per share");
        var time = Field("Actual time on this computer (yyyy-MM-dd HH:mm:ss)", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var notes = Field("Confirmation / notes"); var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; body.Children.Add(error);
        var save = new Button { Content = "Record completed purchase", Margin = new Thickness(0, 18, 0, 0) }; body.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!int.TryParse(quantity.Text, out var q) || q <= 0 || !decimal.TryParse(price.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) || p <= 0 ||
                !DateTime.TryParseExact(time.Text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t > DateTime.Now || string.IsNullOrWhiteSpace(notes.Text))
            { error.Text = "Enter positive shares and price, a completed fill time, and your confirmation."; return; }
            Quantity = q; Price = p; FillUtc = DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime(); Notes = notes.Text.Trim(); DialogResult = true;
        };
    }
}
