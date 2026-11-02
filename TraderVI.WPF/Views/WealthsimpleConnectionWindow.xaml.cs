using Core.WSTrade.Authentication;
#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class WealthsimpleConnectionWindow : Window
{
    private readonly IWealthsimpleConnection connection;
    private readonly CancellationTokenSource closed = new();
    private bool signingIn;
    private CancellationTokenSource? edgeLogin;
    public WealthsimpleConnectionWindow(IWealthsimpleConnection connection)
    {
        InitializeComponent(); this.connection = connection; DataContext = connection;
        connection.PropertyChanged += ConnectionChanged;
        Closed += async (_, _) =>
        {
            closed.Cancel(); Password.Clear(); Email.Clear(); Otp.Clear();
            connection.PropertyChanged -= ConnectionChanged;
            try { await connection.CancelSignInAsync(); }
            catch { /* Closing a client must remain possible if its engine rejects or loses the command. */ }
        };
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Loaded += (_, _) => Email.Focus();
        UpdateControls();
    }
    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e) => UpdateControls();
    private void UpdateControls()
    {
        var enabled = !connection.IsBusy && !signingIn;
        EdgeButton.IsEnabled = enabled;
        EdgeButton.Visibility = !connection.IsConnected && edgeLogin == null ? Visibility.Visible : Visibility.Collapsed;
        CancelEdgeButton.Visibility = edgeLogin != null ? Visibility.Visible : Visibility.Collapsed;
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
        catch { ShowCommandFailure(); }
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
        catch { ShowCommandFailure(); }
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
    private async void Edge_Click(object sender, RoutedEventArgs e)
    {
        edgeLogin = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
        signingIn = true; Password.Clear(); Otp.Clear(); UpdateControls();
        try
        {
            await connection.CancelSignInAsync();
            using var handoff = new WealthsimpleEdgeHandoff();
            var edgePath = FindEdge();
            var start = new ProcessStartInfo(edgePath) { UseShellExecute = false };
            start.ArgumentList.Add("--new-window");
            start.ArgumentList.Add("https://my.wealthsimple.com/app/login#tradervi-login=" + handoff.PairingId);
            using var launched = Process.Start(start);
            LocalMessage.Text = "In the new Edge tab, click the TraderVI extension, then Connect this tab BEFORE signing in. " +
                "Use your saved passkey. The tab closes after handoff; TraderVI then verifies the connection. Waiting up to five minutes…";
            var connected = await handoff.ReceiveAsync(async candidate =>
            {
                await connection.ConnectAsync(candidate, Remember.IsChecked == true, edgeLogin.Token);
                return connection.IsConnected;
            }, edgeLogin.Token);
            LocalMessage.Text = connected ? "" : "Edge sign-in was received, but the connection could not be verified. See the connection status above.";
            if (connected) Email.Clear();
        }
        catch (OperationCanceledException) { LocalMessage.Text = "Edge sign-in cancelled or timed out. You can start again."; }
        catch (FileNotFoundException) { LocalMessage.Text = "Microsoft Edge could not be found. Install Edge, then try again."; }
        catch { LocalMessage.Text = "Edge sign-in could not finish. Check the TraderVI extension and native host setup, then start again."; }
        finally { edgeLogin.Dispose(); edgeLogin = null; signingIn = false; UpdateControls(); }
    }
    private void CancelEdge_Click(object sender, RoutedEventArgs e) => edgeLogin?.Cancel();
    private static string FindEdge()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException();
    }
    private async void Check_Click(object sender, RoutedEventArgs e)
    { try { await connection.CheckAsync(true, closed.Token); } catch (OperationCanceledException) { } catch { ShowCommandFailure(); } }
    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        Password.Clear(); Otp.Clear();
        try { await connection.DisconnectAsync(closed.Token); } catch (OperationCanceledException) { } catch { ShowCommandFailure(); }
    }
    private async void Remember_Click(object sender, RoutedEventArgs e)
    { try { await connection.SetRememberAsync(Remember.IsChecked == true, closed.Token); } catch (OperationCanceledException) { } catch { ShowCommandFailure(); } }
    private void ShowCommandFailure() => LocalMessage.Text =
        "The engine could not complete this command. Check engine status; broker access is disabled during verification.";
}
