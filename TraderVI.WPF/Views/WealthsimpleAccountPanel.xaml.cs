using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class WealthsimpleAccountPanel : UserControl
{
    public event RoutedEventHandler ReviewTransitionRequested;
    public event RoutedEventHandler StrategyChoicesRequested;
    public TfsaLiveStrategyCandidate RequestedStrategy { get; private set; }
    private IReadOnlyList<TfsaLiveStrategyCandidate> candidates = Array.Empty<TfsaLiveStrategyCandidate>();
    private bool selecting;
    private WealthsimpleAccountViewModel model;
    public WealthsimpleAccountPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (model != null) model.PropertyChanged -= AccountChanged;
            model = DataContext as WealthsimpleAccountViewModel;
            if (model != null) model.PropertyChanged += AccountChanged;
            candidates = Array.Empty<TfsaLiveStrategyCandidate>(); ResetStrategySelection();
        };
        ResetStrategySelection();
    }
    private void AccountChanged(object sender, PropertyChangedEventArgs e)
    {
        if (model?.SelectedAccount == null) candidates = Array.Empty<TfsaLiveStrategyCandidate>();
        if (!StrategyPicker.IsDropDownOpen && RequestedStrategy == null) ResetStrategySelection();
    }
    public void SetStrategyCandidates(IReadOnlyList<TfsaLiveStrategyCandidate> value)
    { candidates = value; StrategyPickerStatus.Text = value.Count == 0 ? "No current strategy sources are available for review." : ""; ResetStrategySelection(); }
    public void SetStrategyChoiceError() => StrategyPickerStatus.Text = "Choices could not be loaded. Reopen the dropdown to retry; your current strategy is unchanged.";
    public void ResetStrategySelection()
    {
        selecting = true; RequestedStrategy = null;
        StrategyPicker.ItemsSource = new[] { new StrategyChoice(model?.ManagedStrategy?.StrategyName ?? "Choose a strategy to review…", null, true, "Current approved selection; choosing another opens a transition review.") }
            .Concat(candidates.Select(c => new StrategyChoice(c.DisplayName, c, c.SupportsTransitionReview,
                c.SupportsTransitionReview ? "Preview the exact strategy and holding transitions; execution is disabled." : "This entry profile is not supported for transition review."))).ToArray();
        StrategyPicker.SelectedIndex = 0; selecting = false;
    }
    private void StrategyPicker_Opened(object sender, EventArgs e)
    { if (candidates.Count == 0) { StrategyPickerStatus.Text = "Loading strategy choices…"; StrategyChoicesRequested?.Invoke(this, new RoutedEventArgs()); } }
    private void StrategyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selecting || StrategyPicker.SelectedItem is not StrategyChoice { Candidate: { } candidate, IsSupported: true }) return;
        RequestedStrategy = candidate; StrategyPicker.IsDropDownOpen = false;
        ReviewTransitionRequested?.Invoke(this, new RoutedEventArgs());
    }
    private async void RefreshBroker_Click(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel vm) await vm.RefreshAsync(); }
    private void Today_Checked(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel vm) vm.ShowOverallPerformance = false; }
    private sealed record StrategyChoice(string Label, TfsaLiveStrategyCandidate Candidate, bool IsSupported, string Explanation);
}
