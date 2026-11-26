namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Snoop.Controls;
using Snoop.Controls.ValueEditors;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

internal static class PropertyOriginFilterSmoke
{
    public static void Verify()
    {
        var language = InspectorLocalization.Default.LanguageTag;
        var source = new Border { Width = 260 };
        var target = new Button
        {
            Name = "SaveButton",
            Height = 36,
            IsDefault = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = new Style(typeof(Button)) { Setters = { new Setter(FrameworkElement.MinWidthProperty, 80d) } },
        };
        target.Resources.Add("ActionWidth", 160d);
        target.SetResourceReference(FrameworkElement.WidthProperty, "ActionWidth");
        var binding = target.SetBinding(FrameworkElement.MaxWidthProperty,
            new Binding(nameof(FrameworkElement.Width)) { Source = source, Mode = BindingMode.OneWay });
        var inspector = new PropertyInspector { RootTarget = target };
        var window = new SnoopBaseWindow { Content = inspector, Width = 740, Height = 570, Title = "SaveButton" };
        try
        {
            InspectorLocalization.Default.SetLanguage("en-US");
            window.Show();
            Drain();
            var grid = (PropertyGrid2)inspector.FindName("PropertyGrid");
            var list = (ListView)grid.FindName("ListView");
            var search = (TextBox)inspector.FindName("PropertiesFilter");
            var origins = (ComboBox)inspector.FindName("PropertyOriginFilterCombo");
            var edited = (ToggleButton)inspector.FindName("EditedOnlyToggle");
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(edited).GetPattern(PatternInterface.Toggle);
            PropertyInformation Row(DependencyProperty dp) => grid.Properties.Single(row => row.DependencyProperty == dp);

            var bindingRow = Row(FrameworkElement.MaxWidthProperty);
            var localRow = Row(FrameworkElement.HeightProperty);
            var styleRow = Row(FrameworkElement.MinWidthProperty);
            var resourceRow = Row(FrameworkElement.WidthProperty);
            var fixtureRows = new[] { bindingRow, localRow, styleRow, resourceRow };
            void ExpectRows(string action, params PropertyInformation[] expected)
            {
                Await(() => fixtureRows.All(row => list.Items.Contains(row) == expected.Contains(row)) && ViewportMatchesItems(list),
                    action + " must show these fixture rows: " + string.Join(", ", expected.Select(row => row.DisplayName)));
            }

            void SelectOrigin(PropertyOriginFilter origin) => origins.SetCurrentValue(Selector.SelectedValueProperty, origin);
            bool OnlyHeight() => list.Items.Count == 1 && ((PropertyInformation)list.Items[0]).DependencyProperty == FrameworkElement.HeightProperty
                                 && ViewportMatchesItems(list);

            Require(Equals(origins.SelectedValue, PropertyOriginFilter.All) && edited.IsChecked == false,
                "The inspector must initially show all origins and include unedited properties.");
            ExpectRows("Initial view", fixtureRows);
            VerifySourceSorting(list, bindingRow, localRow, Row(FrameworkElement.HorizontalAlignmentProperty), styleRow);

            SelectOrigin(PropertyOriginFilter.Binding);
            ExpectRows("Binding", bindingRow);
            target.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source });
            ExpectRows("A new binding while Binding is selected", bindingRow, localRow);
            target.Height = 36;
            ExpectRows("Restoring the literal while Binding is selected", bindingRow);
            Await(() => list.Items.Count == 1 && ReferenceEquals(list.Items[0], bindingRow) && ViewportMatchesItems(list),
                "The English Binding screenshot must display a single realized MaxWidth row.");
            VerifyPropertyAutomation(list, search, bindingRow);
            InspectorFlyoutSmoke.Capture(inspector, "property-filters-binding-en-US");
            SelectOrigin(PropertyOriginFilter.Style);
            ExpectRows("Style", styleRow);
            SelectOrigin(PropertyOriginFilter.Local);
            ExpectRows("Local", localRow, resourceRow);
            var searchProvider = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(search).GetPattern(PatternInterface.Value);
            searchProvider.SetValue("Width");
            ExpectRows("Local with Width search", resourceRow);
            SelectOrigin(PropertyOriginFilter.All);
            ExpectRows("All with Width search", bindingRow, styleRow, resourceRow);
            searchProvider.SetValue(string.Empty);
            ExpectRows("Clear search and origin", fixtureRows);

            // Realize the enum editor before changing its target, so the source-to-editor binding runs.
            var enumContainer = SelectRow(list, Row(FrameworkElement.HorizontalAlignmentProperty));
            var enumEditor = InspectorTableSmoke.Descendants(enumContainer).OfType<ComboBox>().Single();
            target.HorizontalAlignment = HorizontalAlignment.Right;
            source.Width = 290;
            Drain();
            Require(enumEditor.SelectedItem is EnumValueWrapper { Value: HorizontalAlignment.Right } && target.MaxWidth == 290,
                "The realized enum editor and bound target must receive application changes.");
            toggle.Toggle();
            Await(() => list.Items.IsEmpty, "Inspecting rows, changing an enum and updating a binding must leave Edited only empty.");
            toggle.Toggle();
            ExpectRows("Turn off Edited only", fixtureRows);

            var value = SelectTextEditor(list, localRow);
            CommitText(value, "036");
            Require(target.Height == 36 && !localRow.IsValueChangedByUser, "An equivalent numeric edit must remain unmarked.");
            CommitText(value, "-1");
            Require(target.Height == 36 && !localRow.IsValueChangedByUser,
                "A rejected Height must leave the original value and edited state intact.");
            CommitText(value, "48");
            Require(target.Height == 48, "Pressing Enter in the Height value cell must commit the edit to the button.");
            toggle.Toggle();
            Await(OnlyHeight, "Edited only must now contain precisely the committed Height row.");
            SelectOrigin(PropertyOriginFilter.Binding);
            Await(() => list.Items.IsEmpty, "The edited Height must be excluded by the Binding origin.");
            SelectOrigin(PropertyOriginFilter.Local);
            Await(OnlyHeight, "The Local and Edited only intersection must contain Height.");

            var englishLocal = ((ComboBoxItem)origins.SelectedItem).Content;
            var englishEdited = edited.Content;
            inspector.Width = 354;
            window.SizeToContent = SizeToContent.Width;
            InspectorLocalization.Default.SetLanguage("zh-CN");
            Drain();
            Require(Equals(origins.SelectedValue, PropertyOriginFilter.Local) && edited.IsChecked == true && OnlyHeight()
                    && !Equals(((ComboBoxItem)origins.SelectedItem).Content, englishLocal) && !Equals(edited.Content, englishEdited),
                "Switching languages must translate both controls and preserve the active filters and edited result.");
            VerifyPropertyAutomation(list, search, localRow);
            InspectorFlyoutSmoke.Capture(inspector, "property-filters-edited-zh-CN");
            VerifyEditedExport(inspector.PropertyFilter, target);

            grid.RefreshPropertyGrid();
            Await(OnlyHeight, "Rebuilding the grid must retain the edited property for this button.");
            var other = new Button { Height = 48 };
            inspector.RootTarget = other;
            Await(() => grid.Properties.Count > 0 && list.Items.IsEmpty,
                "The second button has the same Height value but no inspector edits.");
            inspector.RootTarget = target;
            Await(OnlyHeight, "Reselecting the first button must restore its edited Height row.");
            toggle.Toggle();
            SelectOrigin(PropertyOriginFilter.All);
            Await(() => list.Items.Contains(Row(FrameworkElement.WidthProperty)) && list.Items.Contains(Row(FrameworkElement.MinWidthProperty))
                    && list.Items.Contains(Row(FrameworkElement.MaxWidthProperty)) && list.Items.Contains(Row(FrameworkElement.HeightProperty))
                    && ViewportMatchesItems(list),
                "Clearing both filters must restore all four origins.");
            var alignment = Row(FrameworkElement.HorizontalAlignmentProperty);
            var choices = InspectorTableSmoke.Descendants(SelectRow(list, alignment)).OfType<ComboBox>().Single();
            choices.SetCurrentValue(Selector.SelectedItemProperty,
                choices.Items.Cast<EnumValueWrapper>().Single(choice => Equals(choice.Value, HorizontalAlignment.Center)));
            var isDefault = Row(Button.IsDefaultProperty);
            var check = InspectorTableSmoke.Descendants(SelectRow(list, isDefault)).OfType<CheckBox>().Single(box => box.Name == "PART_CheckBox");
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(check).GetPattern(PatternInterface.Toggle)).Toggle();
            toggle.Toggle();
            Await(() => target.HorizontalAlignment == HorizontalAlignment.Center && target.IsDefault && list.Items.Count == 3
                        && list.Items.Contains(alignment) && list.Items.Contains(isDefault) && list.Items.Contains(Row(FrameworkElement.HeightProperty))
                        && ViewportMatchesItems(list),
                "Changing the enum choice and toggling the Boolean editor must commit both values and list exactly three edited properties.");
            Require(ReferenceEquals(binding, BindingOperations.GetBindingExpressionBase(target, FrameworkElement.MaxWidthProperty))
                    && target.MaxWidth == 290 && target.Width == 160 && target.MinWidth == 80 && target.Height == 48,
                "Filtering, sorting and exporting must preserve the target's binding, resource, setter and edited literal.");
        }
        finally
        {
            window.Close();
            InspectorLocalization.Default.SetLanguage(language);
        }

        Console.WriteLine("PASS: property origin and edited-only controls filter real WPF rows, preserve bindings and isolate edits by target.");
    }

    private static void VerifyPropertyAutomation(ListView list, TextBox filter, PropertyInformation expected)
    {
        var filterPeer = UIElementAutomationPeer.CreatePeerForElement(filter);
        Require(filterPeer.GetAutomationControlType() == AutomationControlType.Edit && filterPeer.GetAutomationId() == "PropertiesFilter"
                && filterPeer.GetName() == InspectorLocalization.Default["PropertyInspector.FilterPlaceholder"]
                && filterPeer.GetPattern(PatternInterface.Value) is IValueProvider { IsReadOnly: false },
            "The property search must expose its localized name, stable ID and editable value.");

        var table = UIElementAutomationPeer.CreatePeerForElement(list);
        Require(table.GetAutomationControlType() == AutomationControlType.DataGrid && table.GetAutomationId() == "ListView"
                && table.GetName() == InspectorLocalization.Default["Tab.Properties"]
                && table.GetPattern(PatternInterface.Grid) is IGridProvider { RowCount: 1 }
                && table.GetPattern(PatternInterface.Selection) is ISelectionProvider,
            "The filtered property table must be identifiable and expose its native grid and selection patterns.");
        var row = table.GetChildren().SingleOrDefault(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem
                                                             && peer.GetAutomationId() == expected.Name);
        if (row is null || row.GetName() != expected.DisplayName || row.GetPattern(PatternInterface.SelectionItem) is not ISelectionItemProvider selection)
        {
            throw new InvalidOperationException("The property table must expose a selectable, named row for " + expected.Name);
        }

        selection.Select();
        Drain();
        var valueCell = row.GetChildren().Single(cell => cell.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Column: 2 });
        Require(selection.IsSelected && ReferenceEquals(list.SelectedItem, expected)
                && DescendantPeers(valueCell).Any(peer => peer.GetAutomationControlType() == AutomationControlType.Edit
                    && peer.GetPattern(PatternInterface.Value) is IValueProvider value && value.Value == expected.StringValue),
            "Automation must be able to select the property and read its current numeric value: " + expected.Name);
    }

    private static IEnumerable<AutomationPeer> DescendantPeers(AutomationPeer parent)
    {
        foreach (var child in parent.GetChildren() ?? Enumerable.Empty<AutomationPeer>())
        {
            yield return child;
            foreach (var peer in DescendantPeers(child))
            {
                yield return peer;
            }
        }
    }

    internal static bool ViewportMatchesItems(ListView list)
    {
        // Check the rendered name cells: ListCollectionView can rebuild its internal
        // list without telling the virtualized ListView to replace its row containers.
        var viewport = new Rect(list.RenderSize);
        var rendered = InspectorTableSmoke.Descendants(list).OfType<ListViewItem>()
            .Where(item => item.IsVisible && item.TransformToAncestor(list).TransformBounds(new Rect(item.RenderSize)).IntersectsWith(viewport))
            .ToArray();
        if (rendered.Length < Math.Min(2, list.Items.Count))
        {
            return false;
        }

        return rendered.All(item =>
        {
            var index = list.ItemContainerGenerator.IndexFromContainer(item);
            return index >= 0 && index < list.Items.Count && list.Items[index] is PropertyInformation row && ReferenceEquals(item.DataContext, row)
                && InspectorTableSmoke.Descendants(item).OfType<TextBlock>().Any(cell => cell.IsVisible && cell.Text == row.DisplayName);
        });
    }

    private static ListViewItem SelectRow(ListView list, PropertyInformation row)
    {
        Require(list.Items.Contains(row), "The property row must be present in the filtered view: " + row.DisplayName);
        list.SetCurrentValue(Selector.SelectedItemProperty, row);
        list.ScrollIntoView(row);
        list.UpdateLayout();
        Await(() => list.ItemContainerGenerator.ContainerFromItem(row) is ListViewItem { IsSelected: true, IsVisible: true },
            "The selected property row was not realized: " + row.DisplayName);
        return (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
    }

    internal static EditTextBox SelectTextEditor(ListView list, PropertyInformation row) =>
        InspectorTableSmoke.Descendants(SelectRow(list, row)).OfType<EditTextBox>().Single();

    internal static void CommitText(EditTextBox editor, string text)
    {
        editor.SetCurrentValue(TextBox.TextProperty, text);
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
        Drain();
    }

    private static void VerifySourceSorting(ListView list, PropertyInformation binding, PropertyInformation height, PropertyInformation alignment, PropertyInformation style)
    {
        var header = InspectorTableSmoke.Descendants(list).OfType<GridViewColumnHeader>().Single(item =>
            item.Column?.Header is TextBlock text && text.Text == InspectorLocalization.Default["PropertyGrid.ColumnValueSource"]);
        var invoke = (IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(header).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        Await(() => GridViewSort.GetDirection(header.Column) == ListSortDirection.Ascending, "The source header must sort ascending.");
        var ascending = new[] { binding, height, alignment, style };
        Require(list.Items.Cast<PropertyInformation>().Where(ascending.Contains).SequenceEqual(ascending),
            "Ascending origin sort must put Binding, Local and Style in order, with Height before HorizontalAlignment.");
        invoke.Invoke();
        Await(() => GridViewSort.GetDirection(header.Column) == ListSortDirection.Descending, "The source header must sort descending.");
        var descending = new[] { style, height, alignment, binding };
        Require(list.Items.Cast<PropertyInformation>().Where(descending.Contains).SequenceEqual(descending),
            "Descending origin sort must reverse the groups and retain name order inside Local.");
    }

    private static void VerifyEditedExport(PropertyFilter filter, Button target)
    {
        using var service = TreeService.From(TreeType.Visual);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        TreeExporter.Export(service.Construct(target, null), writer, filter, new ExportOptions { ExportXamlStyle = true });
        var attributes = XDocument.Parse(writer.ToString()).Root!.Attributes().ToArray();
        Require(attributes.Length == 1 && attributes[0].Name.LocalName == "Height" && attributes[0].Value == "48",
            "A filtered export must include just the edited Height, even though the exporter creates its own property rows.");
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void Await(Func<bool> condition, string message)
    {
        var timeout = Stopwatch.StartNew();
        do
        {
            Drain();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }
        while (timeout.Elapsed < TimeSpan.FromSeconds(5));

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
