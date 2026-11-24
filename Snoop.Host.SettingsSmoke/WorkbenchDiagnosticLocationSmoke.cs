namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Infrastructure.Diagnostics.Providers;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class WorkbenchDiagnosticLocationSmoke
{
    public static void Verify()
    {
        var settings = CoreSettings.Default;
        var originalFile = settings.SettingsFile;
        var fixtureFile = Path.Combine(Path.GetDirectoryName(originalFile)!, "WorkbenchDiagnosticLocationSmoke.xml");
        var originalPlacement = settings.SnoopUIWindowPlacement;
        var originalTreeFraction = settings.WorkbenchTreeFraction;
        var originalDiagnosticFraction = settings.WorkbenchDiagnosticsFraction;
        var originalTreeWidth = settings.WorkbenchTreeWidth;
        var originalPropertiesWidth = settings.WorkbenchPropertiesWidth;
        var originalDiagnosticsHeight = settings.WorkbenchDiagnosticsHeight;
        var originalTreeCollapsed = settings.WorkbenchTreeCollapsed;
        var originalPropertiesCollapsed = settings.WorkbenchPropertiesCollapsed;
        var originalDiagnosticsCollapsed = settings.WorkbenchDiagnosticsCollapsed;
        var originalShowDefaults = settings.ShowDefaults;
        var originalShowPreviewer = settings.ShowPreviewer;
        var originalTraceLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        settings.SettingsFile = fixtureFile;
        try
        {
            var inspector = new SnoopUI();
            // This hidden fixture window does not run OnSourceInitialized.
            CacheManager.Instance.IncreaseUsageCount();
            try
            {
                VerifyInspector(inspector);
            }
            finally
            {
                inspector.Close();
            }
        }
        finally
        {
            settings.SettingsFile = originalFile;
            settings.SnoopUIWindowPlacement = originalPlacement;
            settings.WorkbenchTreeFraction = originalTreeFraction;
            settings.WorkbenchDiagnosticsFraction = originalDiagnosticFraction;
            settings.WorkbenchTreeWidth = originalTreeWidth;
            settings.WorkbenchPropertiesWidth = originalPropertiesWidth;
            settings.WorkbenchDiagnosticsHeight = originalDiagnosticsHeight;
            settings.WorkbenchTreeCollapsed = originalTreeCollapsed;
            settings.WorkbenchPropertiesCollapsed = originalPropertiesCollapsed;
            settings.WorkbenchDiagnosticsCollapsed = originalDiagnosticsCollapsed;
            settings.ShowDefaults = originalShowDefaults;
            settings.ShowPreviewer = originalShowPreviewer;
            PresentationTraceSources.DataBindingSource.Switch.Level = originalTraceLevel;
            InspectorLocalization.Default.SetLanguage(originalLanguage);
            File.Delete(fixtureFile);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: diagnostics locate a real element and preserve scope, filters and pane state");
    }

    private static void VerifyInspector(SnoopUI inspector)
    {
        if (inspector.FindName("WorkbenchDiagnosticsView") is not DiagnosticsView view
            || view.FindName("diagnostics") is not ListView { View: GridView table } list
            || table.Columns[0].CellTemplate.LoadContent() is not Button { Name: "DiagnosticLocateButton" } locate)
        {
            throw new InvalidOperationException("Missing the workbench diagnostic Locate button.");
        }

        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var treeHost = Find<FrameworkElement>(inspector, "TreeWorkbenchPane");
        var treeColumn = Find<ColumnDefinition>(inspector, "TreePaneColumn");
        var propertiesColumn = Find<ColumnDefinition>(inspector, "InspectorPaneColumn");
        var diagnosticsRow = Find<RowDefinition>(inspector, "DiagnosticsPaneRow");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var diagnosticsTab = Find<TabItem>(inspector, "WorkbenchDiagnosticsTab");
        var treePane = Find<MenuItem>(inspector, "ShowTreePaneItem");
        var propertiesPane = Find<MenuItem>(inspector, "ShowPropertiesPaneItem");
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var scope = new StackPanel();
        var branch = new Grid { Name = "DiagnosedElementParent" };
        var target = new Border
        {
            Name = "DiagnosedElement",
            Background = new SolidColorBrush(Colors.Crimson),
        };
        var unrelated = new Button { Name = "UnrelatedElement" };
        branch.Children.Add(target);
        scope.Children.Add(branch);
        scope.Children.Add(unrelated);
        inspector.ScopeTo(scope);
        SnoopUI.RefreshCommand.Execute(null, inspector);
        DrainDispatcher();

        var service = inspector.TreeService;
        var root = inspector.RootTreeItem ?? throw new InvalidOperationException("Missing the scoped diagnostics tree.");
        var targetNode = root.FindNode(target) ?? throw new InvalidOperationException("Missing the diagnosed element.");
        var otherNode = root.FindNode(unrelated) ?? throw new InvalidOperationException("Missing the unrelated element.");
        var row = service.DiagnosticContext.DiagnosticItems.Single(item =>
            item.DiagnosticProvider is LocalResourceDefinitionsDiagnosticProvider && ReferenceEquals(item.TreeItem, targetNode));
        Require(ReferenceEquals(view.DiagnosticContext, service.DiagnosticContext)
                && list.Items.Contains(row) && ReferenceEquals(row.SourceObject, target),
            "The diagnostics dock must receive the real Local brush diagnostic for the target.");

        var command = SnoopUI.LocateDiagnosticElementCommand;
        locate.DataContext = row;
        InspectorLocalization.Default.SetLanguage("en-US");
        DrainDispatcher();
        Require(locate.Content as string == "Locate" && ReferenceEquals(locate.Command, command)
                && ReferenceEquals(locate.CommandParameter, row) && locate.ToolTip is not null,
            "The English Locate button must run the diagnostic command for its own row.");
        InspectorLocalization.Default.SetLanguage("zh-CN");
        DrainDispatcher();
        Require(locate.Content as string == "定位", "The Locate button must switch to Simplified Chinese.");

        var globalRow = new DiagnosticItem(row.DiagnosticProvider, "Global regression fixture");
        service.DiagnosticContext.Add(globalRow);
        Require(command.CanExecute(row, inspector) && !command.CanExecute(globalRow, inspector),
            "Locate must be available for the real element and unavailable for a targetless diagnostic.");

        var setFilter = typeof(SnoopUI).GetMethod("SetFilter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing SnoopUI tree filter setter.");
        inspector.CurrentSelection = otherNode;
        properties.SelectedIndex = 1;
        diagnostics.SelectedItem = diagnosticsTab;
        propertyInspector.FilterString = "Width";
        Arrange();
        MoveSplitter("GridSplitter", 42, 0);
        MoveSplitter("PropertiesSplitter", -37, 0);
        MoveSplitter("DiagnosticsSplitter", 0, -45);
        var customTreeWidth = treeColumn.ActualWidth;
        var customPropertiesWidth = propertiesColumn.ActualWidth;
        var customDiagnosticsHeight = diagnosticsRow.ActualHeight;
        list.SelectedItem = row;
        Require(ReferenceEquals(inspector.CurrentSelection, otherNode), "Selecting a diagnostic row must leave the tree selection alone.");

        // Use the normal asynchronous filter action without waiting for the editable combo box's debounce.
        ApplyFilter(branch.Name);
        Require(targetNode.Parent is { } parent && inspector.TreeItems.Contains(parent),
            "The first query must include an ancestor of the diagnosed element.");
        SetTreeVisible(false);
        command.Execute(row, inspector);
        DrainDispatcher();
        VerifyLocation(branch.Name);

        ApplyFilter(unrelated.Name);
        inspector.CurrentSelection = otherNode;
        Require(inspector.TreeItems.Count == 1 && ReferenceEquals(inspector.TreeItems[0], otherNode),
            "The second query must hide the diagnosed element.");
        SetTreeVisible(false);
        SendKey(Key.Space);
        Require(!treePane.IsChecked && ReferenceEquals(inspector.CurrentSelection, otherNode),
            "A non-Enter key in the diagnostic list must leave the tree alone.");
        SendKey(Key.Enter);
        VerifyLocation(string.Empty);
        Require(inspector.TreeItems.Contains(root), "Locate must restore the unfiltered tree before selecting its hidden target.");

        ApplyFilter(branch.Name);
        SetTreeVisible(false);
        // Queue the new filter and Locate in the same Dispatcher turn.
        setFilter.Invoke(inspector, new object[] { unrelated.Name });
        command.Execute(row, inspector);
        DrainDispatcher();
        VerifyLocation(string.Empty);

        SnoopUI.RefreshCommand.Execute(null, inspector);
        DrainDispatcher();
        var refreshedSelection = inspector.CurrentSelection;
        Require(ReferenceEquals(inspector.TreeService, service) && !ReferenceEquals(inspector.RootTreeItem, root)
                && !command.CanExecute(row, inspector) && !command.CanExecute(globalRow, inspector),
            "Locate must reject rows from before a refresh of the same tree service.");
        command.Execute(row, inspector);
        command.Execute(globalRow, inspector);
        DrainDispatcher();
        Require(ReferenceEquals(inspector.CurrentSelection, refreshedSelection),
            "Executing a stale or targetless diagnostic must leave the current selection alone.");

        void ApplyFilter(string text)
        {
            setFilter.Invoke(inspector, new object[] { text });
            DrainDispatcher();
        }

        void SetTreeVisible(bool visible)
        {
            treePane.IsChecked = visible;
            treePane.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, treePane));
            DrainDispatcher();
        }

        void Arrange()
        {
            chrome.Measure(new Size(1200, 700));
            chrome.Arrange(new Rect(0, 0, 1200, 700));
            DrainDispatcher();
        }

        void MoveSplitter(string name, double x, double y)
        {
            var splitter = Find<GridSplitter>(inspector, name);
            ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(x, y);
            Arrange();
        }

        void SendKey(Key key)
        {
            // This provides a PresentationSource without displaying a native window.
            using var source = new HwndSource(new HwndSourceParameters("Snoop diagnostic list smoke") { Width = 1, Height = 1, WindowStyle = 0 });
            foreach (var routedEvent in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
                {
                    RoutedEvent = routedEvent,
                };
                list.RaiseEvent(input);
                if (input.Handled)
                {
                    break;
                }
            }

            DrainDispatcher();
        }

        void VerifyLocation(string expectedFilter)
        {
            Arrange();
            Require(treePane.IsChecked && treeHost.Visibility == Visibility.Visible
                    && Find<ProperTreeView>(inspector, "Tree").Visibility == Visibility.Visible
                    && ReferenceEquals(inspector.CurrentSelection, targetNode) && targetNode.IsSelected
                    && targetNode.Parent?.IsExpanded is true,
                "Locate must reveal the tree and select the exact diagnosed element.");
            Require(inspector.Filter == expectedFilter && ReferenceEquals(inspector.ScopedRootObject, scope)
                    && ReferenceEquals(inspector.RootTreeItem, root) && ReferenceEquals(list.SelectedItem, row),
                "Locate must preserve the scope and diagnostic row and clear only an obstructing tree filter.");
            Require(propertiesPane.IsChecked && properties.SelectedIndex == 1 && ReferenceEquals(diagnostics.SelectedItem, diagnosticsTab)
                    && ReferenceEquals(propertyInspector.RootTarget, target) && propertyInspector.FilterString == "Width",
                "Locate must update the property target without losing the inspector tabs or property filter.");
            Require(Math.Abs(treeColumn.ActualWidth - customTreeWidth) < 1
                    && Math.Abs(propertiesColumn.ActualWidth - customPropertiesWidth) < 1
                    && Math.Abs(diagnosticsRow.ActualHeight - customDiagnosticsHeight) < 1,
                "Locating a diagnostic changed a workbench splitter size.");
        }
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : class
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing diagnostic regression element " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }
}
