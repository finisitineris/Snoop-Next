namespace Snoop.Windows;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Snoop.Data.Tree;

public sealed partial class SnoopUI
{
    public static readonly RoutedCommand TogglePreviewPickerCommand = new(
        nameof(TogglePreviewPickerCommand), typeof(SnoopUI), new InputGestureCollection { new KeyGesture(Key.F6) });

    private UIElement? previewPickerScene;
    private TreeItem? previewPickerRoot;
    private TreeItem? previewPickerCandidate;
    private IInputElement? focusBeforePreviewPicker;

    private void CanTogglePreviewPicker(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = this.previewPickerScene is not null || this.GetPreviewPickerScene() is not null;
        e.ContinueRouting = !e.CanExecute;
        e.Handled = true;
    }

    private UIElement? GetPreviewPickerScene() =>
        !this.isShuttingDown && !this.WorkbenchCommandPalette.IsOpen && this.WorkbenchPreviewPane.IsVisible
        && this.PreviewArea.IsActive && this.RootTreeItem is not null && this.CurrentSelection is not null
            ? this.PreviewArea.Zoomer.GetKeyboardPickScene() : null;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F6 && e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None
            && (this.previewPickerScene is not null || this.GetPreviewPickerScene() is not null))
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void TogglePreviewPicker(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (this.previewPickerScene is not null)
        {
            this.ClosePreviewPicker(restoreFocus: true);
            return;
        }

        if (this.GetPreviewPickerScene() is not { } scene || this.RootTreeItem is not { } root || this.CurrentSelection is not { } selected)
        {
            return;
        }

        // Limit navigation to the projected scene, even when the inspection root is an Application.
        for (var ancestor = selected; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor.Target, scene))
            {
                // Automation exposes an owner row beside the child peers, not above them.
                root = ancestor.OmitChildren && ancestor.Parent is AutomationPeerTreeItem peer ? peer : ancestor;
                break;
            }

            if (ancestor is AutomationPeerTreeItem automation && ReferenceEquals(automation.Visual, scene))
            {
                root = ancestor;
                break;
            }
        }

        TreeItem? candidate = selected;
        if (!this.CanPickPreviewItem(selected, scene))
        {
            candidate = this.CanPickPreviewItem(root, scene) ? root : this.FindAdjacentPreviewItem(root, root, scene, true);
        }

        if (candidate is null)
        {
            return;
        }

        this.previewPickerScene = scene;
        this.previewPickerRoot = root;
        this.focusBeforePreviewPicker = this.IsKeyboardFocusWithin ? Keyboard.FocusedElement : null;
        this.WorkbenchPreviewHeader.Visibility = Visibility.Collapsed;
        this.WorkbenchPreviewPicker.Visibility = Visibility.Visible;
        this.ShowPreviewPickerCandidate(candidate);
        if (!this.WorkbenchPreviewPickerCandidate.Focus())
        {
            this.ClosePreviewPicker(restoreFocus: true);
        }
    }

    private bool CanPickPreviewItem(TreeItem item, UIElement scene)
    {
        if (item.Target is not UIElement element || !this.PreviewArea.Zoomer.CanPickWithKeyboard(element, scene))
        {
            return false;
        }

        // Match pointer picking when the inspector is configured to select a template's owner.
        return !this.SkipTemplateParts || element is not FrameworkElement frameworkElement
            || GetItemToFindAndSkipTemplateParts(frameworkElement) is not { } owner
            || ReferenceEquals(owner, element)
            || (!ReferenceEquals(owner, scene) && !owner.IsDescendantOf(scene));
    }

    private TreeItem? FindAdjacentPreviewItem(TreeItem item, TreeItem root, UIElement scene, bool forward)
    {
        // Resume the existing branch in order. Each ancestor needs one sibling lookup;
        // large runs of clipped or hidden siblings are then visited by index.
        var branch = new Stack<KeyValuePair<TreeItem, int>>();
        var step = forward ? 1 : -1;
        if (forward)
        {
            branch.Push(new(item, 0));
        }

        while (true)
        {
            if (branch.Count == 0)
            {
                if (item == root || item.Parent is not { } parent)
                {
                    return null;
                }

                var index = parent.Children.IndexOf(item);
                if (index < 0)
                {
                    return null;
                }

                branch.Push(new(parent, index + step));
                item = parent;
            }

            var position = branch.Pop();
            var node = position.Key;
            var childIndex = position.Value;
            if (childIndex >= 0 && childIndex < node.Children.Count)
            {
                var child = node.Children[childIndex];
                if (forward && this.CanPickPreviewItem(child, scene))
                {
                    return child;
                }

                branch.Push(new(node, childIndex + step));
                branch.Push(new(child, forward ? 0 : child.Children.Count - 1));
            }
            else if (!forward && this.CanPickPreviewItem(node, scene))
            {
                return node;
            }
        }
    }

    private void ShowPreviewPickerCandidate(TreeItem candidate)
    {
        this.previewPickerCandidate = candidate;
        this.WorkbenchPreviewPickerCandidate.DataContext = candidate;
        this.PreviewArea.Zoomer.ShowKeyboardPickCandidate((UIElement)candidate.Target);
    }

    private void HandlePreviewPickerKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                if (this.previewPickerCandidate is { } candidate && this.previewPickerRoot is { } root
                    && this.previewPickerScene is { } scene && ReferenceEquals(scene, this.PreviewArea.Zoomer.GetKeyboardPickScene()))
                {
                    if (this.FindAdjacentPreviewItem(candidate, root, scene, e.Key == Key.Down) is { } next)
                    {
                        this.ShowPreviewPickerCandidate(next);
                    }
                }
                else
                {
                    this.ClosePreviewPicker(restoreFocus: true);
                }

                break;
            case Key.Enter:
                e.Handled = true;
                this.CommitPreviewPicker(sender, e);
                break;
            case Key.Escape:
            case Key.F6:
                e.Handled = true;
                this.ClosePreviewPicker(restoreFocus: true);
                break;
        }
    }

    private void CommitPreviewPicker(object sender, RoutedEventArgs e)
    {
        var candidate = this.previewPickerCandidate;
        var scene = this.previewPickerScene;
        if (candidate is null || scene is null || !ReferenceEquals(scene, this.GetPreviewPickerScene())
            || !this.CanPickPreviewItem(candidate, scene))
        {
            this.ClosePreviewPicker(restoreFocus: true);
            return;
        }

        this.ClosePreviewPicker(restoreFocus: true);
        this.HandlePreviewElementPicked((UIElement)candidate.Target, scene);
    }

    private void HandlePreviewPickerEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            this.ClosePreviewPicker(restoreFocus: true);
        }
    }

    private void HandlePreviewPickerLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => this.ClosePreviewPicker(restoreFocus: false);

    private void ClosePreviewPicker(bool restoreFocus)
    {
        if (this.previewPickerScene is null)
        {
            return;
        }

        var previous = this.focusBeforePreviewPicker;
        this.previewPickerScene = null;
        this.previewPickerRoot = null;
        this.previewPickerCandidate = null;
        this.focusBeforePreviewPicker = null;
        this.WorkbenchPreviewPickerCandidate.DataContext = null;
        this.WorkbenchPreviewHeader.Visibility = Visibility.Visible;
        this.WorkbenchPreviewPicker.Visibility = Visibility.Collapsed;
        this.PreviewArea.Zoomer.ShowKeyboardPickCandidate(null);
        if (restoreFocus && previous is not null)
        {
            Keyboard.Focus(previous);
        }
    }
}
