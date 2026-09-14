#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Shows the concrete immutable review before the operator chooses approval.</summary>
public sealed class TradingPromotionDialog : Window
{
    public TradingPromotionDialog(Window owner, TradingPromotionReview review)
    {
        Owner = owner; Title = "Use as main strategy · review"; Width = 780; Height = 720; MinWidth = 620; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(17, 22, 29)); Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246));
        var body = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Text(string value, bool heading = false) => body.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = heading ? 21 : 14,
            Margin = new Thickness(0, 0, 0, 12)
        });
        Text(review.Strategy.Name, true);
        Text($"Main real account: {review.AccountLabel}\nSource paper portfolio: {review.Evidence.AccountName}");
        Text("Approval changes this account's source of recommendations when it is saved. The next evaluation uses its reported Real holdings and cash. Resume Trading separately when ready. Paper accounts keep their balances, positions and history. No broker order is sent.");
        Text($"Selection: {review.Strategy.Selection} · maximum holdings: {review.Strategy.MaximumHoldings}\nThe main account keeps its financial history when its strategy changes.");
        Text(string.Join("\n\n", review.Evidence.Limitations));
        var detail = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 220,
            Background = new SolidColorBrush(Color.FromRgb(26, 34, 45)), Foreground = Foreground,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = $"Complete version: {review.Strategy.VersionId:D}\nDaily version: {review.Strategy.Daily.StrategyVersionId:D}\nPolicy version: {review.Strategy.PolicyVersionId:D}\nPaper account: {review.Evidence.PaperAccountId:D}\nPrior approval: {review.PriorPromotionId?.ToString() ?? "None"}\nAccount report: {review.AccountReportId?.ToString() ?? "Missing"}\nImplementation: {review.Strategy.ImplementationIdentity}\nModels:\n" + string.Join("\n", review.Strategy.Daily.Models.Select(m => $"{m.TaskType}: {m.ModelId:D}; SHA256 {m.ArtifactSha256}")) + "\n\nSaved supporting facts:\n" + review.Evidence.FactsJson
        };
        body.Children.Add(new Expander { Header = "Exact versions and supporting evidence", Content = detail, Margin = new Thickness(0, 0, 0, 16) });
        Text(review.CanApprove ? "Available for your approval. Evidence limitations are advisory in manual recommendation mode." : "Blocked: " + string.Join(" ", review.OperationalBlocks));
        Text("Your reason for this decision");
        var reason = new TextBox { MinHeight = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 1024,
            Background = new SolidColorBrush(Color.FromRgb(26,34,45)), Foreground = Foreground, CaretBrush = Foreground }; body.Children.Add(reason);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; body.Children.Add(status);
        var approve = new Button { Content = "Approve as main strategy", IsEnabled = false, Style = (Style)FindResource("WorkspaceButton") }; body.Children.Add(approve);
        reason.TextChanged += (_, _) => approve.IsEnabled = review.CanApprove && !string.IsNullOrWhiteSpace(reason.Text);
        TradingPromotionApproval? request = null;
        approve.Click += async (_, _) =>
        {
            approve.IsEnabled = false; reason.IsReadOnly = true;
            request ??= new(review, Environment.UserName, reason.Text.Trim());
            try
            {
                var saved = await new TradingPromotionRepository().ApproveAsync(request);
                status.Text = $"Approved for {saved.AccountLabel} at {saved.EffectiveUtc:yyyy-MM-dd HH:mm:ss} UTC. Approval {saved.PromotionId:D}. Open Trading → Main account to evaluate and inspect the result. Trading remains paused until you resume it.";
                approve.Content = "Approval saved";
            }
            catch (Exception error)
            {
                status.Text = "Approval was not confirmed. Retry this same request to recover a possibly saved result, or close and prepare a fresh review if anything changed. " + error.Message;
                approve.Content = "Retry this approval"; approve.IsEnabled = true;
            }
        };
    }
}
