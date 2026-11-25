namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class InspectorFilterSmoke
{
    private static readonly MethodInfo ProcessFilter = typeof(SnoopUI).GetMethod("ProcessFilter", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SnoopUI filter callback was not found");

    public static void Verify(SnoopUI inspector)
    {
        var filterBox = inspector.FindName("filterComboBox") as ComboBox
            ?? throw new InvalidOperationException("SnoopUI tree filter was not found");
        var root = new SmokeTreeItem(new object(), null, inspector.TreeService);
        var ordinaryChild = new SmokeTreeItem("ordinary", root, inspector.TreeService);
        var bindingErrorChild = new SmokeTreeItem(new object(), root, inspector.TreeService, hasBindingError: true);
        var systemResources = new SystemResourcesTreeItem(null, inspector.TreeService);
        SetRoot(inspector, nameof(SnoopUI.RootTreeItem), root);
        SetRoot(inspector, nameof(SnoopUI.SystemResourcesTreeItem), systemResources);

        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        try
        {
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                InspectorLocalization.Default.SetLanguage(language);

                filterBox.SetCurrentValue(ComboBox.TextProperty, "String");
                ProcessFilter.Invoke(inspector, null);
                Require(filterBox.SelectedItem is null && inspector.TreeItems.Count == 1
                    && inspector.TreeItems[0] == ordinaryChild,
                    "literal tree search did not return the expected item in " + language);

                filterBox.SelectedIndex = 1;
                ProcessFilter.Invoke(inspector, null);
                Require(inspector.Filter == InspectorLocalization.Default["Tooltip.ShowBindingErrors"]
                    && inspector.TreeItems.Count == 1 && inspector.TreeItems[0] == bindingErrorChild,
                    "binding-error action did not return the expected item in " + language);

                filterBox.SelectedIndex = 0;
                ProcessFilter.Invoke(inspector, null);
                Require(inspector.Filter == string.Empty && filterBox.SelectedItem is null,
                    "clear action did not reset the tree filter in " + language);

                ProcessFilter.Invoke(inspector, null);
                Require(inspector.TreeItems.Count == 2 && inspector.TreeItems[0] == systemResources
                    && inspector.TreeItems[1] == root,
                    "clear action did not restore the unfiltered tree in " + language);
            }

            Console.WriteLine("PASS: SnoopUI localized filter actions and literal search");
        }
        finally
        {
            InspectorLocalization.Default.SetLanguage(originalLanguage);
        }
    }

    public static void VerifySearchRecovery(SnoopUI inspector)
    {
        var tree = (TreeView)inspector.FindName("Tree");
        var pane = (FrameworkElement)inspector.FindName("TreeWorkbenchPane");
        var filter = (ComboBox)inspector.FindName("filterComboBox");
        filter.ApplyTemplate();
        var editor = filter.Template.FindName("PART_EditableTextBox", filter) as TextBox
            ?? throw new InvalidOperationException("The tree filter needs an editable text box.");
        var empty = (FrameworkElement)inspector.FindName("TreeFilterEmptyState");
        var message = (TextBlock)inspector.FindName("TreeFilterEmptyText");
        var clear = (Button)inspector.FindName("ClearEmptyTreeFilterButton");
        var properties = (PropertyInspector)inspector.FindName("PropertyInspector");
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            // Both the tree and its query must use ordinal matching even inside a Turkish inspectee.
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var scope = new Grid { Name = "SearchScope" };
            var nested = new Border { Name = "NestedContent" };
            var firstGroup = new Border { Name = "ResultGroupA", Child = nested };
            var input = new TextBlock { Name = "NameInputResultGroupB", Text = "Selected field" };
            scope.Children.Add(firstGroup);
            scope.Children.Add(input);
            inspector.ScopeTo(scope);
            SnoopUI.RefreshCommand.Execute(null, inspector);
            var root = inspector.RootTreeItem ?? throw new InvalidOperationException("Missing scoped search root.");
            var leaf = root.FindNode(input) ?? throw new InvalidOperationException("Missing searchable leaf.");
            var systemResources = inspector.SystemResourcesTreeItem;
            root.IsExpanded = true;
            WaitFor(() => LeafRow() is not null, "realizing the initial tree row");
            Require(!empty.IsVisible, "An unfiltered tree must not show a search message.");
            LeafRow()!.IsSelected = true;
            WaitFor(() => ReferenceEquals(inspector.CurrentSelection, leaf), "selecting the leaf in its tree row");

            Search("textBLOCK", () => Only(leaf));
            Search("searchSCOPE", () => Only(root));
            Search("input", () => Only(leaf));
            Require(!empty.IsVisible, "A matching root or child must hide the empty state.");

            var groupNode = root.FindNode(firstGroup) ?? throw new InvalidOperationException("Missing first result group.");
            var nestedNode = root.FindNode(nested) ?? throw new InvalidOperationException("Missing nested result.");
            Search("ResultGroup", () => tree.Items.Count == 2 && tree.Items[0] == groupNode && tree.Items[1] == leaf);
            var reduceBranch = typeof(SnoopUI).GetMethod(nameof(SnoopUI.ApplyReduceDepthFilter),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TreeItem), typeof(TreeItem) }, null)
                ?? throw new InvalidOperationException("Missing branch-specific depth reduction.");
            reduceBranch.Invoke(inspector, new object[] { nestedNode, groupNode });
            WaitFor(() => tree.Items.Count == 2 && tree.Items[0] == nestedNode && tree.Items[1] == leaf
                    && ReferenceEquals(tree.SelectedItem, leaf) && leaf.IsSelected
                    && ReferenceEquals(inspector.CurrentSelection, leaf),
                "reducing the first result branch while retaining its selected sibling");

            // A layout callback queued for the old results must not repopulate the tree after a newer search is flushed.
            editor.SetCurrentValue(TextBox.TextProperty, "MissingAfterDepthChange");
            inspector.ApplyReduceDepthFilter(leaf);
            ProcessFilter.Invoke(inspector, null);
            inspector.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
                Require(tree.Items.Count == 0 && empty.IsVisible && leaf.IsSelected && ReferenceEquals(inspector.CurrentSelection, leaf),
                    "A stale depth reduction replaced the newer empty search results.")));
            ClearAndVerify();

            foreach (var language in new[] { "en-US", "zh-CN" })
            {
                localization.SetLanguage(language);
                ThemeManager.Current.ApplyTheme(language == "en-US" ? ThemeMode.Light : ThemeMode.Dark, inspector);
                Search("NoSuchTreeElement", () => empty.IsVisible && tree.Items.Count == 0);
                Require(message.Text == localization["Tree.FilterNoMatches"] && clear.IsVisible && clear.IsEnabled
                        && Equals(clear.Content, localization["Tree.ClearFilter"]),
                    "A literal miss must offer the localized explanation and a working reset in " + language);
                Require(leaf.IsSelected && ReferenceEquals(inspector.CurrentSelection, leaf) && ReferenceEquals(properties.RootTarget, input),
                    "An empty result must keep the selected element in the property inspector.");
                LauncherAppearanceSmoke.CaptureIfRequested(pane, "tree-search-empty-" + language);
                ClearAndVerify();

                filter.SelectedIndex = 1;
                WaitFor(() => empty.IsVisible && tree.Items.Count == 0, "filtering for binding errors");
                Require(leaf.IsSelected && ReferenceEquals(inspector.CurrentSelection, leaf)
                        && message.Text == localization["Tree.FilterNoBindingErrors"]
                        && message.Text != localization["Tree.FilterNoMatches"],
                    "Binding-error searches must distinguish a clean scope from a literal miss in " + language);
                ClearAndVerify();
            }

            Console.WriteLine("PASS: tree search matches names, types and the scoped root, and clearing either empty state restores the selected WPF row in EN/ZH.");

            TreeViewItem? LeafRow()
            {
                var rootRow = tree.ItemContainerGenerator.ContainerFromItem(root) as TreeViewItem;
                return rootRow?.ItemContainerGenerator.ContainerFromItem(leaf) as TreeViewItem;
            }

            bool Only(TreeItem item) => tree.Items.Count == 1 && ReferenceEquals(tree.Items[0], item);

            void Search(string query, Func<bool> result)
            {
                editor.SetCurrentValue(TextBox.TextProperty, query);
                WaitFor(() => inspector.Filter == query && result(), "applying query '" + query + "'");
            }

            void ClearAndVerify()
            {
                ((IInvokeProvider)new ButtonAutomationPeer(clear)).Invoke();
                WaitFor(() => filter.Text.Length == 0 && filter.SelectedIndex == -1 && !empty.IsVisible
                        && tree.Items.Count == 2 && ReferenceEquals(tree.Items[0], systemResources)
                        && ReferenceEquals(tree.Items[1], root) && LeafRow()?.IsSelected == true
                        && ReferenceEquals(tree.SelectedItem, leaf) && ReferenceEquals(inspector.CurrentSelection, leaf),
                    "restoring the unfiltered roots and originally selected tree row");
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            localization.SetLanguage(originalLanguage);
        }

        void WaitFor(Func<bool> condition, string operation)
        {
            // Let the editable ComboBox debounce and the real row containers finish updating.
            var timeout = Stopwatch.StartNew();
            do
            {
                inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                if (condition())
                {
                    return;
                }

                Thread.Sleep(10);
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(5));

            throw new InvalidOperationException($"Timed out {operation}. Filter '{inspector.Filter}', {tree.Items.Count} roots, selected row '{tree.SelectedItem}', message '{message.Text}'.");
        }
    }

    private static void SetRoot(SnoopUI inspector, string propertyName, TreeItem root)
    {
        var property = typeof(SnoopUI).GetProperty(propertyName)
            ?? throw new InvalidOperationException("SnoopUI root property was not found: " + propertyName);
        property.SetValue(inspector, root);
    }

    private static void Require(bool result, string description)
    {
        if (!result)
        {
            throw new InvalidOperationException(description);
        }
    }

    private sealed class SmokeTreeItem : TreeItem
    {
        public SmokeTreeItem(object target, SmokeTreeItem? parent, TreeService treeService, bool hasBindingError = false)
            : base(target, parent, treeService)
        {
            this.HasBindingError = hasBindingError;
            parent?.AddChild(this);
        }

        public override bool HasBindingError { get; }
    }
}
