#nullable enable

using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class ScorecardsView : UserControl
{
    private readonly ScorecardsViewModel viewModel = new();
    private bool loadedOnce;

    public ScorecardsView()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += ScorecardsView_Loaded;
    }

    private async void ScorecardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce)
            return;
        loadedOnce = true;
        await viewModel.RefreshAsync();
    }

    private async void RefreshScorecardsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshScorecardsButton.IsEnabled = false;
        try
        {
            await viewModel.RefreshAsync();
        }
        finally
        {
            RefreshScorecardsButton.IsEnabled = true;
        }
    }
}
