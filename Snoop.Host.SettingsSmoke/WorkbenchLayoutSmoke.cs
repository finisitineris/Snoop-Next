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
    private const double DefaultTreeFraction = 1.0 / 3;
    private const double DefaultDiagnosticsFraction = 3.0 / 13;

    public static void Verify(SnoopUI inspector, string settingsDirectory)
    {
        Find<Grid>(inspector, "WorkbenchGrid");
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
            LoadLayout("0.36", "0.31");

            var lastDiagnostics = 0.0;
            var lastTree = 0.0;
            WithInspector(older =>
            {
                VerifyFractions(older, 0.36, 0.31);
                WithInspector(edited =>
                {
                    VerifyFractions(edited, 0.36, 0.31);
                    var grid = Find<Grid>(edited, "WorkbenchGrid");
                    Arrange(grid, 1200, 700);
                    MoveSplitter(edited, "GridSplitter", 75, 0);
                    Arrange(grid, 1200, 700);
                    MoveSplitter(edited, "DiagnosticsSplitter", 0, -45);
                    lastTree = TreeFraction(edited);
                    lastDiagnostics = DiagnosticsFraction(edited);
                    Require(lastTree > 0.36 && lastDiagnostics > 0.31, "The WPF splitters did not change both star ratios.");

                    Arrange(grid, 480, 320);
                    Require(Find<ColumnDefinition>(edited, "TreePaneColumn").ActualWidth >= 120
                            && Find<ColumnDefinition>(edited, "InspectorPaneColumn").ActualWidth >= 220
                            && Find<RowDefinition>(edited, "PropertiesPaneRow").ActualHeight >= 80
                            && Find<RowDefinition>(edited, "DiagnosticsPaneRow").ActualHeight >= 100,
                        "The workbench panel minima were lost at a narrow size.");
                    VerifyFractions(edited, lastTree, lastDiagnostics);
                });
                VerifySavedFractions(lastTree, lastDiagnostics);

                // This earlier window only changed the tree width. Closing it must keep
                // the diagnostics preference saved by the newer window.
                Arrange(Find<Grid>(older, "WorkbenchGrid"), 1000, 650);
                MoveSplitter(older, "GridSplitter", -50, 0);
                lastTree = TreeFraction(older);
                Require(lastTree < 0.36, "The older workbench did not change its tree width.");
            });
            VerifySavedFractions(lastTree, lastDiagnostics);

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifyFractions(reopened, lastTree, lastDiagnostics);
                var reset = Find<MenuItem>(reopened, "ResetWorkbenchLayoutItem");
                InspectorLocalization.Default.SetLanguage("en-US");
                DrainDispatcher();
                Require(reset.Header as string == "Reset layout", "Missing English layout reset caption.");
                InspectorLocalization.Default.SetLanguage("zh-CN");
                DrainDispatcher();
                Require(reset.Header as string == "重置布局", "Missing Chinese layout reset caption.");
                reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, reset));
                DrainDispatcher();
                VerifyFractions(reopened, DefaultTreeFraction, DefaultDiagnosticsFraction);
                Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Reset layout changed the density preference.");
            });
            settings.Reload();
            WithInspector(reopened => VerifyFractions(reopened, DefaultTreeFraction, DefaultDiagnosticsFraction));

            LoadLayout("invalid", "0.42");
            WithInspector(malformed => VerifyFractions(malformed, DefaultTreeFraction, 0.42));
            LoadLayout("0.47", "NaN");
            WithInspector(malformed => VerifyFractions(malformed, 0.47, DefaultDiagnosticsFraction));
            LoadLayout(null, null);
            WithInspector(legacy => VerifyFractions(legacy, DefaultTreeFraction, DefaultDiagnosticsFraction));
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

        void LoadLayout(string? treeFraction, string? diagnosticsFraction)
        {
            new XDocument(new XElement("Settings",
                new XElement("WorkbenchDensity", CoreSettings.CompactDensity),
                treeFraction is null ? null : new XElement("WorkbenchTreeFraction", treeFraction),
                diagnosticsFraction is null ? null : new XElement("WorkbenchDiagnosticsFraction", diagnosticsFraction)))
                .Save(layoutPath);
            settings.Reload();
            Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Layout XML lost the density preference.");
        }

        void VerifySavedFractions(double tree, double diagnostics)
        {
            var xml = XDocument.Load(layoutPath).Root;
            Require(TryRead(xml, "WorkbenchTreeFraction", out var savedTree) && Approximately(savedTree, tree)
                    && TryRead(xml, "WorkbenchDiagnosticsFraction", out var savedDiagnostics) && Approximately(savedDiagnostics, diagnostics),
                "Closing a workbench did not save both expected splitter ratios.");
        }
    }

    private static void WithInspector(Action<SnoopUI> action)
    {
        var inspector = new SnoopUI();
        // Hidden fixture windows do not run OnSourceInitialized, which normally increments the count.
        CacheManager.Instance.IncreaseUsageCount();
        try
        {
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
    }

    private static void Arrange(Grid grid, double width, double height)
    {
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
    }

    private static bool TryRead(XElement? root, string name, out double value)
    {
        return double.TryParse((string?)root?.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void VerifyFractions(SnoopUI inspector, double tree, double diagnostics)
    {
        Require(Approximately(TreeFraction(inspector), tree), "Unexpected tree pane star ratio.");
        Require(Approximately(DiagnosticsFraction(inspector), diagnostics), "Unexpected diagnostics pane star ratio.");
    }

    private static double TreeFraction(SnoopUI inspector)
    {
        return Fraction(Find<ColumnDefinition>(inspector, "TreePaneColumn").Width,
            Find<ColumnDefinition>(inspector, "InspectorPaneColumn").Width);
    }

    private static double DiagnosticsFraction(SnoopUI inspector)
    {
        return Fraction(Find<RowDefinition>(inspector, "DiagnosticsPaneRow").Height,
            Find<RowDefinition>(inspector, "PropertiesPaneRow").Height);
    }

    private static double Fraction(GridLength pane, GridLength other)
    {
        Require(pane.IsStar && other.IsStar, "Workbench pane definitions must remain proportional.");
        return pane.Value / (pane.Value + other.Value);
    }

    private static bool Approximately(double actual, double expected)
    {
        return Math.Abs(actual - expected) < 0.000001;
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
