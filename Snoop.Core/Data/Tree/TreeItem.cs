// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Data.Tree;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using JetBrains.Annotations;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

public class TreeItem : INotifyPropertyChanged, IDisposable
{
    public static readonly ExportOptions DefaultExportOptions = new ExportOptions
    {
        ExportXamlStyle = true,
        IncludeDefaultEmptyValues = false,
        IncludeTypenameOnlyValues = false,
        IncludeSystemCollectionNamespaceValues = false,
        RoundDecimals = true
    };

    private bool isExpanded;
    private bool isSelected;

    private string name = string.Empty;
    private string nameLower = string.Empty;
    private readonly string typeNameLower;
    private int childItemCount;
    private readonly ObservableCollection<TreeItem> children = new();
    private ObservableCollection<MenuItem>? menuItems;

    public TreeItem(object target, TreeItem? parent, TreeService treeService)
        : this(target, parent, treeService, false)
    {
    }

    public TreeItem(object target, TreeItem? parent, TreeService treeService, bool isScoped)
    {
        this.IsScoped = isScoped;
        this.Target = target ?? throw new ArgumentNullException(nameof(target));
        this.TargetType = this.Target.GetType();

        this.typeNameLower = this.TargetType.Name.ToLower();

        this.Parent = parent;
        this.TreeService = treeService;

        this.Children = new(this.children);

        if (parent is not null)
        {
            this.Depth = parent.Depth + 1;
        }
    }

    /// <summary>
    /// The WPF object that this instance is wrapping.
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// Gets a value indicating if this is the treeitem we are currently scoped to
    /// </summary>
    public bool IsScoped { get; }

    /// <summary>
    /// Gets a value indicating if the user can scope the treeview to this item
    /// </summary>
    public virtual bool CanBeScoped { get; }

    public BindableType TargetType { get; }

    /// <summary>
    /// The parent of this instance.
    /// </summary>
    public TreeItem? Parent { get; }

    public TreeService TreeService { get; }

    /// <summary>
    /// The depth (in the visual tree) of this instance
    /// </summary>
    public int Depth { get; }

    public bool ShouldBeAnalyzed { get; protected set; } = true;

    public string Name
    {
        get => this.name;

        private set
        {
            if (this.name == value)
            {
                return;
            }

            // ReSharper disable once ConstantNullCoalescingCondition
            // ensure that name never is null
            this.name = value ?? string.Empty;
            this.nameLower = this.name.ToLower();

            this.OnPropertyChanged(nameof(this.Name));
            this.OnPropertyChanged(nameof(this.DisplayName));
        }
    }

    public virtual string DisplayName => $"{this.Name} ({this.TargetType.Name})";

    public int SortOrder { get; protected set; }

    /// <summary>
    /// The children of this instance
    /// </summary>
    public ReadOnlyObservableCollection<TreeItem> Children { get; }

    public ObservableCollection<MenuItem> MenuItems => this.menuItems ??= this.CreateMenuItems();

    protected virtual ObservableCollection<MenuItem> CreateMenuItems()
    {
        var export = CreateLocalizedMenuItem("TreeMenu.Export");
        export.SetResourceReference(MenuItem.IconProperty, "SnoopNext.Icon.ExportTree");
        export.Items.Add(this.CreateExportCommand("TreeMenu.TreeWithFilter", recurse: true, useFilter: true));
        export.Items.Add(this.CreateExportCommand("TreeMenu.TreeWithoutFilter", recurse: true, useFilter: false));
        export.Items.Add(new Separator());
        export.Items.Add(this.CreateExportCommand("TreeMenu.ElementWithFilter", recurse: false, useFilter: true));
        export.Items.Add(this.CreateExportCommand("TreeMenu.ElementWithoutFilter", recurse: false, useFilter: false));
        export.Items.Add(new Separator());

        var options = CreateLocalizedMenuItem("TreeMenu.ExportOptions");
        options.Items.Add(CreateExportOption(nameof(ExportOptions.ExportXamlStyle)));
        options.Items.Add(new Separator());
        options.Items.Add(CreateExportOption(nameof(ExportOptions.IncludeDefaultEmptyValues)));
        options.Items.Add(CreateExportOption(nameof(ExportOptions.IncludeTypenameOnlyValues)));
        options.Items.Add(CreateExportOption(nameof(ExportOptions.IncludeSystemCollectionNamespaceValues)));
        options.Items.Add(new Separator());
        options.Items.Add(CreateExportOption(nameof(ExportOptions.RoundDecimals)));
        export.Items.Add(options);

        var items = new ObservableCollection<MenuItem> { export };
        if (this.CanBeScoped || this.IsScoped)
        {
            var scope = CreateLocalizedMenuItem(this.IsScoped ? "TreeMenu.ClearScope" : "TreeMenu.Scope");
            scope.SetResourceReference(MenuItem.IconProperty, "SnoopNext.Icon.ScopeTree");
            scope.IsCheckable = true;
            scope.IsChecked = this.IsScoped;
            scope.Command = SnoopUI.ScopeToCommand;
            scope.CommandParameter = this.IsScoped ? null : this;
            items.Add(scope);
        }

        return items;
    }

    protected static MenuItem CreateLocalizedMenuItem(string key)
    {
        var item = new MenuItem();
        item.SetBinding(MenuItem.HeaderProperty, new InspectorLocExtension(key).CreateBinding());
        return item;
    }

    private MenuItem CreateExportCommand(string key, bool recurse, bool useFilter)
    {
        var item = CreateLocalizedMenuItem(key);
        item.Command = SnoopUI.ExportTreeWithFilterCommand;
        item.CommandParameter = new ExportOptions { TreeItem = this, Recurse = recurse, UseFilter = useFilter };
        return item;
    }

    private static MenuItem CreateExportOption(string property)
    {
        var key = "TreeMenu." + property;
        var item = CreateLocalizedMenuItem(key);
        item.IsCheckable = true;
        item.StaysOpenOnClick = true;
        item.SetBinding(MenuItem.ToolTipProperty, new InspectorLocExtension(key + "Tip").CreateBinding());
        item.SetBinding(MenuItem.IsCheckedProperty, new Binding(property) { Source = DefaultExportOptions, Mode = BindingMode.TwoWay });
        return item;
    }

    public bool IsSelected
    {
        get => this.isSelected;
        set
        {
            if (this.isSelected == value)
            {
                return;
            }

            this.isSelected = value;

            // Need to expand all ancestors so this will be visible in the tree.
            if (this.isSelected)
            {
                this.Parent?.ExpandTo();
            }

            this.OnPropertyChanged(nameof(this.IsSelected));
            this.OnIsSelectedChanged();
        }
    }

    /// <summary>
    /// Need this to databind to TreeView so we can expand our children.
    /// </summary>
    public bool IsExpanded
    {
        get => this.isExpanded;
        set
        {
            if (this.isExpanded == value)
            {
                return;
            }

            this.isExpanded = value;
            this.OnPropertyChanged(nameof(this.IsExpanded));
        }
    }

    public virtual Visual? MainVisual => null;

    public virtual Brush TreeBackgroundBrush => new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));

    public virtual bool HasPreviewToolTip => this.VisualBrush is not null;

    public virtual Brush? VisualBrush => null;

    /// <summary>
    /// Checks to see if any property on this element has a binding error.
    /// </summary>
    public virtual bool HasBindingError => false;

    public bool OmitChildren { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString()
    {
        var sb = new StringBuilder(4 + 1 + this.DisplayName.Length + 1 + this.childItemCount > 0 ? 3 : 0);

        // [depth] name (type) numberOfChildren
        sb.AppendFormat("[{0:D3}] {1}", this.Depth, this.DisplayName);

        if (this.childItemCount != 0)
        {
            sb.Append(' ');
            sb.Append(this.childItemCount.ToString());
        }

        return sb.ToString();
    }

    protected virtual void OnIsSelectedChanged()
    {
    }

    /// <summary>
    /// Expand this element and all elements leading to it.
    /// Used to show this element in the tree view.
    /// </summary>
    public void ExpandTo()
    {
        this.Parent?.ExpandTo();

        this.IsExpanded = true;
    }

    /// <summary>
    /// Update the view of this visual, rebuild children as necessary
    /// </summary>
    public TreeItem Reload()
    {
        this.Name = this.GetName();

        this.RemoveAllChildren();

        this.ReloadCore();

        // Reset children count prior to re-calculation
        this.childItemCount = 0;

        // calculate the number of dependency object children
        foreach (var child in this.Children)
        {
            if (child is DependencyObjectTreeItem)
            {
                this.childItemCount++;
            }

            this.childItemCount += child.childItemCount;
        }

        return this;
    }

    protected virtual string GetName()
    {
        return UIObjectNameHelper.GetName(this.Target as DependencyObject);
    }

    protected virtual void ReloadCore()
    {
    }

    public virtual TreeItem? FindNode(object? target)
    {
        if (target is null)
        {
            return null;
        }

        if (ReferenceEquals(this.Target, target))
        {
            return this;
        }

        foreach (var child in this.Children)
        {
            var node = child.FindNode(target);

            if (node is not null)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Used for tree search.
    /// </summary>
    /// <param name="value">The value to search for.</param>
    /// <returns><c>true</c> if this matches <paramref name="value"/>. Otherwise <c>false</c>.</returns>
    public bool Filter(string value)
    {
        if (this.typeNameLower.Contains(value, StringComparison.Ordinal))
        {
            return true;
        }

        if (this.nameLower.Contains(value, StringComparison.Ordinal))
        {
            return true;
        }

        if (int.TryParse(value, out var n)
            && n == this.Depth)
        {
            return true;
        }

        return false;
    }

    protected void AddChild(TreeItem item)
    {
        this.children.Add(item);
    }

    protected bool RemoveChild(TreeItem item)
    {
        item.IsSelected = false;
        var result = this.children.Remove(item);
        item.Dispose();

        return result;
    }

    protected void RemoveAllChildren()
    {
        foreach (var item in this.children.ToList())
        {
            item.IsSelected = false;
            item.Dispose();
        }

        this.children.Clear();
    }

    public void Dispose()
    {
        if (this.Parent is null)
        {
            // We clear all diagnostic items here for performance reasons if this is the root node
            this.TreeService.DiagnosticContext.DiagnosticItems.Clear();
        }

        this.TreeService.DiagnosticContext.TreeItemDisposed(this);

        foreach (var treeItem in this.Children)
        {
            treeItem.Dispose();
        }
    }

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}