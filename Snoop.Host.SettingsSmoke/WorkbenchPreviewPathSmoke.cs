namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchPreviewPathSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var path = inspector.FindName("WorkbenchPreviewSelectionPath") as TextBlock
            ?? throw new InvalidOperationException("Missing the workbench preview selection path.");
        var previewPane = Find<FrameworkElement>(inspector, "WorkbenchPreviewPane");
        var preview = Find<Previewer>(inspector, "PreviewArea");
        var properties = Find<PropertyInspector>(inspector, "PropertyInspector");
        var status = Find<TextBlock>(inspector, "WorkbenchSelectionValue");
        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        Require(path.TextTrimming == TextTrimming.CharacterEllipsis, "The preview path must trim long text.");
        if (path.ToolTip is not ToolTip { Content: TextBlock tooltipText } tooltip)
        {
            throw new InvalidOperationException("Missing the full preview path tooltip.");
        }

        Require(tooltipText.TextWrapping == TextWrapping.Wrap && tooltipText.MaxWidth == 420,
            "The full path tooltip must wrap instead of exceeding the window.");
        var savedTooltipTarget = tooltip.PlacementTarget;
        var savedSelection = inspector.CurrentSelection;
        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var previewActive = preview.IsActive;
        var outer = new Grid { Name = "OutsideInspectionScope" };
        var scope = new Grid { Name = "FixtureScope" };
        var group = new StackPanel();
        var save = new Button { Name = "SaveButton" };
        var input = new TextBox();
        outer.Children.Add(scope);
        scope.Children.Add(group);
        group.Children.Add(save);
        group.Children.Add(input);
        using var tree = TreeService.From(TreeType.Visual);
        // Keep the selected wrappers alive when reloading ancestor names.
        using var root = new TreeItem(scope, null, tree, isScoped: true).Reload();
        using var parent = new TreeItem(group, root, tree).Reload();
        using var saveNode = new TreeItem(save, parent, tree).Reload();
        using var inputNode = new TreeItem(input, parent, tree).Reload();
        inspector.TreeItems.Add(root);

        try
        {
            tooltip.SetCurrentValue(ToolTip.PlacementTargetProperty, path);
            inspector.CurrentSelection = null;
            InspectorLocalization.Default.SetLanguage("en-US");
            VerifyPath("No selection", null);
            InspectorLocalization.Default.SetLanguage("zh-CN");
            VerifyPath("未选择元素", null);

            inspector.CurrentSelection = saveNode;
            VerifyPath("FixtureScope › StackPanel › SaveButton", saveNode);
            inspector.CurrentSelection = inputNode;
            VerifyPath("FixtureScope › StackPanel › TextBox", inputNode);

            group.Name = "RenamedGroup";
            parent.Reload();
            VerifyPath("FixtureScope › RenamedGroup › TextBox", inputNode);
            input.Name = "UpdatedInput";
            inputNode.Reload();
            VerifyPath("FixtureScope › RenamedGroup › UpdatedInput", inputNode);

            var longScope = new string('W', 160);
            scope.Name = longScope;
            root.Reload();
            var frame = SystemParameters.WindowNonClientFrameThickness;
            WorkbenchLayoutSmoke.Arrange(chrome, new Rect(0, 0, inspector.MinWidth - frame.Left - frame.Right,
                inspector.MinHeight - frame.Top - frame.Bottom));
            VerifyPath(longScope + " › RenamedGroup › UpdatedInput", inputNode);
            Require(previewPane.IsAncestorOf(path) && path.ActualWidth > 0 && path.ActualHeight > 0,
                "The path must remain in the preview header at minimum window size.");
            var pathTopLeft = path.TranslatePoint(new Point(0, 0), previewPane);
            var pathBottomRight = path.TranslatePoint(new Point(path.ActualWidth, path.ActualHeight), previewPane);
            var previewTop = preview.TranslatePoint(new Point(0, 0), previewPane).Y;
            Require(pathTopLeft.X >= -1 && pathBottomRight.X <= previewPane.ActualWidth + 1
                    && pathTopLeft.Y >= -1 && pathBottomRight.Y <= previewTop + 1,
                "The full selection path must be trimmed within the preview header.");

            inspector.CurrentSelection = null;
            VerifyPath("未选择元素", null);
        }
        finally
        {
            tooltip.SetCurrentValue(ToolTip.PlacementTargetProperty, savedTooltipTarget);
            inspector.CurrentSelection = savedSelection;
            inspector.TreeItems.Remove(root);
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: preview selection path tracks the scoped tree, renames and inspector selection");

        void VerifyPath(string expected, TreeItem? selection)
        {
            DrainDispatcher();
            Require(path.Text == expected && tooltipText.Text == expected,
                $"Unexpected preview path or full-path tooltip. Expected '{expected}', got '{path.Text}'.");
            Require(ReferenceEquals(inspector.CurrentSelection, selection)
                    && ReferenceEquals(preview.Target, selection?.Target)
                    && ReferenceEquals(properties.RootTarget, selection?.Target)
                    && status.Text == (selection?.DisplayName ?? expected) && preview.IsActive == previewActive,
                "The preview path, property target, preview target and status must follow the same selection.");
        }
    }

    private static T Find<T>(SnoopUI inspector, string name)
        where T : class
    {
        return inspector.FindName(name) as T
            ?? throw new InvalidOperationException("Missing preview path regression element " + name + ".");
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
