namespace Snoop.Views.DebugListenerTab;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Infrastructure;
using Snoop.Localization;

public partial class DebugListenerControl : IListener, IDisposable
{
#pragma warning disable CA2213
    private readonly SnoopDebugListener snoopDebugListener = new();
#pragma warning restore CA2213
    private readonly List<string> capturedOutput = new();
    private string search = string.Empty;
    private int outputGeneration;
    private bool disposed;
    private bool ignoreWrites;

    public DebugListenerControl()
    {
        this.FiltersViewModel = new FiltersViewModel(Settings.Default.SnoopDebugFilters);
        this.DataContext = this.FiltersViewModel;

        this.InitializeComponent();

        this.snoopDebugListener.RegisterListener(this);

        PresentationTraceSources.SetTraceLevel(this, PresentationTraceLevel.None);
        PresentationTraceSources.SetTraceLevel(this.textBlockStatus, PresentationTraceLevel.None);
        PresentationTraceSources.SetTraceLevel(this.textBoxDebugContent, PresentationTraceLevel.None);
    }

    internal FiltersViewModel FiltersViewModel { get; }

    private void CheckBoxStartListening_Checked(object sender, RoutedEventArgs e)
    {
        Trace.Listeners.Add(this.snoopDebugListener);
        PresentationTraceSources.DataBindingSource.Listeners.Add(this.snoopDebugListener);
    }

    private void CheckBoxStartListening_Unchecked(object sender, RoutedEventArgs e)
    {
        this.CleanupListeners();
    }

    public void Write(string? str)
    {
        if (str is not { Length: > 0 }
            || Volatile.Read(ref this.disposed)
            || (this.Dispatcher.CheckAccess() && this.ignoreWrites))
        {
            return;
        }

        var generation = Volatile.Read(ref this.outputGeneration);
        this.Dispatcher.RunInDispatcherAsync(() => this.AppendOutput(str, generation), DispatcherPriority.Background);
    }

    private void AppendOutput(string text, int generation)
    {
        if (Volatile.Read(ref this.disposed) || generation != this.outputGeneration)
        {
            return;
        }

        this.capturedOutput.Add(text);
        if (this.MatchesOutput(text))
        {
            this.UpdateOutput(text, append: true);
        }
    }

    private bool MatchesOutput(string text)
        => (this.search.Length == 0 || text.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) >= 0)
           && (!this.FiltersViewModel.IsSet || this.FiltersViewModel.FilterMatches(text));

    private void HandleOutputSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        this.search = ((TextBox)sender).Text.Trim();
        this.RefreshOutput();
    }

    private void RefreshOutput()
        => this.UpdateOutput(string.Concat(this.capturedOutput.Where(this.MatchesOutput)), append: false);

    private void UpdateOutput(string text, bool append)
    {
        this.ignoreWrites = true;
        try
        {
            var output = this.textBoxDebugContent;
            var shouldScrollToEnd = output.SelectionLength == 0 && output.SelectionStart == output.Text.Length;
            if (append)
            {
                output.AppendText(text);
            }
            else
            {
                output.Text = text;
            }

            if (shouldScrollToEnd)
            {
                output.ScrollToEnd();
                output.SelectionStart = output.Text.Length;
            }
        }
        finally
        {
            this.ignoreWrites = false;
        }
    }

    private void ButtonClear_Click(object sender, RoutedEventArgs e)
    {
        // Discard callbacks queued before this clear, including filtered writes.
        Interlocked.Increment(ref this.outputGeneration);
        this.capturedOutput.Clear();
        this.UpdateOutput(string.Empty, append: false);
    }

    private void ButtonClearFilters_Click(object sender, RoutedEventArgs e)
    {
        var localization = InspectorLocalization.Default;
        var result = MessageBox.Show(
            localization["DebugListener.ClearFiltersMessage"],
            localization["DebugListener.ClearFiltersTitle"],
            MessageBoxButton.YesNo);
        if (result == MessageBoxResult.Yes)
        {
            this.FiltersViewModel.ClearFilters();
            Settings.Default.SnoopDebugFilters.Clear();
            this.RefreshOutput();
        }
    }

    private void ButtonSetFilters_Click(object sender, RoutedEventArgs e)
    {
        var setFiltersWindow = new SetFiltersWindow(this.FiltersViewModel)
        {
            Topmost = true
        };

        setFiltersWindow.ShowDialogEx(this);

        this.RefreshOutput();
    }

    private void ComboBoxPresentationTraceLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.comboBoxPresentationTraceLevel?.Items is null
            || this.comboBoxPresentationTraceLevel.Items.Count <= this.comboBoxPresentationTraceLevel.SelectedIndex
            || this.comboBoxPresentationTraceLevel.SelectedIndex < 0)
        {
            return;
        }

        var selectedComboBoxItem = this.comboBoxPresentationTraceLevel.Items[this.comboBoxPresentationTraceLevel.SelectedIndex] as ComboBoxItem;
        if (selectedComboBoxItem?.Tag is null)
        {
            return;
        }

        var sourceLevel = (SourceLevels)Enum.Parse(typeof(SourceLevels), selectedComboBoxItem.Tag.ToString()!);
        PresentationTraceSources.DataBindingSource.Switch.Level = sourceLevel;
    }

    private void CleanupListeners()
    {
        Trace.Listeners.Remove(this.snoopDebugListener);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(this.snoopDebugListener);
    }

    public void Dispose()
    {
        Volatile.Write(ref this.disposed, true);
        this.CleanupListeners();
        this.snoopDebugListener.Dispose();
    }
}
