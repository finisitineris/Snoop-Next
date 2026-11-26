namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Windows;

internal static class WorkbenchTreeModeSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        WorkbenchAutomationClientSmoke.VerifyTreeModeSelector(inspector);
        var combo = (ComboBox)inspector.FindName("TreeTypeComboBox");
        var tree = (TreeView)inspector.FindName("Tree");
        var properties = (PropertyInspector)inspector.FindName("PropertyInspector");
        var chrome = (FrameworkElement)inspector.FindName("WorkbenchChrome");

        // The template deliberately has no ContentPresenter. The logical child and
        // the rendered decoration belong to the same button in different trees.
        var logicalContent = new TextBlock { Name = "ActionContent", Text = "Apply" };
        var decoration = new FrameworkElementFactory(typeof(Border), "ActionChrome");
        decoration.SetValue(Border.BackgroundProperty, Brushes.SteelBlue);
        var caption = new FrameworkElementFactory(typeof(TextBlock));
        caption.SetValue(TextBlock.TextProperty, "Apply");
        decoration.AppendChild(caption);
        var button = new Button
        {
            Name = "ApplyAction",
            Content = logicalContent,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = decoration }
        };
        var panel = new StackPanel { Name = "ActionPanel" };
        var divider = new Border { Name = "ActionDivider", Height = 2, Background = Brushes.Gray };
        panel.Children.Add(button);
        panel.Children.Add(divider);
        var scope = new GroupBox { Name = "ActionScope", Header = "Actions", Content = panel };
        var outside = new Button { Name = "OutsideScope", Content = "Outside" };
        var scene = new StackPanel();
        scene.Children.Add(scope);
        scene.Children.Add(outside);
        using var source = new HwndSource(new HwndSourceParameters("Snoop tree mode fixture")
        {
            Width = 320, Height = 160, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        source.RootVisual = scene;
        WorkbenchLayoutSmoke.Arrange(scene, new Rect(0, 0, 320, 160));
        var visualPart = button.Template.FindName("ActionChrome", button) as Border
            ?? throw new InvalidOperationException("The target Button did not realize its template.");

        inspector.ScopeTo(scope);
        SnoopUI.RefreshCommand.Execute(null, inspector);
        Settle();
        var visual = Root(TreeType.Visual, scope);
        var visualButton = Node(visual, button);
        Require(ReferenceEquals(Node(visual, visualPart).Parent, visualButton) && visual.FindNode(logicalContent) is null,
            "The Visual tree must show the button template and omit its unpresented content.");
        Select(visualButton);
        ShowChildren(visualButton, visualPart, "visual");

        var logical = Switch(TreeType.Logical, scope);
        var logicalButton = Node(logical, button);
        Require(ReferenceEquals(Node(logical, logicalContent).Parent, logicalButton) && logical.FindNode(visualPart) is null,
            "The Logical tree must show the button's content and omit the template decoration.");
        VerifySelection(logicalButton);
        ShowChildren(logicalButton, logicalContent, "logical");

        var automation = Switch(TreeType.Automation, scope);
        var buttonPeer = UIElementAutomationPeer.FromElement(button) as ButtonAutomationPeer
            ?? throw new InvalidOperationException("The Automation tree must create the target button's WPF peer.");
        var peerNode = Node(automation, buttonPeer);
        var ownerNode = Node(automation, button);
        Require(ReferenceEquals(ownerNode.Parent, peerNode) && automation.FindNode(visualPart) is null
                && automation.FindNode(logicalContent) is null,
            "The Automation tree must expose the button peer with its inspectable owner.");
        VerifySelection(ownerNode);
        ShowChildren(peerNode, button, "automation");

        Select(peerNode);
        visual = Switch(TreeType.Visual, scope);
        VerifySelection(Node(visual, button));
        Select(Node(visual, visualPart));
        logical = Switch(TreeType.Logical, scope);
        VerifySelection(Node(logical, button));
        Select(Node(logical, logicalContent));
        visual = Switch(TreeType.Visual, scope);
        VerifySelection(Node(visual, button));

        // A layout panel has no automation peer. It must still bound the peer walk.
        inspector.ScopeTo(panel);
        SnoopUI.RefreshCommand.Execute(null, inspector);
        Settle();
        Select(Node(Root(TreeType.Visual, panel), button));
        automation = Switch(TreeType.Automation, panel);
        Require(automation.FindNode(buttonPeer) is not null && automation.FindNode(scope) is null,
            "A scoped layout panel must expose descendant peers and exclude its enclosing group.");
        VerifySelection(Node(automation, button));
        visual = Switch(TreeType.Visual, panel);
        Select(Node(visual, divider));
        automation = Switch(TreeType.Automation, panel);
        Require(automation.FindNode(divider) is null, "The decorative divider must have no Automation row.");
        VerifySelection(automation);
        Console.WriteLine("PASS: switching Visual, Logical and Automation trees preserves the selected WPF row, property target and scope, including ancestor and peer-owner recovery.");

        TreeItem Root(TreeType mode, FrameworkElement expectedScope)
        {
            var root = inspector.RootTreeItem ?? throw new InvalidOperationException("Missing " + mode + " root.");
            Require(inspector.CurrentTreeType == mode && root.TreeService.TreeType == mode
                    && tree.Items.Contains(root) && root.IsScoped && ReferenceEquals(inspector.ScopedRootObject, expectedScope)
                    && ReferenceEquals(inspector.ScopedPreviewTarget, expectedScope) && root.FindNode(outside) is null,
                $"The displayed {mode} tree and preview must stay scoped to {expectedScope.Name}.");
            return root;
        }

        TreeItem Switch(TreeType mode, FrameworkElement expectedScope)
        {
            combo.SetCurrentValue(Selector.SelectedValueProperty, mode);
            Settle();
            return Root(mode, expectedScope);
        }

        void ShowChildren(TreeItem parent, object expectedChild, string capture)
        {
            parent.ExpandTo();
            Settle();
            Require(Row(Node(parent, expectedChild)) is { IsVisible: true },
                $"The {capture} tree did not render the expected child below {parent.DisplayName}.");
            LauncherAppearanceSmoke.CaptureIfRequested(chrome, "tree-mode-" + capture);
        }

        void Select(TreeItem node)
        {
            node.Parent?.ExpandTo();
            Settle();
            var row = Row(node) ?? throw new InvalidOperationException("Missing selectable tree row " + node.DisplayName);
            row.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
            Settle();
            VerifySelection(node);
        }

        void VerifySelection(TreeItem node)
        {
            Require(ReferenceEquals(inspector.CurrentSelection, node) && node.IsSelected && ReferenceEquals(tree.SelectedItem, node)
                    && Row(node) is { IsSelected: true, IsVisible: true } && ReferenceEquals(properties.RootTarget, node.Target),
                $"Expected selected {inspector.CurrentTreeType} row and properties for {node.DisplayName}, "
                + $"got model '{inspector.CurrentSelection}', tree '{tree.SelectedItem}', properties '{properties.RootTarget}'.");
        }

        TreeViewItem? Row(TreeItem node)
        {
            ItemsControl? parent = node.Parent is null ? tree : Row(node.Parent);
            return parent?.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
        }

        void Settle()
        {
            inspector.UpdateLayout();
            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }
    }

    private static TreeItem Node(TreeItem root, object target)
        => root.FindNode(target) ?? throw new InvalidOperationException($"Missing {target} below {root.DisplayName}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
