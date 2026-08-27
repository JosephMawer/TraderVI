#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class DataAuditView : UserControl
{
    private readonly DataAuditViewModel viewModel = new();

    public DataAuditView()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void RunAuditButton_Click(object sender, RoutedEventArgs e)
    {
        RunAuditButton.IsEnabled = false;
        try
        {
            await viewModel.RunAsync();
        }
        finally
        {
            RunAuditButton.IsEnabled = true;
        }
    }
}

