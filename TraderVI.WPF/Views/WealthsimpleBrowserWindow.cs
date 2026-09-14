#nullable enable
using Core.WSTrade.Authentication;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TraderVI.WPF.Views;

/// <summary>Hosts the provider's real login and normal challenges. No injected scripts or credential form.</summary>
public sealed class WealthsimpleBrowserWindow : Window
{
    private static readonly Uri TokenUri = new("https://api.production.wealthsimple.com/v1/oauth/v2/token");
    private readonly WebView2 browser = new();
    private readonly TextBlock message = new() { Text = "Opening Wealthsimple’s secure sign-in page…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 10) };
    private WealthsimpleAuthClient? candidate;
    private bool closed, completing;
    public WealthsimpleBrowserWindow()
    {
        Title = "Sign in · Wealthsimple"; Width = Math.Min(860, SystemParameters.WorkArea.Width);
        Height = Math.Min(820, SystemParameters.WorkArea.Height); MinWidth = 620; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 21, 27)); Foreground = Brushes.White;
        var layout = new DockPanel();
        var origin = new TextBlock { Text = "Secure sign-in  ·  https://my.wealthsimple.com", Margin = new Thickness(16, 12, 16, 8), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(origin, Dock.Top); layout.Children.Add(origin);
        DockPanel.SetDock(message, Dock.Bottom); layout.Children.Add(message);
        layout.Children.Add(browser); Content = layout;
        Loaded += Initialize;
        Closed += (_, _) => { closed = true; browser.Dispose(); if (DialogResult != true) { candidate?.Dispose(); candidate = null; } };
    }

    public WealthsimpleAuthClient? TakeSession() { var value = candidate; candidate = null; return value; }

    private async void Initialize(object sender, RoutedEventArgs e)
    {
        try
        {
            browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple", "Browser"),
                ProfileName = "Login" + Guid.NewGuid().ToString("N"), IsInPrivateModeEnabled = true
            };
            await browser.EnsureCoreWebView2Async();
            if (closed) return;
            var web = browser.CoreWebView2;
            web.Settings.IsPasswordAutosaveEnabled = false;
            web.Settings.IsGeneralAutofillEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.NewWindowRequested += (_, args) => args.Handled = true;
            web.DownloadStarting += (_, args) => args.Cancel = true;
            web.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "my.wealthsimple.com")
                { args.Cancel = true; message.Text = "This sign-in window stays on my.wealthsimple.com. Close it to use another sign-in method."; }
            };
            web.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && !completing) message.Text = "The sign-in page could not load. Close this window and try again.";
            };
            web.WebResourceResponseReceived += ResponseReceived;
            message.Text = "Sign in normally, including any verification code or security check.";
            web.Navigate("https://my.wealthsimple.com/app/login");
        }
        catch { if (!closed) message.Text = "The sign-in browser could not start. Install or update Microsoft Edge WebView2 Runtime, then try again."; }
    }

    private async void ResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (closed || completing || e.Request.Method != "POST" || e.Response.StatusCode != 200 ||
            !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri != TokenUri) return;
        WealthsimpleAuthClient? captured = null;
        var stage = "reading the login request";
        try
        {
            // Request is the already-committed copy; reading it cannot change the browser's request.
            using var request = e.Request.Content;
            if (request == null) { message.Text = "The browser did not expose the completed login. Sign-in handoff needs investigation."; return; }
            using var requestCopy = WealthsimpleBrowserBody.Copy(request);
            using var json = JsonDocument.Parse(requestCopy);
            if (!json.RootElement.TryGetProperty("grant_type", out var grant) ||
                grant.GetString() is not ("password" or "refresh_token" or "webauthn")) return;
            completing = true;
            // Retain only public protocol fields, never email, password, OTP or challenge values.
            using var metadata = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            { grant_type = grant.GetString(), client_id = json.RootElement.GetProperty("client_id").GetString() }));
            string? Header(string name) => e.Request.Headers.Contains(name) ? e.Request.Headers.GetHeader(name) : null;
            var device = Header("x-ws-device-id"); var session = Header("x-ws-session-id");
            stage = "reading the login response";
            using var response = await e.Response.GetContentAsync();
            if (closed) return;
            if (response == null) throw new InvalidDataException();
            using var responseCopy = WealthsimpleBrowserBody.Copy(response);
            stage = "importing the session";
            captured = WealthsimpleAuthClient.FromBrowserExchange(uri, "POST", 200, metadata, responseCopy, device, session);
            candidate = captured; captured = null;
            // End the browser owner before handing refresh authority to the native client.
            // Close after this COM callback returns; disposing WebView2 inside its callback is unsafe.
            _ = Dispatcher.BeginInvoke(new Action(() => { if (!closed) DialogResult = true; }), DispatcherPriority.Normal);
        }
        catch (InvalidDataException)
        {
            if (!closed) message.Text = "This login could not be handed to TraderVI. The provider may require a browser signing key; support needs further investigation.";
        }
        catch { if (!closed) message.Text = $"TraderVI could not finish {stage}. Close this window and try again."; }
        finally { captured?.Dispose(); if (candidate == null) completing = false; }
    }
}
