namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;

internal static class InspectorTableSmoke
{
    public static void Verify()
    {
        var language = InspectorLocalization.Default.LanguageTag;
        using var context = new DiagnosticContext();
        var diagnostics = new DiagnosticsView { DiagnosticContext = context };
        var provider = context.DiagnosticProviders[0];
        foreach (var item in context.DiagnosticProviders)
        {
            item.IsActive = true;
        }

        context.Add(new DiagnosticItem(provider, "MainWindow.Title", "绑定路径未返回值", DiagnosticArea.Binding, DiagnosticLevel.Error));
        context.Add(new DiagnosticItem(provider, "ResourceDictionary", "检查当前窗口的资源引用", DiagnosticArea.Resource, DiagnosticLevel.Warning));
        context.Add(new DiagnosticItem(provider, "ItemsControl", "已启用列表虚拟化", DiagnosticArea.Performance));
        var properties = new PropertyGrid2 { Target = new Button { Content = "Snoop Next", Width = 160, Height = 36 } };
        var window = new SnoopBaseWindow
        {
            Width = Math.Min(760, SystemParameters.WorkArea.Width - 32),
            Height = Math.Min(420, SystemParameters.WorkArea.Height - 32),
            Left = 16, Top = 16,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        try
        {
            InspectorLocalization.Default.SetLanguage("zh-CN");
            window.Show();
            foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                ThemeManager.Current.ApplyTheme(theme, window);
                window.Title = "Snoop Next · 诊断";
                window.Content = diagnostics;
                Drain();
                var list = (ListView)diagnostics.FindName("diagnostics");
                list.SelectedIndex = 0;
                VerifyGridView(list);
                var chooser = Descendants(diagnostics).OfType<NoSelectionComboBox>().Single();
                chooser.IsDropDownOpen = true;
                try
                {
                    Drain();
                    var popup = (Popup)chooser.Template.FindName("PART_Popup", chooser);
                    var root = (FrameworkElement)popup.Child;
                    var checks = Descendants(root).OfType<CheckBox>().ToArray();
                    var check = checks.First(item => !ReferenceEquals(item.DataContext, provider));
                    var diagnosticProvider = (DiagnosticProvider)check.DataContext;
                    var peer = new CheckBoxAutomationPeer(check);
                    var toggle = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
                    var previous = diagnosticProvider.IsActive;
                    toggle.Toggle();
                    Drain();
                    if (checks.Length != context.DiagnosticProviders.Count || diagnosticProvider.IsActive == previous
                        || check.IsChecked != diagnosticProvider.IsActive || check.ActualHeight < 28
                        || check.Template.FindName("CheckBoxBorder", check) is not Border { CornerRadius.TopLeft: > 0 })
                    {
                        throw new InvalidOperationException("The actual diagnostic provider rows lost their Fluent checkbox, hit area or two-way toggle.");
                    }

                    InspectorFlyoutSmoke.Capture(diagnostics, "diagnostic-providers-" + theme, root);
                    toggle.Toggle();
                }
                finally
                {
                    chooser.IsDropDownOpen = false;
                }

                window.Title = "Snoop Next · 属性";
                window.Content = properties;
                Drain();
                var propertyList = (ListView)properties.FindName("ListView");
                propertyList.SelectedIndex = 0;
                VerifyGridView(propertyList);
                var nameHeader = Descendants(propertyList).OfType<GridViewColumnHeader>()
                    .First(header => ReferenceEquals(header.Column, ((GridView)propertyList.View).Columns[1]));
                if (!ReferenceEquals(nameHeader.Command, PropertyGrid2.SortCommand)
                    || nameHeader.Template.FindName("SortMark", nameHeader) is not FrameworkElement { IsVisible: true })
                {
                    throw new InvalidOperationException("The property table lost its sort command or direction indicator.");
                }

                InspectorFlyoutSmoke.Capture(properties, "property-table-" + theme);
                VerifyDataGrid(window, theme);
            }
        }
        finally
        {
            window.Close();
            diagnostics.DiagnosticContext = null;
            InspectorLocalization.Default.SetLanguage(language);
        }

        Console.WriteLine("PASS: both themes cover actual diagnostic/provider and property tables, plus editable and read-only WPF DataGrid cells.");
    }

    private static void VerifyGridView(ListView list)
    {
        Drain();
        var header = Descendants(list).OfType<GridViewColumnHeader>().First(item => item.Column is not null);
        if (list.Template.FindName("TableSurface", list) is not Border || !VirtualizingPanel.GetIsVirtualizing(list)
            || header.Template.FindName("PART_HeaderGripper", header) is not Thumb
            || header.Template.FindName("HeaderSurface", header) is not Border
            || list.ItemContainerGenerator.ContainerFromIndex(0) is not ListViewItem row
            || row.Template.FindName("SelectionRail", row) is not Border { IsVisible: true })
        {
            throw new InvalidOperationException("The populated GridView needs column headers, a selected row and virtualization.");
        }
    }

    private static void VerifyDataGrid(Window window, ThemeMode theme)
    {
        var values = new[] { new GridEntry("Width", "120"), new GridEntry("Height", "36") };
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = values, CanUserAddRows = false };
        var name = new DataGridTextColumn { Header = "属性", Binding = new Binding(nameof(GridEntry.Name)), Width = 180, IsReadOnly = true };
        var value = new DataGridTextColumn { Header = "值", Binding = new Binding(nameof(GridEntry.Value)), Width = 160 };
        grid.Columns.Add(name);
        grid.Columns.Add(value);
        window.Title = "Snoop Next · DataGrid";
        window.Content = grid;
        Drain();
        var heading = Descendants(grid).OfType<DataGridColumnHeader>().First(header => ReferenceEquals(header.Column, value));
        value.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        if (!grid.EnableRowVirtualization || !grid.EnableColumnVirtualization
            || heading.Template.FindName("PART_RightHeaderGripper", heading) is not Thumb
            || heading.Template.FindName("SortMark", heading) is not FrameworkElement { IsVisible: true })
        {
            throw new InvalidOperationException("The DataGrid lost virtualized columns, sizing or its sort indicator.");
        }

        grid.SelectedItem = values[0];
        grid.CurrentCell = new DataGridCellInfo(values[0], value);
        grid.Focus();
        if (!grid.BeginEdit() || value.GetCellContent(values[0]) is not TextBox edit)
        {
            throw new InvalidOperationException("The DataGrid could not enter its text editor.");
        }

        edit.Text = "160";
        if (!grid.CommitEdit(DataGridEditingUnit.Row, true) || values[0].Value != "160")
        {
            throw new InvalidOperationException("The DataGrid failed to commit the edited value.");
        }

        grid.CurrentCell = new DataGridCellInfo(values[0], name);
        if (grid.BeginEdit())
        {
            throw new InvalidOperationException("The read-only DataGrid column accepted an edit.");
        }

        InspectorFlyoutSmoke.Capture(grid, "datagrid-" + theme);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var item in Descendants(child))
            {
                yield return item;
            }
        }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    public sealed class GridEntry
    {
        public GridEntry(string name, string value)
        {
            this.Name = name;
            this.Value = value;
        }

        public string Name { get; }

        public string Value { get; set; }
    }
}
