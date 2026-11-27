namespace Snoop.Windows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Snoop.Infrastructure;
using Snoop.Localization;

public sealed partial class SnoopUI
{
    public static readonly RoutedCommand OpenCommandPaletteCommand = new(
        nameof(OpenCommandPaletteCommand),
        typeof(SnoopUI),
        new InputGestureCollection { new KeyGesture(Key.K, ModifierKeys.Control) });

    private static readonly KeyValuePair<WorkbenchDestination, string>[] paletteCommands =
    {
        new(WorkbenchDestination.Tree, "Workbench.ShowTree"),
        new(WorkbenchDestination.Properties, "Tab.Properties"),
        new(WorkbenchDestination.Diagnostics, "Tab.Diagnostics"),
        new(WorkbenchDestination.Events, "Tab.Events"),
        new(WorkbenchDestination.Preview, "Tab.Preview"),
        new(WorkbenchDestination.Output, "Tab.Output"),
    };

    private IInputElement? focusBeforeCommandPalette;

    protected override void OnDeactivated(EventArgs e)
    {
        this.ClosePreviewPicker(restoreFocus: false);
        this.WorkbenchCommandPalette.IsOpen = false;
        base.OnDeactivated(e);
    }

    private void HandleOpenCommandPalette(object sender, ExecutedRoutedEventArgs e)
    {
        if (this.WorkbenchCommandPalette.IsOpen)
        {
            this.WorkbenchCommandQuery.Focus();
            return;
        }

        this.focusBeforeCommandPalette = Keyboard.FocusedElement;
        this.WorkbenchCommandQuery.Clear();
        this.FilterWorkbenchCommands();
        this.WorkbenchCommandPalette.IsOpen = true;
    }

    private void FilterWorkbenchCommands()
    {
        var query = this.WorkbenchCommandQuery.Text.Trim();
        var localization = InspectorLocalization.Default;
        this.WorkbenchCommandResults.ItemsSource = paletteCommands
            .Select(command => new WorkbenchCommandMatch(command.Key, localization[command.Value]))
            .Where(command => command.Caption.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        this.WorkbenchCommandResults.SelectedIndex = this.WorkbenchCommandResults.HasItems ? 0 : -1;
    }

    private void HandleCommandPaletteOpened(object? sender, EventArgs e)
    {
        // A Popup has a separate visual tree. Exclude its controls from target picking.
        SnoopPartsRegistry.AddSnoopVisualTreeRoot(this.CommandPaletteSurface);
        this.WorkbenchCommandQuery.Focus();
    }

    private void HandleCommandPaletteClosed(object? sender, EventArgs e)
    {
        SnoopPartsRegistry.RemoveSnoopVisualTreeRoot(this.CommandPaletteSurface);
        this.focusBeforeCommandPalette = null;
    }

    private void HandleCommandPalettePreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                var previousFocus = this.focusBeforeCommandPalette;
                this.WorkbenchCommandPalette.IsOpen = false;
                if (previousFocus is not null)
                {
                    Keyboard.Focus(previousFocus);
                }

                break;
            case Key.Enter:
                e.Handled = true;
                this.ExecuteWorkbenchCommand(this.WorkbenchCommandResults.SelectedItem);
                break;
            case Key.Down or Key.Up when this.WorkbenchCommandQuery.IsKeyboardFocusWithin:
                e.Handled = true;
                var results = this.WorkbenchCommandResults;
                if (results.HasItems)
                {
                    var step = e.Key == Key.Down ? 1 : -1;
                    results.SelectedIndex = Math.Min(results.Items.Count - 1, Math.Max(0, results.SelectedIndex + step));
                    results.ScrollIntoView(results.SelectedItem);
                }

                break;
        }
    }

    private void HandleCommandPaletteItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            e.Handled = true;
            this.ExecuteWorkbenchCommand(item.Content);
        }
    }

    private void ExecuteWorkbenchCommand(object? item)
    {
        if (item is WorkbenchCommandMatch command)
        {
            this.WorkbenchCommandPalette.IsOpen = false;
            this.NavigateToWorkbench(command.Destination);
        }
    }

    private enum WorkbenchDestination
    {
        Tree,
        Properties,
        Diagnostics,
        Events,
        Preview,
        Output
    }

    private sealed class WorkbenchCommandMatch
    {
        public WorkbenchCommandMatch(WorkbenchDestination destination, string caption)
        {
            this.Destination = destination;
            this.Caption = caption;
        }

        public WorkbenchDestination Destination { get; }

        public string Caption { get; }
    }
}
