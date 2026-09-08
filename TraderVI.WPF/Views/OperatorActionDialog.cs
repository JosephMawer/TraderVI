#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Explicit operator intent and actual-fill details; never supplies a fictional execution price.</summary>
public sealed class OperatorActionDialog : Window
{
    private readonly TextBox reason = new() { MinHeight=65, TextWrapping=TextWrapping.Wrap, AcceptsReturn=true, MaxLength=400 };
    private readonly TextBox price = new();
    private readonly TextBox time = new() { Text=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
    private readonly TextBlock error = new() { Foreground=Brushes.Firebrick, TextWrapping=TextWrapping.Wrap };
    public string Reason => reason.Text.Trim();
    public decimal FillPrice { get; private set; }
    public DateTime FillTime { get; private set; }
    public OperatorActionDialog(Window? owner,string title,string explanation,bool realFill=false)
    {
        Owner=owner; Title=title; Width=510; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; Background=Brushes.White; Foreground=Brushes.Black;
        var body=new StackPanel {Margin=new Thickness(22)}; Content=body;
        body.Children.Add(new TextBlock {Text=explanation,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        if(realFill)
        {
            body.Children.Add(new TextBlock {Text="Actual sale price per share (all shares, zero commission)"}); body.Children.Add(price);
            body.Children.Add(new TextBlock {Text="Actual fill time on this computer (yyyy-MM-dd HH:mm:ss)",Margin=new Thickness(0,12,0,0)}); body.Children.Add(time);
        }
        body.Children.Add(new TextBlock {Text="Reason / notes",Margin=new Thickness(0,12,0,6)}); body.Children.Add(reason); body.Children.Add(error);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        var cancel=new Button {Content="Cancel",IsCancel=true,Padding=new Thickness(16,7,16,7),Margin=new Thickness(0,0,10,0)};
        var confirm=new Button {Content=realFill ? "Record completed sale" : "Confirm",Padding=new Thickness(16,7,16,7)};
        confirm.Click+=(_,_)=>
        {
            if(Reason.Length==0) {error.Text="Enter a reason for the audit record.";return;}
            if(realFill)
            {
                if(!decimal.TryParse(price.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var p)||p<=0)
                {error.Text="Enter a positive actual fill price using a decimal point.";return;}
                if(!DateTime.TryParseExact(time.Text,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt)||dt>DateTime.Now)
                {error.Text="Enter a valid completed fill time, not a future time.";return;}
                FillPrice=p; FillTime=dt;
            }
            DialogResult=true;
        };
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);body.Children.Add(buttons);
    }
}
