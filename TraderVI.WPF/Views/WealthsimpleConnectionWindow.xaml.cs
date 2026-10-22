using Core.WSTrade.Authentication;
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class WealthsimpleConnectionWindow : Window
{
    private readonly WealthsimpleConnection connection;
    private readonly CancellationTokenSource closed = new();
    private bool signingIn;
    public WealthsimpleConnectionWindow(WealthsimpleConnection connection)
    {
        InitializeComponent(); this.connection = connection; DataContext = connection;
        connection.PropertyChanged += ConnectionChanged;
        Closed += async (_, _) =>
        {
            closed.Cancel(); Password.Clear(); Email.Clear(); Otp.Clear();
            connection.PropertyChanged -= ConnectionChanged;
            await connection.CancelSignInAsync();
        };
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Loaded += (_, _) => Email.Focus();
        UpdateControls();
    }
    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e) => UpdateControls();
    private void UpdateControls()
    {
        var enabled = !connection.IsBusy && !signingIn;
        SignInButton.IsEnabled = SubmitOtpButton.IsEnabled = BrowserButton.IsEnabled = CheckButton.IsEnabled = DisconnectButton.IsEnabled = Remember.IsEnabled = enabled;
        CredentialsPanel.IsEnabled = OtpPanel.IsEnabled = enabled;
        CredentialsPanel.Visibility = SignInButton.Visibility = !connection.IsConnected && !connection.AwaitingOtp ? Visibility.Visible : Visibility.Collapsed;
        OtpPanel.Visibility = SubmitOtpButton.Visibility = connection.AwaitingOtp ? Visibility.Visible : Visibility.Collapsed;
        BrowserButton.Visibility = connection.BrowserVerificationRequired ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.IsDefault = !connection.AwaitingOtp; SubmitOtpButton.IsDefault = connection.AwaitingOtp;
        CheckButton.Visibility = connection.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Content = connection.AwaitingOtp ? "Cancel sign-in" : "Disconnect";
    }
    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LocalMessage.Text = "";
        if (string.IsNullOrWhiteSpace(Email.Text) || string.IsNullOrEmpty(Password.Password))
        { LocalMessage.Text = "Enter your Wealthsimple email and password."; return; }
        try
        {
            var operation = connection.SignInAsync(Email.Text, Password.Password, Remember.IsChecked == true, closed.Token);
            Password.Clear();
            await operation;
            if (connection.AwaitingOtp) { Otp.Clear(); Otp.Focus(); }
            if (connection.IsConnected) Email.Clear();
        }
        catch (OperationCanceledException) { }
        finally { Password.Clear(); }
    }
    private async void SubmitOtp_Click(object sender, RoutedEventArgs e)
    {
        LocalMessage.Text = "";
        if (string.IsNullOrWhiteSpace(Otp.Text)) { LocalMessage.Text = "Enter your verification code."; return; }
        try
        {
            var operation = connection.SubmitOtpAsync(Otp.Text, closed.Token); Otp.Clear();
            await operation;
            if (connection.IsConnected) Email.Clear();
            else if (connection.AwaitingOtp) Otp.Focus();
        }
        catch (OperationCanceledException) { }
    }
    private async void Browser_Click(object sender, RoutedEventArgs e)
    {
        signingIn = true; UpdateControls(); LocalMessage.Text = "";
        try
        {
            // A dedicated private browser has sole ownership until it closes. Only then does .NET verify/refresh.
            var browser = new WealthsimpleBrowserWindow { Owner = this };
            if (browser.ShowDialog() == true && browser.TakeSession() is { } candidate)
                await connection.ConnectAsync(candidate, Remember.IsChecked == true, closed.Token);
        }
        catch (OperationCanceledException) { }
        catch { LocalMessage.Text = "Sign-in could not finish. Please try again."; }
        finally { signingIn = false; UpdateControls(); }
    }
    private async void Check_Click(object sender, RoutedEventArgs e)
    { try { await connection.CheckAsync(true, closed.Token); } catch (OperationCanceledException) { } }
    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        Password.Clear(); Otp.Clear();
        try { await connection.DisconnectAsync(closed.Token); } catch (OperationCanceledException) { }
    }
    private async void Remember_Click(object sender, RoutedEventArgs e)
    { try { await connection.SetRememberAsync(Remember.IsChecked == true, closed.Token); } catch (OperationCanceledException) { } }
}
