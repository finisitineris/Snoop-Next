namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class WorkbenchLayoutSmoke
{
    private const double ReferenceWidth = 1360;
    private const double ReferenceHeight = 790;
    private const double CompactWidth = 420;
    private const double CompactHeight = 280;

    public static void Verify(SnoopUI inspector, string settingsDirectory)
    {
        var workbench = Find<Grid>(inspector, "WorkbenchGrid");
        Require(workbench.ColumnDefinitions.Count == 5,
            "The workbench needs independent splitter tracks between the tree, center preview, and property panes.");
        var center = Find<Grid>(inspector, "WorkbenchCenterGrid");
        Require(Grid.GetColumn(center) == 2 && center.IsAncestorOf(Find<FrameworkElement>(inspector, "PreviewArea"))
                && ReferenceEquals(workbench.ColumnDefinitions[2], Find<ColumnDefinition>(inspector, "CenterPaneColumn")),
            "The live Preview should be in the center column independently of the diagnostic tabs.");
        var settings = CoreSettings.Default;
        var originalSettingsFile = settings.SettingsFile;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        var snapshotPath = Path.Combine(settingsDirectory, "WorkbenchLayoutOriginal.xml");
        var layoutPath = Path.Combine(settingsDirectory, "WorkbenchLayoutSmoke.xml");
        try
        {
            settings.SettingsFile = snapshotPath;
            settings.Save();
            settings.SettingsFile = layoutPath;
            LoadLayout(new XElement("WorkbenchTreeFraction", "0.30"), new XElement("WorkbenchDiagnosticsFraction", "0.31"));
            var migratedTree = 0.30 * ReferenceWidth;
            var migratedDiagnostics = 0.31 * ReferenceHeight;

            var last = new PaneSizes(migratedTree, 354, migratedDiagnostics);
            WithInspector(older =>
            {
                VerifyPaneSizes(older, migratedTree, 354, migratedDiagnostics);
                WithInspector(edited =>
                {
                    VerifyPaneSizes(edited, migratedTree, 354, migratedDiagnostics);
                    var grid = Find<Grid>(edited, "WorkbenchGrid");
                    var centerGrid = Find<Grid>(edited, "WorkbenchCenterGrid");
                    RequireClose(grid.ColumnDefinitions[1].ActualWidth, 5, "Left splitter track");
                    RequireClose(grid.ColumnDefinitions[3].ActualWidth, 5, "Right splitter track");
                    RequireClose(centerGrid.RowDefinitions[Grid.GetRow(Find<GridSplitter>(edited, "DiagnosticsSplitter"))].ActualHeight,
                        5, "Diagnostics splitter track");

                    MoveSplitter(edited, "GridSplitter", 42, 0);
                    VerifyPaneSizes(edited, migratedTree + 42, 354, migratedDiagnostics);

                    MoveSplitter(edited, "PropertiesSplitter", -50, 0);
                    VerifyPaneSizes(edited, migratedTree + 42, 404, migratedDiagnostics);

                    MoveSplitter(edited, "DiagnosticsSplitter", 0, -36);
                    VerifyPaneSizes(edited, migratedTree + 42, 404, migratedDiagnostics + 36);
                    last = ReadPaneSizes(edited);

                    Arrange(edited, CompactWidth, CompactHeight);
                    var compact = ReadPaneSizes(edited);
                    Require(compact.Tree > 0 && compact.Tree < last.Tree && compact.Properties > 0
                            && compact.Properties < last.Properties && compact.Diagnostics < last.Diagnostics,
                        $"A compact workbench did not temporarily fit all three requested pane sizes. "
                        + $"Tree {compact.Tree}/{last.Tree}, Properties {compact.Properties}/{last.Properties}, Diagnostics {compact.Diagnostics}/{last.Diagnostics}.");
                    Require(Find<ColumnDefinition>(edited, "CenterPaneColumn").ActualWidth >= 239
                            && Find<RowDefinition>(edited, "PreviewPaneRow").ActualHeight >= 109
                            && compact.Diagnostics >= 109,
                        "The preview width and both center-row minima were lost at a compact size.");
                    Arrange(edited);
                    VerifyPaneSizes(edited, last.Tree, last.Properties, last.Diagnostics);
                    Arrange(edited, CompactWidth, CompactHeight);
                });
                VerifySavedSizes(last.Tree, last.Properties, last.Diagnostics);

                // This earlier window only changed the tree width. Closing it must keep
                // the other two preferences saved by the newer window.
                MoveSplitter(older, "GridSplitter", -50, 0);
                VerifyPaneSizes(older, migratedTree - 50, 354, migratedDiagnostics);
                last = new PaneSizes(ReadPaneSizes(older).Tree, last.Properties, last.Diagnostics);
            });
            VerifySavedSizes(last.Tree, last.Properties, last.Diagnostics);

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifyPaneSizes(reopened, last.Tree, last.Properties, last.Diagnostics);
                var reset = Find<MenuItem>(reopened, "ResetWorkbenchLayoutItem");
                InspectorLocalization.Default.SetLanguage("en-US");
                DrainDispatcher();
                Require(reset.Header as string == "Reset layout", "Missing English layout reset caption.");
                InspectorLocalization.Default.SetLanguage("zh-CN");
                DrainDispatcher();
                Require(reset.Header as string == "重置布局", "Missing Chinese layout reset caption.");
                reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, reset));
                Arrange(reopened);
                VerifyPaneSizes(reopened, 282, 354, 210);
                Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Reset layout changed the density preference.");
            });
            settings.Reload();
            WithInspector(reopened => VerifyPaneSizes(reopened, 282, 354, 210));
            WithInspector(InspectorFilterSmoke.VerifySearchRecovery, showWideWindow: true);
            WithInspector(WorkbenchPropertyPreviewSmoke.Verify, showWideWindow: true);

            LoadLayout(new XElement("WorkbenchTreeFraction", "invalid"), new XElement("WorkbenchDiagnosticsFraction", "0.42"),
                new XElement("WorkbenchTreeWidth", "NaN"), new XElement("WorkbenchPropertiesWidth", "389"),
                new XElement("WorkbenchDiagnosticsHeight", "invalid"));
            WithInspector(malformed => VerifyPaneSizes(malformed, 282, 389, 0.42 * ReferenceHeight));

            LoadLayout(new XElement("WorkbenchTreeFraction", "0.98"), new XElement("WorkbenchDiagnosticsFraction", "0.02"),
                new XElement("WorkbenchPropertiesWidth", "Infinity"));
            WithInspector(legacy =>
            {
                Arrange(legacy, 2200, ReferenceHeight);
                VerifyPaneSizes(legacy, 0.98 * ReferenceWidth, 354, 110);
            });

            LoadLayout(new XElement("WorkbenchTreeWidth", "282"));
            WithInspector(edited =>
            {
                Arrange(edited, 1920, ReferenceHeight);
                var splitter = Find<GridSplitter>(edited, "GridSplitter");
                ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(700, 0);
                Arrange(edited, 1920, ReferenceHeight);
                VerifyPaneSizes(edited, 982, 354, 210);
                Arrange(edited, CompactWidth, CompactHeight);
                Require(Find<ColumnDefinition>(edited, "CenterPaneColumn").ActualWidth >= 239,
                    "A wide tree preference must still leave the center usable on a small window.");
            });
            RequireSavedSize(XDocument.Load(layoutPath).Root, "WorkbenchTreeWidth", 982);
            settings.Reload();
            WithInspector(reopened =>
            {
                Arrange(reopened, 1920, ReferenceHeight);
                VerifyPaneSizes(reopened, 982, 354, 210);
            });
            WithInspector(WorkbenchTreeOverflowSmoke.Verify, showWideWindow: true);

            LoadLayout(new XElement("WorkbenchTreeFraction", "0.23"), new XElement("WorkbenchDiagnosticsFraction", "0.33"),
                new XElement("WorkbenchTreeWidth", "315"), new XElement("WorkbenchPropertiesWidth", "610"),
                new XElement("WorkbenchDiagnosticsHeight", "195"));
            WithInspector(current => VerifyPaneSizes(current, 315, 590, 195));

            LoadLayout(new XElement("WorkbenchTreeFraction", "1"), new XElement("WorkbenchDiagnosticsFraction", "NaN"));
            WithInspector(malformed => VerifyPaneSizes(malformed, 282, 354, 210));
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                settings.SettingsFile = snapshotPath;
                settings.Reload();
            }

            settings.SettingsFile = originalSettingsFile;
            InspectorLocalization.Default.SetLanguage(originalLanguage);
            File.Delete(snapshotPath);
            File.Delete(layoutPath);
        }

        Console.WriteLine("PASS: workbench splitter bounds, persistence, layout reset and XML fallback");

        void LoadLayout(params XElement[] elements)
        {
            var xml = new XElement("Settings", new XElement("WorkbenchDensity", CoreSettings.CompactDensity), elements);
            new XDocument(xml).Save(layoutPath);
            settings.Reload();
            Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Layout XML lost the density preference.");
        }

        void VerifySavedSizes(double tree, double properties, double diagnostics)
        {
            var xml = XDocument.Load(layoutPath).Root;
            RequireSavedSize(xml, "WorkbenchTreeWidth", tree);
            RequireSavedSize(xml, "WorkbenchPropertiesWidth", properties);
            RequireSavedSize(xml, "WorkbenchDiagnosticsHeight", diagnostics);
        }
    }

    private static void WithInspector(Action<SnoopUI> action, bool showWideWindow = false)
    {
        var inspector = new SnoopUI();
        try
        {
            if (showWideWindow)
            {
                inspector.Show();
                inspector.WindowState = WindowState.Normal;
                inspector.Left = SystemParameters.WorkArea.Left + 8;
                inspector.Top = SystemParameters.WorkArea.Top + 8;
                inspector.Width = Math.Min(1740, SystemParameters.WorkArea.Width - 16);
                inspector.Height = Math.Min(760, SystemParameters.WorkArea.Height - 16);
                inspector.UpdateLayout();
                DrainDispatcher();
            }
            else
            {
                // Hidden fixture windows do not run OnSourceInitialized.
                CacheManager.Instance.IncreaseUsageCount();
                Arrange(inspector);
            }

            action(inspector);
        }
        finally
        {
            inspector.Close();
        }
    }

    private static void MoveSplitter(SnoopUI inspector, string name, double x, double y)
    {
        var splitter = Find<GridSplitter>(inspector, name);
        ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(x, y);
        Arrange(inspector);
    }

    internal static void Arrange(SnoopUI inspector, double width = ReferenceWidth, double height = ReferenceHeight)
    {
        Arrange(Find<Grid>(inspector, "WorkbenchGrid"), new Rect(0, 0, width, height));
    }

    internal static void Arrange(FrameworkElement element, Rect bounds)
    {
        element.Measure(bounds.Size);
        element.Arrange(bounds);
        element.UpdateLayout();
        // Hidden smoke windows need an explicit second layout after the viewport changes.
        element.Measure(bounds.Size);
        element.Arrange(bounds);
        element.UpdateLayout();
        DrainDispatcher();
    }

    internal static void RequireSavedSize(XElement? xml, string name, double expected)
    {
        Require(double.TryParse((string?)xml?.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var size),
            "Closing the workbench failed to save " + name + ".");
        RequireClose(size, expected, name);
    }

    internal static void VerifyPaneSizes(SnoopUI inspector, double tree, double properties, double diagnostics)
    {
        var actual = ReadPaneSizes(inspector);
        RequireClose(actual.Tree, tree, "Tree pane width");
        RequireClose(actual.Properties, properties, "Properties pane width");
        RequireClose(actual.Diagnostics, diagnostics, "Diagnostics pane height");
    }

    internal static PaneSizes ReadPaneSizes(SnoopUI inspector)
    {
        return new PaneSizes(Find<ColumnDefinition>(inspector, "TreePaneColumn").ActualWidth,
            Find<ColumnDefinition>(inspector, "InspectorPaneColumn").ActualWidth,
            Find<RowDefinition>(inspector, "DiagnosticsPaneRow").ActualHeight);
    }

    internal readonly struct PaneSizes
    {
        public PaneSizes(double tree, double properties, double diagnostics)
        {
            this.Tree = tree;
            this.Properties = properties;
            this.Diagnostics = diagnostics;
        }

        public double Tree { get; }

        public double Properties { get; }

        public double Diagnostics { get; }
    }

    private static void RequireClose(double actual, double expected, string description)
    {
        // Layout rounding and the native splitter can each round by half a device pixel.
        Require(Math.Abs(actual - expected) <= 1.001, $"{description} expected {expected} DIP, got {actual} DIP.");
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : class
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench layout element " + name);
    }
}
