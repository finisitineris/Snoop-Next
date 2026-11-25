namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Windows;

internal static class InspectorContextMenuSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var properties = (TabControl)inspector.FindName("PropertiesWorkbenchPane");
        var diagnostics = (TabControl)inspector.FindName("DiagnosticsWorkbenchPane");
        var propertyPaneItem = (MenuItem)inspector.FindName("ShowPropertiesPaneItem");
        var diagnosticsPaneItem = (MenuItem)inspector.FindName("ShowDiagnosticsPaneItem");
        var previousPropertyTab = properties.SelectedItem;
        var previousDiagnosticsTab = diagnostics.SelectedItem;
        var propertiesShown = propertyPaneItem.IsChecked;
        var diagnosticsShown = diagnosticsPaneItem.IsChecked;
        var propertyGrid = (Grid)inspector.FindName("PropertyInspector");
        var owner = propertyGrid.Children.OfType<TextBlock>().Single(item => item.ContextMenu?.Name == "delveTypeContextMenu");
        var menu = owner.ContextMenu;
        var previousPlacementTarget = menu.PlacementTarget;
        var eventsView = (FrameworkElement)inspector.FindName("eventsView");
        var chooser = (NoSelectionComboBox)eventsView.FindName("EventChooser");

        try
        {
            SetPaneShown(propertyPaneItem, true);
            properties.SelectedItem = inspector.FindName("WorkbenchPropertiesTab");
            inspector.UpdateLayout();
            menu.PlacementTarget = owner;
            menu.IsOpen = true;
            VerifySurface(menu);
            var firstCommand = menu.Items.OfType<MenuItem>().First();
            firstCommand.ApplyTemplate();
            if (!owner.IsVisible || firstCommand.Template.FindName("ItemSurface", firstCommand) is not Border { IsVisible: true }
                || !ReferenceEquals(firstCommand.CommandTarget, owner))
            {
                throw new InvalidOperationException("The open property-type context menu lost its Fluent command row or placement target.");
            }

            menu.IsOpen = false;
            SetPaneShown(diagnosticsPaneItem, true);
            diagnostics.SelectedItem = inspector.FindName("WorkbenchEventsTab");
            inspector.UpdateLayout();
            chooser.IsDropDownOpen = true;
            var surface = VerifySurface(chooser);
            if (!chooser.IsVisible || chooser.Template.FindName("PART_Popup", chooser) is not Popup { IsOpen: true }
                || chooser.Template.FindName("PART_ItemsControl", chooser) is not ItemsControl { IsVisible: true } groups
                || groups.Items.Count == 0 || !ReferenceEquals(groups.ItemsSource, chooser.ItemsSource)
                || !groups.GroupStyle.Any(group => ReferenceEquals(group.HeaderTemplate, chooser.GroupHeaderTemplate))
                || surface.Child is not DockPanel contents
                || !contents.Children.OfType<Button>().Any(button => button.IsVisible && button.Command != null
                                                                    && ReferenceEquals(button.Command, chooser.ResetSettingsCommand)))
            {
                throw new InvalidOperationException("The open event chooser lost its grouped events or reset command.");
            }
        }
        finally
        {
            menu.IsOpen = false;
            menu.PlacementTarget = previousPlacementTarget;
            chooser.IsDropDownOpen = false;
            properties.SelectedItem = previousPropertyTab;
            diagnostics.SelectedItem = previousDiagnosticsTab;
            SetPaneShown(propertyPaneItem, propertiesShown);
            SetPaneShown(diagnosticsPaneItem, diagnosticsShown);
        }

        Border VerifySurface(Control control)
        {
            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            control.ApplyTemplate();
            if (control.Template.FindName("FlyoutSurface", control) is not Border { IsVisible: true } panel
                || panel.ActualHeight <= 0 || panel.ActualWidth <= 0 || panel.CornerRadius.TopLeft < 4
                || PresentationSource.FromVisual(panel) is not HwndSource source
                || source.Handle == new WindowInteropHelper(inspector).Handle
                || panel.Background is not SolidColorBrush brush
                || brush.Color != ((SolidColorBrush)inspector.FindResource("SnoopNext.Brush.Panel")).Color)
            {
                throw new InvalidOperationException($"The open {control.Name} lacks a Fluent flyout in the inspector theme.");
            }

            return panel;
        }
    }

    private static void SetPaneShown(MenuItem toggle, bool shown)
    {
        if (toggle.IsChecked != shown)
        {
            toggle.IsChecked = shown;
            toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, toggle));
        }
    }
}
