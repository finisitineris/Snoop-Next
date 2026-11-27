namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snoop.Controls;
using Snoop.Data;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class WorkbenchDisconnectSmoke
{
    internal const string Argument = "--workbench-disconnect-smoke";

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            Verify();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    internal static void Verify()
    {
        const string firstValue = "First disconnect edit";
        const string secondValue = "Other workbench edit";
        var settings = Snoop.Core.Settings.Default;
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        var originalTheme = settings.ThemeMode;
        var originalMainWindow = Application.Current.MainWindow;
        var firstButton = new Button { Name = "DisconnectFirstButton", Content = "First fixture" };
        var secondButton = new Button { Name = "DisconnectSecondButton", Content = "Second fixture" };
        var firstTarget = CreateTarget("Snoop disconnect first fixture", firstButton);
        var secondTarget = CreateTarget("Snoop disconnect second fixture", secondButton);
        SnoopUI? first = null;
        SnoopUI? second = null;
        SnoopUI? reopened = null;
        try
        {
            settings.ThemeMode = Snoop.Core.ThemeMode.Dark;
            localization.SetLanguage("en-US");
            firstTarget.Show();
            secondTarget.Show();
            first = new SnoopUI();
            first.ScopeTo(firstTarget.Content);
            first.Inspect(firstTarget);
            second = new SnoopUI();
            second.Inspect(secondTarget);
            var themeColor = ((SolidColorBrush)first.FindResource("SnoopNext.Brush.Background")).Color;

            var firstProperties = (PropertyInspector)first.FindName("PropertyInspector");
            var firstGrid = GetPropertyGrid(firstProperties);
            var firstPreview = (Previewer)first.FindName("PreviewArea");
            var firstUndo = HistoryButton(firstProperties, "UndoPropertyEditButton");
            var firstRedo = HistoryButton(firstProperties, "RedoPropertyEditButton");
            Edit(first, firstButton, firstValue);
            var retainedRow = Edit(first, firstButton, "Temporary first edit");
            Invoke(firstUndo);
            Await(() => Equals(firstButton.Content, firstValue) && CanExecute(firstUndo) && CanExecute(firstRedo),
                "The first workbench must have both undo and redo history before disconnect.");
            SaveEditedProperties(first);

            var secondProperties = (PropertyInspector)second.FindName("PropertyInspector");
            var secondUndo = HistoryButton(secondProperties, "UndoPropertyEditButton");
            Edit(second, secondButton, secondValue);
            SaveEditedProperties(second);
            var report = GetEditedReport();
            Require(report.Contains(firstValue) && report.Contains(secondValue),
                "The report must contain the two workbenches' separate edits before disconnect.");

            Disconnect(first);
            Await(() => !first.IsVisible, "Disconnect must close its workbench.");
            Require(first.RootObject is null && first.ScopedRootObject is null && first.RootTreeItem is null
                    && first.TreeService.RootTreeItem is null && first.CurrentSelection is null && first.TreeItems.Count == 0
                    && first.CurrentFocus is null && first.CurrentFocusScope is null,
                "The closed workbench must release its inspection roots, tree, scope, selection and focus.");
            Require(firstProperties.RootTarget is null && firstProperties.Target is null && firstGrid.Target is null
                    && firstGrid.Properties.Count == 0 && firstPreview.Target is null && firstPreview.ScopeTarget is null,
                "The closed workbench must release property and preview targets.");
            Require(!CanExecute(firstUndo) && !CanExecute(firstRedo), "Disconnect must discard both edit history stacks.");
            Require(!PropertyGrid2.ClearCommand.CanExecute(retainedRow, firstGrid),
                "A field retained from a disconnected workbench must not be eligible for Clear.");
            PropertyGrid2.ClearCommand.Execute(retainedRow, firstGrid);
            Require(Equals(firstButton.Content, firstValue) && firstTarget.IsVisible,
                "Disconnect and stale commands must leave the target window and its committed property value intact.");
            report = GetEditedReport();
            Require(!report.Contains(firstValue) && report.Contains(secondValue),
                "Disconnect must remove its own edited-properties report while preserving the other workbench's report.");
            Require(second.IsVisible && ReferenceEquals(second.CurrentSelection?.Target, secondButton)
                    && CanExecute(secondUndo) && Equals(secondButton.Content, secondValue),
                "The other workbench must retain its selection and usable history.");

            reopened = new SnoopUI();
            reopened.Inspect(firstTarget);
            var reopenedProperties = (PropertyInspector)reopened.FindName("PropertyInspector");
            var reopenedRow = SelectContent(reopened, firstButton);
            Require(!reopenedRow.IsValueChangedByUser && !CanExecute(HistoryButton(reopenedProperties, "UndoPropertyEditButton"))
                    && !CanExecute(HistoryButton(reopenedProperties, "RedoPropertyEditButton")),
                "Reopening the target must start with a fresh edit history.");
            Require(Equals(reopenedRow.Value, firstValue) && localization.LanguageTag == "en-US"
                    && settings.ThemeMode == Snoop.Core.ThemeMode.Dark
                    && Equals(((MenuItem)reopened.FindName("FileMenuItem")).Header, "File")
                    && ((SolidColorBrush)reopened.FindResource("SnoopNext.Brush.Background")).Color == themeColor,
                "A fresh workbench must show the committed value with the user's language and theme.");

            second.Close();
            Require(second.RootObject is null && secondProperties.RootTarget is null
                    && !CanExecute(secondUndo) && !GetEditedReport().Contains(secondValue),
                "Closing a workbench normally must run the same session cleanup as Disconnect.");
        }
        finally
        {
            Close(reopened);
            Close(second);
            Close(first);
            Application.Current.MainWindow = originalMainWindow;
            Close(secondTarget);
            Close(firstTarget);
            localization.SetLanguage(originalLanguage);
            settings.ThemeMode = originalTheme;
        }

        Console.WriteLine("PASS: workbench Disconnect and Close release inspected targets and edits without affecting another workbench or saved appearance.");
    }

    private static void Disconnect(SnoopUI inspector)
    {
        var item = (MenuItem?)inspector.FindName("DisconnectTargetItem")
            ?? throw new InvalidOperationException("The File menu must expose a Disconnect target action.");
        var label = InspectorLocalization.Default["Menu.DisconnectTarget"];
        Require(label != "Menu.DisconnectTarget" && Equals(item.Header, label),
            "The File menu's Disconnect action must be localized.");
        var peer = UIElementAutomationPeer.CreatePeerForElement(item);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static Window CreateTarget(string title, Button button) => new()
    {
        Title = title,
        Width = 280,
        Height = 120,
        ShowActivated = false,
        ShowInTaskbar = false,
        Content = new StackPanel { Children = { button } }
    };

    private static PropertyInformation Edit(SnoopUI inspector, Button button, string value)
    {
        var row = SelectContent(inspector, button);
        CommitText(SelectTextEditor(GetPropertyList(inspector), row), value);
        Await(() => Equals(button.Content, value) && row.IsValueChangedByUser,
            "The fixture property grid must commit and mark the Content edit.");
        return row;
    }

    private static PropertyInformation SelectContent(SnoopUI inspector, Button button)
    {
        inspector.Target = button;
        var list = GetPropertyList(inspector);
        Await(() => list.Items.Cast<PropertyInformation>().Any(MatchesContent),
            "The workbench must show the selected fixture button's Content row.");
        return list.Items.Cast<PropertyInformation>().Single(MatchesContent);

        bool MatchesContent(PropertyInformation row) => row.DependencyProperty == ContentControl.ContentProperty
            && ReferenceEquals(row.Target, button);
    }

    private static ListView GetPropertyList(SnoopUI inspector)
        => (ListView)GetPropertyGrid((PropertyInspector)inspector.FindName("PropertyInspector")).FindName("ListView");

    private static PropertyGrid2 GetPropertyGrid(PropertyInspector properties) => (PropertyGrid2)properties.FindName("PropertyGrid");

    private static Button HistoryButton(PropertyInspector properties, string name) => (Button)properties.FindName(name);

    private static bool CanExecute(Button button) => button.Command?.CanExecute(null) == true;

    private static void Invoke(Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static void SaveEditedProperties(SnoopUI inspector)
        => typeof(SnoopUI).GetMethod("SaveEditedProperties", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(inspector, new object?[] { inspector.CurrentSelection });

    private static string GetEditedReport()
        => (string)typeof(EditedPropertiesHelper).GetMethod("FormatReport", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;

    private static void Close(Window? window)
    {
        if (window?.IsVisible == true)
        {
            window.Close();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
