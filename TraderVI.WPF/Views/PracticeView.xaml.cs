using Core.Practice;
using Core.Trader.DelphiLive;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TraderVI.WPF.Views;

public partial class PracticeView : UserControl
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<PracticeFrame> frames = [];
    private int index;
    public PracticeView()
    {
        InitializeComponent();
        timer.Tick += (_, _) => Advance();
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        var dialog = new OpenFileDialog { Title = "Open a Practice / Exploratory playback", Filter = "Practice frames|frames.ndjson" };
        if (dialog.ShowDialog() != true) return;
        await OpenPlaybackAsync(dialog.FileName);
    }
    public async Task OpenPlaybackAsync(string path)
    {
        Pause();
        OpenButton.IsEnabled = false;
        try
        {
            var loaded = await Task.Run(() => PracticePlayback.Load(path));
            frames = loaded;
            index = 0;
            ShowFrame();
        }
        catch (Exception ex) { StatusText.Text = "Unable to open practice playback: " + ex.Message; }
        finally { OpenButton.IsEnabled = true; }
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (timer.IsEnabled) Pause();
        else { timer.Start(); PlayButton.Content = "Pause"; }
    }
    private void Step_Click(object sender, RoutedEventArgs e) { Pause(); Advance(); }
    private void Advance()
    {
        if (frames.Count == 0) return;
        if (index < frames.Count - 1) index++;
        ShowFrame();
    }
    private void Pause() { timer.Stop(); if (PlayButton is not null) PlayButton.Content = "Play"; }
    private void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Speed.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int speed))
            timer.Interval = TimeSpan.FromSeconds(1d / speed);
    }
    private void View_Unloaded(object sender, RoutedEventArgs e) => Pause();
    private static string Toronto(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, ReviewedTsxSessionCalendar.Toronto).ToString("yyyy-MM-dd HH:mm");
    private static string AccountName(string code)
    {
        var definition = Core.Trader.SystemShadowPortfolioDefinition.Version1.Single(p => p.Code == code);
        return definition.Lens + " · Top " + definition.MaximumPositions;
    }
    private void ShowFrame()
    {
        var frame = frames[index];
        ClockText.Text = "PRACTICE · " + Toronto(frame.SimulatedUtc) + " Toronto";
        ModelTiming.Text = frame.ModelTimingNote;
        StatusText.Text = frame.DailyStatus + (index == frames.Count - 1 ? " · End of recorded playback." : "");
        Accounts.ItemsSource = frame.Accounts.Select(a => new { Account = AccountName(a.Code), a.Cash, a.NetAssetValue, a.Return, a.MarkStatus, a.RiskStatus }).ToArray();
        Holdings.ItemsSource = frame.Accounts.SelectMany(a => a.Positions.Select(p => new
        { Account = AccountName(a.Code), p.Symbol, p.Shares, p.AverageCost, p.LastPrice, PriceTime = Toronto(p.LastPriceEventUtc) })).ToArray();
        Orders.ItemsSource = frame.Accounts.SelectMany(a => a.PendingOrders.Select(o => new
        { Account = AccountName(a.Code), o.Symbol, o.Side, Reason = o.ReasonCode })).ToArray();
        DailyPicks.ItemsSource = frame.DailyPicks;
        Candidates.ItemsSource = frame.Candidates.Select(c => new { Account = AccountName(c.Account), c.Symbol, c.Rank, c.State, c.ReasonCode }).ToArray();
        Warnings.Text = string.Join(Environment.NewLine, frame.Poll?.Warnings ?? []);
        PlayButton.IsEnabled = StepButton.IsEnabled = index < frames.Count - 1;
        if (index == frames.Count - 1) Pause();
    }
}
