// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Windows;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using JetBrains.Annotations;
using Snoop.Core;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Infrastructure.Helpers;
using Snoop.Localization;
using Snoop.Views;

public sealed partial class SnoopUI : INotifyPropertyChanged
{
    #region Public Static Routed Commands

    public static readonly RoutedCommand IntrospectCommand = new(nameof(IntrospectCommand), typeof(SnoopUI));
    public static readonly RoutedCommand RefreshCommand = new(nameof(RefreshCommand), typeof(SnoopUI));
    public static readonly RoutedCommand ExportTreeWithFilterCommand = new(nameof(ExportTreeWithFilterCommand), typeof(SnoopUI));
    public static readonly RoutedCommand HelpCommand = new(nameof(HelpCommand), typeof(SnoopUI));
    public static readonly RoutedCommand InspectCommand = new(nameof(InspectCommand), typeof(SnoopUI));
    public static readonly RoutedCommand LocateDiagnosticElementCommand = new(nameof(LocateDiagnosticElementCommand), typeof(SnoopUI));
    public static readonly RoutedCommand SelectFocusCommand = new(nameof(SelectFocusCommand), typeof(SnoopUI));
    public static readonly RoutedCommand SelectFocusScopeCommand = new(nameof(SelectFocusScopeCommand), typeof(SnoopUI));
    public static readonly RoutedCommand ClearSearchFilterCommand = new(nameof(ClearSearchFilterCommand), typeof(SnoopUI));
    public static readonly RoutedCommand CopyPropertyChangesCommand = new(nameof(CopyPropertyChangesCommand), typeof(SnoopUI));
    public static readonly RoutedCommand ScopeToCommand = new(nameof(ScopeToCommand), typeof(SnoopUI));

    #endregion

    static SnoopUI()
    {
        IntrospectCommand.InputGestures.Add(new KeyGesture(Key.I, ModifierKeys.Control));
        RefreshCommand.InputGestures.Add(new KeyGesture(Key.F5));
        HelpCommand.InputGestures.Add(new KeyGesture(Key.F1));
        ClearSearchFilterCommand.InputGestures.Add(new KeyGesture(Key.Escape));
        CopyPropertyChangesCommand.InputGestures.Add(new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
    }

    public SnoopUI()
    {
        this.TreeService = TreeService.From(this.CurrentTreeType);

        this.filterCall = new DelayedCall(this.ProcessFilter, DispatcherPriority.Background);

        this.InitializeComponent();
        this.RestoreWorkbenchLayout();
        this.WorkbenchGrid.LayoutUpdated += this.HandleWorkbenchLayoutUpdated;
        this.WorkbenchCenterGrid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                this.UpdateWorkbenchColumnLimits();
            }
        };

        this.CommandBindings.Add(new(ApplicationCommands.Close, (_, _) => this.Close()));

        this.CommandBindings.Add(new CommandBinding(IntrospectCommand, this.HandleIntrospection));
        this.CommandBindings.Add(new CommandBinding(RefreshCommand, this.HandleRefresh));
        this.CommandBindings.Add(new CommandBinding(ExportTreeWithFilterCommand, this.HandleExport));
        this.CommandBindings.Add(new CommandBinding(ScopeToCommand, this.HandleScopeTo));
        this.CommandBindings.Add(new CommandBinding(HelpCommand, this.HandleHelp));

        this.CommandBindings.Add(new CommandBinding(InspectCommand, this.HandleInspect));
        this.CommandBindings.Add(new CommandBinding(
            LocateDiagnosticElementCommand,
            this.HandleLocateDiagnosticElement,
            (_, e) => e.CanExecute = this.GetDiagnosticTreeItem(e.Parameter) is not null));

        this.CommandBindings.Add(new CommandBinding(SelectFocusCommand, this.HandleSelectFocus));
        this.CommandBindings.Add(new CommandBinding(SelectFocusScopeCommand, this.HandleSelectFocusScope));

        this.CommandBindings.Add(new CommandBinding(ClearSearchFilterCommand, this.ClearSearchFilterHandler));
        this.CommandBindings.Add(new CommandBinding(OpenCommandPaletteCommand, this.HandleOpenCommandPalette));

        this.CommandBindings.Add(new CommandBinding(CopyPropertyChangesCommand, this.CopyPropertyChangesHandler));

        this.WorkbenchCommandQuery.TextChanged += (_, _) => this.FilterWorkbenchCommands();
        InputManager.Current.PreProcessInput += this.HandlePreProcessInputForTracking;
        this.Tree.SelectedItemChanged += this.HandleTreeSelectedItemChanged;
        this.PreviewArea.ElementPicked += this.HandlePreviewElementPicked;
        this.PropertyInspector.PropertyGrid.EditReplayed += this.HandleEditReplayed;

        this.filterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(0.3)
        };
        this.filterTimer.Tick += (_, _) =>
        {
            this.EnqueueAfterSettingFilter();
            this.filterTimer.Stop();
        };
    }

    #region Public Properties

    public static Version? AssemblyVersion { get; } = typeof(SnoopUI).Assembly.GetName().Version;

    #region Tree

    public TreeService TreeService
    {
        get => this.treeService;
        private set
        {
            if (Equals(value, this.treeService))
            {
                return;
            }

            this.treeService = value;
            this.OnPropertyChanged();
        }
    }

    /// <summary>
    /// This is the collection the TreeView binds to.
    /// </summary>
    public ObservableCollection<TreeItem> TreeItems { get; } = new();

    #endregion

    #region RootTreeItem

    public SystemResourcesTreeItem? SystemResourcesTreeItem { get; private set; }

    /// <summary>
    /// Root element of the tree.
    /// </summary>
    public TreeItem? RootTreeItem
    {
        get => this.rootTreeItem;

        private set
        {
            this.rootTreeItem = value;
            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(this.InspectedProcess));
            this.OnPropertyChanged(nameof(this.ScopedPreviewTarget));
        }
    }

    /// <summary>
    /// Identifies the process that owns the inspected tree.
    /// </summary>
    public string? InspectedProcess => this.RootTreeItem is null ? null : inspectedProcessIdentity.Value;

    public object? ScopedPreviewTarget => this.RootTreeItem is { IsScoped: true } root ? root.Target : null;

    /// <summary>
    /// Root element of the tree.
    /// </summary>
    private TreeItem? rootTreeItem;

    #endregion

    #region CurrentSelection

    public override object? Target
    {
        get => this.CurrentSelection?.Target;
        set => this.CurrentSelection = this.FindItem(value);
    }

    /// <summary>
    /// Currently selected item in the tree view.
    /// </summary>
    public TreeItem? CurrentSelection
    {
        get => this.currentSelection;

        set
        {
            if (this.currentSelection == value)
            {
                return;
            }

            if (this.currentSelection is not null)
            {
                this.ObservePreviewPathNames(false);
                this.SaveEditedProperties(this.currentSelection);
                this.currentSelection.IsSelected = false;
            }

            this.currentSelection = value;

            if (this.CurrentSelection is not null)
            {
                this.CurrentSelection.IsSelected = true;
                this.ObservePreviewPathNames(true);
            }

            if (this.isShuttingDown)
            {
                return;
            }

            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(this.PreviewSelectionPath));

            if (this.currentSelection is null)
            {
                return;
            }

            if (this.TreeItems.Count > 1
                || (this.TreeItems.Count == 1 && this.TreeItems[0] != this.RootTreeItem)
                || (this.TreeItems.Count == 2 && this.TreeItems[1] != this.RootTreeItem))
            {
                // Check whether the selected item is filtered out by the filter,
                // in which case reset the filter.
                var tmp = this.CurrentSelection;

                while (tmp is not null && !this.TreeItems.Contains(tmp))
                {
                    tmp = tmp.Parent;
                }

                if (tmp is null)
                {
                    // The selected item is not a descendant of any root.
                    RefreshCommand.Execute(null, this);
                }
            }
        }
    }

    private TreeItem? currentSelection;

    public string? PreviewSelectionPath
    {
        get
        {
            if (this.currentSelection is null)
            {
                return null;
            }

            var segments = new Stack<string>();
            for (var item = this.currentSelection; item is not null; item = item.Parent)
            {
                segments.Push(string.IsNullOrWhiteSpace(item.Name) ? item.TargetType.Name : item.Name);
            }

            return string.Join(" › ", segments);
        }
    }

    private void ObservePreviewPathNames(bool observe)
    {
        for (var item = this.currentSelection; item is not null; item = item.Parent)
        {
            if (observe)
            {
                PropertyChangedEventManager.AddHandler(item, this.HandlePreviewPathNameChanged, nameof(TreeItem.Name));
            }
            else
            {
                PropertyChangedEventManager.RemoveHandler(item, this.HandlePreviewPathNameChanged, nameof(TreeItem.Name));
            }
        }
    }

    private void HandlePreviewPathNameChanged(object? sender, PropertyChangedEventArgs e)
        => this.OnPropertyChanged(nameof(this.PreviewSelectionPath));

    #endregion

    #region Filter

    /// <summary>
    /// This Filter property is bound to the editable combo box that the user can type in to filter the visual tree TreeView.
    /// Every time the user types a key, the setter gets called, enqueueing a delayed call to the ProcessFilter method.
    /// </summary>
    public string Filter
    {
        get { return this.filter; }

        set
        {
            this.filter = value;

            if (this.fromTextBox is false)
            {
                this.EnqueueAfterSettingFilter();
            }
            else
            {
                this.filterTimer.Stop();
                this.filterTimer.Start();
            }

            this.OnPropertyChanged();
        }
    }

    private void SetFilter(string value)
    {
        this.fromTextBox = false;
        this.Filter = value;
        this.fromTextBox = true;
    }

    private void EnqueueAfterSettingFilter()
    {
        this.filterCall.Enqueue(this.Dispatcher);

        this.OnPropertyChanged(nameof(this.Filter));
    }

    public bool IsTreeFilterEmpty { get; private set; }

    internal bool IsTreeFilterApplied { get; private set; }

    private bool isUpdatingTreeFilter;
    private string filter = string.Empty;

    #endregion

    #region Focus

    public IInputElement? CurrentFocus
    {
        get
        {
            var newFocus = Keyboard.FocusedElement;

            if (newFocus == this.currentFocus
                || (newFocus is null && this.IsActive))
            {
                return this.currentFocus;
            }

            if (SnoopPartsRegistry.IsSnoopingSnoop == false
                && newFocus is DependencyObject dpo
                && dpo.IsPartOfSnoopVisualTree())
            {
                return this.previousFocus;
            }

            this.currentFocus = newFocus;

            var result = this.returnPreviousFocus ? this.previousFocus : this.currentFocus;

            // Store reference to previously focused element only if focused element was changed.
            this.previousFocus = this.currentFocus;

            this.OnPropertyChanged(nameof(this.CurrentFocusScope));

            return result;
        }
    }

    public object? CurrentFocusScope
    {
        get
        {
            if (this.currentFocus is DependencyObject selectedItem)
            {
                return FocusManager.GetFocusScope(selectedItem);
            }

            return null;
        }
    }

    #endregion

    public bool TrackOnMouseMove { get; set; } = true;

    public bool TrackOnClick { get; set; } = false;

    public bool IgnoreHitTestVisibility
    {
        get => this.ignoreHitTestVisibility;
        set
        {
            this.ignoreHitTestVisibility = value;
            this.PreviewArea?.SetIgnoreHitTestVisibility(value);
        }
    }

    public bool SkipTemplateParts { get; set; } = false;

    /// <summary>Identifies the <see cref="CurrentTreeType"/> dependency property.</summary>
    public static readonly DependencyProperty CurrentTreeTypeProperty = DependencyProperty.Register(nameof(CurrentTreeType), typeof(TreeType), typeof(SnoopUI), new PropertyMetadata(TreeType.Visual, OnCurrentTreeTypeChanged));

    private static void OnCurrentTreeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SnoopUI)d;

        control.TreeService?.Dispose();
        control.TreeService = TreeService.From((TreeType)e.NewValue);
        control.Refresh();
    }

    public TreeType CurrentTreeType
    {
        get { return (TreeType)this.GetValue(CurrentTreeTypeProperty); }
        set { this.SetValue(CurrentTreeTypeProperty, value); }
    }

    #endregion

    #region Public Methods

    public void ApplyReduceDepthFilter(TreeItem? newRoot) => this.ApplyReduceDepthFilter(newRoot, null);

    internal void ApplyReduceDepthFilter(TreeItem? newRoot, TreeItem? displayedRoot)
    {
        this.depthAdjustment?.Abort();
        this.depthAdjustment = null;
        if (newRoot is null)
        {
            return;
        }

        this.depthAdjustment = this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            this.depthAdjustment = null;
            this.UpdateDisplayedTreeItems(() =>
            {
                if (displayedRoot is null)
                {
                    this.TreeItems.Clear();
                    this.TreeItems.Add(newRoot);
                }
                else
                {
                    var index = this.TreeItems.IndexOf(displayedRoot);
                    if (index >= 0)
                    {
                        this.TreeItems[index] = newRoot;
                    }
                }
            });
        }));
    }

    public bool IsReduceInProgress => this.depthAdjustment is not null;

    /// <summary>
    /// Loop through the properties in the current PropertyGrid and save away any properties
    /// that have been changed by the user.
    /// </summary>
    /// <param name="owningObject">currently selected object that owns the properties in the grid (before changing selection to the new object)</param>
    private void SaveEditedProperties(TreeItem owningObject)
    {
        foreach (var property in this.PropertyInspector.PropertyGrid.Properties)
        {
            if (property.IsValueChangedByUser)
            {
                EditedPropertiesHelper.AddEditedProperty(this.Dispatcher, owningObject, property);
            }
        }
    }

    #endregion

    #region Protected Event Overrides

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        CacheManager.Instance.IncreaseUsageCount();

        // load whether all properties are shown by default
        this.PropertyInspector.ShowDefaults = Settings.Default.ShowDefaults;

        // load whether the previewer is shown by default
        this.PreviewArea.IsActive = Settings.Default.ShowPreviewer;

        // load the window placement details from the user settings.
        SnoopWindowUtils.LoadWindowPlacement(this, Settings.Default.SnoopUIWindowPlacement);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // persist the window placement details to the user settings.
        SnoopWindowUtils.SaveWindowPlacement(this, wp => Settings.Default.SnoopUIWindowPlacement = wp);

        base.OnClosing(e);

        if (!e.Cancel)
        {
            this.CaptureWorkbenchLayout();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        this.WorkbenchCommandPalette.IsOpen = false;
        this.isShuttingDown = true;
        this.depthAdjustment?.Abort();
        this.depthAdjustment = null;
        this.CurrentSelection = null;

        this.TreeService?.Dispose();

        this.eventsView?.Dispose();
        this.debugListenerControl?.Dispose();

        CacheManager.Instance.DecreaseUsageCount();

        this.WorkbenchGrid.LayoutUpdated -= this.HandleWorkbenchLayoutUpdated;
        this.PreviewArea.ElementPicked -= this.HandlePreviewElementPicked;
        this.PropertyInspector.PropertyGrid.EditReplayed -= this.HandleEditReplayed;
        InputManager.Current.PreProcessInput -= this.HandlePreProcessInputForTracking;

        this.filterTimer.Stop();

        // persist whether all properties are shown by default
        Settings.Default.ShowDefaults = this.PropertyInspector.ShowDefaults;

        // persist whether the previewer is shown by default
        Settings.Default.ShowPreviewer = this.PreviewArea?.IsActive == true;

        // actually do the persisting
        Settings.Default.Save();

        base.OnClosed(e);
    }

    #endregion

    #region Private Routed Event Handlers

    /// <summary>
    /// Just for fun, the ability to run Snoop on itself :)
    /// </summary>
    private void HandleIntrospection(object sender, ExecutedRoutedEventArgs e)
    {
        this.Load(this);
    }

    private void HandleRefresh(object sender, ExecutedRoutedEventArgs e)
    {
        this.Refresh();
    }

    private void Refresh()
    {
        var saveCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var previousSelection = this.CurrentSelection;
            var previousTarget = previousSelection?.Target;

            SystemResourcesCache.Instance.Reload();

            this.TreeItems.Clear();

            this.SystemResourcesTreeItem?.Dispose();
            this.SystemResourcesTreeItem = (SystemResourcesTreeItem)new SystemResourcesTreeItem(null, this.TreeService).Reload();

            this.RootTreeItem?.Dispose();
            this.RootTreeItem = this.TreeService.Construct(this.ScopedRootObject ?? this.RootObject!, null, isScoped: this.ScopedRootObject is not null);

            this.TreeService.DiagnosticContext.AnalyzeTree();

            if (previousTarget is not null)
            {
                var treeItem = this.FindItem(previousTarget);
                if (treeItem is not null)
                {
                    this.CurrentSelection = treeItem;
                    this.PropertyInspector.PropertyGrid.RefreshPropertyGrid();

                    if (previousSelection?.IsExpanded == true)
                    {
                        this.CurrentSelection.ExpandTo();
                    }
                }
                else
                {
                    this.CurrentSelection = null;
                }
            }

            this.SetFilter(this.filter);
        }
        finally
        {
            Mouse.OverrideCursor = saveCursor;
        }
    }

    private void HandleExport(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not ExportOptions options)
        {
            return;
        }

        Cursor saveCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var propertyFilter = options.UseFilter ? this.PropertyInspector.PropertyFilter : null;

            options.ExportXamlStyle = TreeItem.DefaultExportOptions.ExportXamlStyle;
            options.IncludeDefaultEmptyValues = TreeItem.DefaultExportOptions.IncludeDefaultEmptyValues;
            options.IncludeTypenameOnlyValues = TreeItem.DefaultExportOptions.IncludeTypenameOnlyValues;
            options.IncludeSystemCollectionNamespaceValues = TreeItem.DefaultExportOptions.IncludeSystemCollectionNamespaceValues;
            options.RoundDecimals = TreeItem.DefaultExportOptions.RoundDecimals;

            var treeItem = options.TreeItem ?? this.CurrentSelection;
            var localization = InspectorLocalization.Default;

            if (treeItem is null)
            {
                MessageBox.Show(localization["Workbench.ExportMissingTreeMessage"], localization["Workbench.ExportMissingTreeTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var filePath = ExportHelper.GetUniqueExportFilePath("TreeExport", "xml");

            using (var streamWriter = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                TreeExporter.Export(treeItem, streamWriter, propertyFilter, options);
            }

            MessageBox.Show(
                string.Format(CultureInfo.InvariantCulture, localization["Diagnostics.ExportCompletedMessage"], filePath),
                localization["Diagnostics.ExportCompletedTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            Mouse.OverrideCursor = saveCursor;
        }
    }

    private void HandleScopeTo(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not TreeItem treeItem)
        {
            this.Unscope();
        }
        else
        {
            this.ScopeTo(treeItem.Target);
            this.CurrentSelection = treeItem;
        }

        this.Refresh();
    }

    private void HandleHelp(object sender, ExecutedRoutedEventArgs e)
    {
        //Help help = new Help();
        //help.Show();
    }

    private void HandleInspect(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is Visual visual)
        {
            var node = this.FindItem(visual);
            if (node is not null)
            {
                this.CurrentSelection = node;
            }
        }
        else if (e.Parameter is not null)
        {
            this.PropertyInspector.SetTarget(e.Parameter);
        }
    }

    private TreeItem? GetDiagnosticTreeItem(object? parameter)
    {
        if (this.isShuttingDown
            || parameter is not DiagnosticItem { TreeItem: { } item } diagnostic
            || !ReferenceEquals(item.TreeService, this.TreeService)
            || !this.TreeService.DiagnosticContext.DiagnosticItems.Contains(diagnostic))
        {
            return null;
        }

        var ancestor = item;
        while (ancestor.Parent is { } parent)
        {
            if (!parent.Children.Contains(ancestor))
            {
                return null;
            }

            ancestor = parent;
        }

        return ReferenceEquals(ancestor, this.RootTreeItem) ? item : null;
    }

    private void HandlePreviewElementPicked(Visual visual, UIElement scene)
    {
        var request = ++this.previewPickRequest;
        if (this.RootTreeItem is not { } root
            || this.GetPreviewTreeItem(root, visual, scene) is not { } item)
        {
            return;
        }

        if (this.filterTimer.IsEnabled)
        {
            this.filterTimer.Stop();
            this.ProcessFilter();
        }

        if (this.IsTreeItemDisplayed(item))
        {
            this.SelectPreviewTreeItem(item);
            return;
        }

        // A preview pick can reveal an element hidden by the tree filter without rebuilding the inspected tree.
        this.ClearTreeFilter();
        var selection = this.CurrentSelection;
        this.RunInDispatcherAsync(() =>
        {
            if (request == this.previewPickRequest
                && ReferenceEquals(this.CurrentSelection, selection)
                && !this.filterTimer.IsEnabled
                && this.filter.Length == 0
                && this.GetPreviewTreeItem(root, visual, scene) is { } current
                && this.IsTreeItemDisplayed(current))
            {
                this.SelectPreviewTreeItem(current);
            }
        }, DispatcherPriority.Background);
    }

    private TreeItem? GetPreviewTreeItem(TreeItem root, Visual visual, UIElement scene)
    {
        if (this.isShuttingDown
            || !this.PreviewArea.IsActive
            || !ReferenceEquals(root, this.RootTreeItem)
            || (this.ScopedPreviewTarget is { } scope && !ReferenceEquals(scene, scope))
            || PresentationSource.FromVisual(scene)?.CompositionTarget is null
            || (!ReferenceEquals(visual, scene) && !visual.IsDescendantOf(scene)))
        {
            return null;
        }

        for (DependencyObject? candidate = visual; candidate is Visual or Visual3D; candidate = VisualTreeHelper.GetParent(candidate))
        {
            if (this.SkipTemplateParts
                && candidate is FrameworkElement element
                && GetItemToFindAndSkipTemplateParts(element) is { } templateOwner
                && (ReferenceEquals(templateOwner, scene) || templateOwner.IsDescendantOf(scene))
                && root.FindNode(templateOwner) is { } owner)
            {
                return owner;
            }

            if (root.FindNode(candidate) is { } item)
            {
                return item;
            }

            if (ReferenceEquals(candidate, scene))
            {
                break;
            }
        }

        return null;
    }

    private void SelectPreviewTreeItem(TreeItem item)
    {
        item.Parent?.ExpandTo();
        this.CurrentSelection = item;
        item.IsSelected = true;
    }

    private void HandleLocateDiagnosticElement(object sender, ExecutedRoutedEventArgs e)
    {
        var diagnostic = e.Parameter;
        if (this.GetDiagnosticTreeItem(diagnostic) is not null)
        {
            // A stopped debounce timer may already have queued the filter.
            this.RunInDispatcherAsync(() => this.LocateDiagnosticElement(diagnostic), DispatcherPriority.Background);
        }
    }

    private void LocateDiagnosticElement(object? diagnostic)
    {
        if (this.filterTimer.IsEnabled)
        {
            this.filterTimer.Stop();
            this.ProcessFilter();
        }

        if (this.GetDiagnosticTreeItem(diagnostic) is not { } item)
        {
            return;
        }

        if (this.IsTreeItemDisplayed(item))
        {
            this.RevealDiagnosticTreeItem(item);
            return;
        }

        // Rebuild only the displayed roots. Refresh would replace the inspected tree.
        this.ClearTreeFilter();
        this.RunInDispatcherAsync(() =>
        {
            if (this.GetDiagnosticTreeItem(diagnostic) is { } current && this.IsTreeItemDisplayed(current))
            {
                this.RevealDiagnosticTreeItem(current);
            }
        }, DispatcherPriority.Background);
    }

    private bool IsTreeItemDisplayed(TreeItem item)
    {
        for (TreeItem? ancestor = item; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (this.TreeItems.Contains(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void RevealDiagnosticTreeItem(TreeItem item)
    {
        this.ShowWorkbenchPane(this.ShowTreePaneItem);
        item.Parent?.ExpandTo();
        this.CurrentSelection = item;
        // Filtering can reset the tree container without changing CurrentSelection.
        item.IsSelected = true;
        this.Tree.Focus();
    }

    private void HandleSelectFocus(object sender, ExecutedRoutedEventArgs e)
    {
        // We know we've stolen focus here. Let's use previously focused element.
        this.returnPreviousFocus = true;
        this.SelectItem(this.CurrentFocus as DependencyObject);
        this.returnPreviousFocus = false;
        this.OnPropertyChanged(nameof(this.CurrentFocus));
    }

    private void HandleSelectFocusScope(object sender, ExecutedRoutedEventArgs e)
    {
        this.SelectItem(e.Parameter as DependencyObject);
    }

    private void ClearSearchFilterHandler(object sender, ExecutedRoutedEventArgs e)
    {
        if (this.PropertyInspector.PropertiesFilter.IsFocused
            || this.PropertyInspector.PropertiesFilter.IsKeyboardFocusWithin)
        {
            this.PropertyInspector.FilterString = string.Empty;
        }
        else if (this.filterComboBox.IsKeyboardFocusWithin && !this.filterComboBox.IsDropDownOpen)
        {
            this.ClearTreeFilter();
        }
    }

    private void ClearTreeFilter_Click(object sender, RoutedEventArgs e)
    {
        this.ClearTreeFilter();
        this.filterComboBox.Focus();
    }

    private void ClearTreeFilter()
    {
        this.filterComboBox.SelectedIndex = -1;
        this.filterTimer.Stop();
        this.SetFilter(string.Empty);
    }

    private void HandleEditReplayed(DependencyObject target, DependencyProperty property, object? value, bool edited)
    {
        var owner = ReferenceEquals(this.CurrentSelection?.Target, target) ? this.CurrentSelection : null;
        EditedPropertiesHelper.UpdateEditedProperty(this.Dispatcher, this.PropertyInspector.PropertyFilter, target, owner, property, property.Name, value, edited);
    }

    private void CopyPropertyChangesHandler(object sender, ExecutedRoutedEventArgs e)
    {
        if (this.CurrentSelection is not null)
        {
            this.SaveEditedProperties(this.CurrentSelection);
        }

        EditedPropertiesHelper.DumpObjectsWithEditedProperties();
    }

    private void SelectItem(DependencyObject? item)
    {
        if (item is not null)
        {
            var node = this.FindItem(item);
            if (node is not null)
            {
                this.CurrentSelection = node;
            }
        }
    }
    #endregion

    #region Private Event Handlers

    private bool IsTrackingInput(PreProcessInputEventArgs e)
    {
        // "Track on click"
        if (this.TrackOnClick
            && e.StagingItem.Input is MouseButtonEventArgs mouseButtonEventArgs
            && mouseButtonEventArgs.RoutedEvent == Mouse.PreviewMouseDownEvent
            && mouseButtonEventArgs.ChangedButton is MouseButton.Left)
        {
            return true;
        }

        // "Track on mouse move"
        if (this.TrackOnMouseMove
            && ((e.StagingItem.Input is MouseEventArgs mouseEventArgs
                && mouseEventArgs.RoutedEvent == Mouse.PreviewMouseMoveEvent)
            // in addition to mouse movements we have to handle cases where the user just hovers over an element and then presses CTRL + SHIFT
            || (e.StagingItem.Input is KeyEventArgs keyEventArgs
                && keyEventArgs.RoutedEvent == Keyboard.PreviewKeyDownEvent
                && keyEventArgs.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)))
        {
            var currentModifiers = InputManager.Current.PrimaryKeyboardDevice.Modifiers;

            var isControlPressed = currentModifiers.HasFlag(ModifierKeys.Control);
            var isShiftPressed = currentModifiers.HasFlag(ModifierKeys.Shift);

            return isControlPressed is true
                   && isShiftPressed is true;
        }

        return false;
    }

    private void HandlePreProcessInputForTracking(object sender, PreProcessInputEventArgs e)
    {
        this.OnPropertyChanged(nameof(this.CurrentFocus));

        if (this.IsTrackingInput(e) is false)
        {
            return;
        }

        var itemToFind = Mouse.PrimaryDevice.GetDirectlyOver(this.IgnoreHitTestVisibility);

        switch (itemToFind)
        {
            case null:
            case var dependencyObject when dependencyObject.IsPartOfSnoopVisualTree():
            case Visual visual when visual.IsDescendantOf(this):
                return;
        }

        // If template parts should be skipped search up the tree of templated parents.
        if (this.SkipTemplateParts
            && itemToFind is FrameworkElement frameworkElement)
        {
            itemToFind = GetItemToFindAndSkipTemplateParts(frameworkElement);
        }

        var node = this.FindItem(itemToFind);
        if (node is not null
            && ReferenceEquals(this.CurrentSelection, node) == false)
        {
            this.CurrentSelection = node;
        }
    }

    private static UIElement? GetItemToFindAndSkipTemplateParts(FrameworkElement? uiElement)
    {
        UIElement? itemToFind = uiElement;

        while (uiElement?.TemplatedParent is not null)
        {
            uiElement = uiElement.TemplatedParent as FrameworkElement;
            itemToFind = uiElement;
        }

        if (itemToFind is not null)
        {
            var parent = VisualTreeHelper.GetParent(itemToFind) as FrameworkElement;

            // If the current item is of a certain type and is part of a template try to look further up the tree
            if (parent is FrameworkElement { TemplatedParent: { } } and (ContentPresenter or AccessText))
            {
                return GetItemToFindAndSkipTemplateParts(parent);
            }
        }

        return itemToFind;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Find the TreeItem for the specified target.
    /// If the item is not found and is not part of the Snoop UI,
    /// the tree will be adjusted to include the root visual the item is in.
    /// </summary>
    private TreeItem? FindItem(object? target)
    {
        if (this.RootTreeItem is null)
        {
            return null;
        }

        {
            var node = this.RootTreeItem.FindNode(target);

            if (node is not null)
            {
                return node;
            }
        }

        // Not every visual element is in the logical or the automation tree, so try the visual tree
        if (this.TreeService.TreeType is TreeType.Logical or TreeType.Automation
            && target is DependencyObject dependencyObject and (Visual or Visual3D))
        {
            var parent = VisualTreeHelper.GetParent(dependencyObject);

            while (parent is not null)
            {
                var node = this.RootTreeItem.FindNode(parent);

                if (node is not null)
                {
                    return node;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        var newRootSet = false;
        var rootVisual = this.RootTreeItem.MainVisual;

        if (target is Visual visual
            && rootVisual is not null)
        {
            // If target is a part of the SnoopUI, let's get out of here.
            if (visual.IsDescendantOf(this))
            {
                return null;
            }

            // If not in the root tree, make the root be the tree the visual is in.
            if (visual.IsDescendantOf(rootVisual) == false)
            {
                var presentationSource = PresentationSource.FromVisual(visual);
                if (presentationSource is null)
                {
                    return null; // Something went wrong. At least we will not crash with null ref here.
                }

                this.SystemResourcesTreeItem = (SystemResourcesTreeItem)new SystemResourcesTreeItem(null, this.TreeService).Reload();
                this.RootTreeItem = this.TreeService.Construct(presentationSource.RootVisual, null);
                newRootSet = true;
            }
        }

        // Constructing a new root already reloads it
        if (newRootSet == false)
        {
            this.SystemResourcesTreeItem?.Reload();
            this.RootTreeItem.Reload();
        }

        this.TreeService.DiagnosticContext.AnalyzeTree();

        {
            var node = this.RootTreeItem.FindNode(target);

            // Tree items are cleared when filtering
            this.SetFilter(this.filter);

            return node;
        }
    }

    private void HandleTreeSelectedItemChanged(object sender, EventArgs e)
    {
        if (!this.isUpdatingTreeFilter && this.Tree.SelectedItem is TreeItem item)
        {
            this.CurrentSelection = item;
        }
    }

    private void ProcessFilter()
    {
        if (SnoopModes.MultipleDispatcherMode
            && this.Dispatcher.CheckAccess() == false)
        {
            this.RunInDispatcherAsync(this.ProcessFilter);
            return;
        }

        if (this.isShuttingDown)
        {
            return;
        }

        if (this.filterComboBox.SelectedItem == this.ClearTreeFilterItem)
        {
            this.ClearTreeFilter();
            return;
        }

        var bindingErrors = this.filterComboBox.SelectedItem == this.BindingErrorsFilterItem;
        this.IsTreeFilterApplied = bindingErrors || this.filter.Length != 0;
        // A queued depth adjustment belongs to the old set of displayed roots.
        this.depthAdjustment?.Abort();
        this.depthAdjustment = null;
        this.UpdateDisplayedTreeItems(() =>
        {
            this.TreeItems.Clear();
            if (this.RootTreeItem is { } root)
            {
                if (this.IsTreeFilterApplied)
                {
                    var query = this.filter;
                    this.AddMatchingRoots(root, bindingErrors ? item => item.HasBindingError : item => item.Filter(query));
                }
                else
                {
                    if (this.SystemResourcesTreeItem is { } resources)
                    {
                        this.TreeItems.Add(resources);
                    }

                    this.TreeItems.Add(root);
                }
            }
        });

        var empty = this.RootTreeItem is not null && this.IsTreeFilterApplied && this.TreeItems.Count == 0;
        if (this.IsTreeFilterEmpty != empty)
        {
            this.IsTreeFilterEmpty = empty;
            this.OnPropertyChanged(nameof(this.IsTreeFilterEmpty));
        }
    }

    private void UpdateDisplayedTreeItems(Action update)
    {
        // Replacing rows can deselect their WPF containers. Keep the inspected object and its highlight.
        this.isUpdatingTreeFilter = true;
        try
        {
            update();
            if (this.CurrentSelection is { } selection)
            {
                selection.IsSelected = true;
            }
        }
        finally
        {
            this.isUpdatingTreeFilter = false;
        }

        // A cached tree peer can still expose the previous roots after a search
        // promotes a child to the top level. Rebuild its children after layout
        // has generated the replacement containers, including for late UIA clients.
        if (UIElementAutomationPeer.FromElement(this.Tree) is { } treePeer)
        {
            this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(treePeer.ResetChildrenCache));
        }
    }

    private void AddMatchingRoots(TreeItem root, Func<TreeItem, bool> matches)
    {
        var remaining = new Stack<TreeItem>();
        remaining.Push(root);
        while (remaining.Count > 0)
        {
            var item = remaining.Pop();
            if (matches(item))
            {
                // Its descendants are already available by expanding this result.
                this.TreeItems.Add(item);
                continue;
            }

            for (var i = item.Children.Count - 1; i >= 0; i--)
            {
                remaining.Push(item.Children[i]);
            }
        }
    }

    protected override void Load(object newRoot)
    {
        this.Refresh();

        this.CurrentSelection = this.RootTreeItem;
    }

    #endregion

    #region Private Fields

    private const double SplitterSize = 5;
    private const double MinimumCenterWidth = 240;
    private const double MinimumTreeWidth = 165;
    private const double MinimumPropertiesWidth = 240;
    private const double MaximumPropertiesWidth = 590;
    private const double MinimumDiagnosticsHeight = 110;
    private const double MaximumDiagnosticsHeight = 440;
    private const double MinimumPreviewHeight = 110;
    private const double CompactPreviewHeight = 60;
    private const double LayoutSizeTolerance = 0.001;

    // The v1.1 reference window's work area, after allowing for the chrome.
    private const double ReferenceWorkbenchWidth = 1360;
    private const double ReferenceWorkbenchHeight = 790;
    private static readonly WorkbenchPaneSizes DefaultLayout = new(282, 354, 210);
    private static readonly WorkbenchPaneSizes PreviewLayout = new(210, 285, 160);
    private static readonly WorkbenchPaneSizes PropertiesLayout = new(270, 460, 240);

    private struct WorkbenchPaneSizes(double tree, double properties, double diagnostics)
    {
        public double Tree = tree;
        public double Properties = properties;
        public double Diagnostics = diagnostics;
    }

    private static readonly Lazy<string> inspectedProcessIdentity = new(() =>
    {
        using var process = Process.GetCurrentProcess();
        return FormattableString.Invariant($"{process.ProcessName} · PID {process.Id}");
    });

    private bool fromTextBox = true;
    private readonly DispatcherTimer filterTimer;

    private readonly DelayedCall filterCall;

    private DispatcherOperation? depthAdjustment;

    private IInputElement? currentFocus;
    private IInputElement? previousFocus;

    /// <summary>
    /// Indicates whether CurrentFocus should return previously focused element.
    /// This fixes problem where Snoop steals the focus from snooped app.
    /// </summary>
    private bool returnPreviousFocus;

    private TreeService treeService = null!;

    private bool isShuttingDown;
    private bool ignoreHitTestVisibility = true;
    private int previewPickRequest;

    private WorkbenchPaneSizes savedWorkbenchSizes;
    private WorkbenchPaneSizes expandedWorkbenchSizes;
    private WorkbenchPaneSizes appliedWorkbenchSizes;
    private double workbenchViewportWidth = ReferenceWorkbenchWidth;
    private double workbenchViewportHeight = ReferenceWorkbenchHeight;
    private bool savedTreeCollapsed;
    private bool savedPropertiesCollapsed;
    private bool savedDiagnosticsCollapsed;

    #endregion

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    private void HandleMakeSettingsApplicationsSpecific_OnClick(object sender, RoutedEventArgs e)
    {
        this.CaptureWorkbenchLayout();
        Settings.Default.SettingsFile = SettingsHelper.GetApplicationSpecificSettingsFile();
        Settings.Default.Save();
    }

    private void HandleDeleteApplicationSpecificSettings_OnClick(object sender, RoutedEventArgs e)
    {
        // Prevent accidental delete of default settings file
        if (Settings.Default.IsDefaultSettingsFile == false)
        {
            File.Delete(Settings.Default.SettingsFile);
        }

        Settings.Default.SettingsFile = SettingsHelper.GetDefaultApplicationSettingsFile();

        Settings.Default.Reload();
        this.RestoreWorkbenchLayout();
    }

    private void HandleOpenSettingsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(Settings.Default.SettingsFile);

        if (directory is not null
            && directory.Length > 0)
        {
            var processStartInfo = new ProcessStartInfo(directory)
            {
                UseShellExecute = true
            };
            using (Process.Start(processStartInfo))
            {
            }
        }
    }

    private void HandleSetWorkbenchDensity_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string density })
        {
            Settings.Default.WorkbenchDensity = density;
        }
    }

    private void RestoreWorkbenchLayout()
    {
        var settings = Settings.Default;
        this.savedWorkbenchSizes = new WorkbenchPaneSizes(
            ReadPaneSize(
                settings.WorkbenchTreeWidth,
                ReadLegacyPaneSize(settings.WorkbenchTreeFraction, ReferenceWorkbenchWidth, DefaultLayout.Tree),
                MinimumTreeWidth),
            ReadPaneSize(settings.WorkbenchPropertiesWidth, DefaultLayout.Properties, MinimumPropertiesWidth, MaximumPropertiesWidth),
            ReadPaneSize(
                settings.WorkbenchDiagnosticsHeight,
                ReadLegacyPaneSize(settings.WorkbenchDiagnosticsFraction, ReferenceWorkbenchHeight, DefaultLayout.Diagnostics),
                MinimumDiagnosticsHeight,
                MaximumDiagnosticsHeight));
        this.expandedWorkbenchSizes = this.savedWorkbenchSizes;
        this.savedTreeCollapsed = settings.WorkbenchTreeCollapsed;
        this.savedPropertiesCollapsed = settings.WorkbenchPropertiesCollapsed;
        this.savedDiagnosticsCollapsed = settings.WorkbenchDiagnosticsCollapsed;
        this.ShowTreePaneItem.IsChecked = !this.savedTreeCollapsed;
        this.ShowPropertiesPaneItem.IsChecked = !this.savedPropertiesCollapsed;
        this.ShowDiagnosticsPaneItem.IsChecked = !this.savedDiagnosticsCollapsed;
        this.ApplyWorkbenchPaneLayout();
        this.RefreshWorkbenchLayoutMenu();
    }

    private void HandleToggleWorkbenchPane_OnClick(object sender, RoutedEventArgs e)
    {
        this.ReadExpandedWorkbenchSizes();
        this.ApplyWorkbenchPaneLayout();
    }

    private void HandleNavigateToTree_OnClick(object sender, RoutedEventArgs e)
        => this.NavigateToWorkbench(WorkbenchDestination.Tree);

    private void HandleNavigateToProperties_OnClick(object sender, RoutedEventArgs e)
        => this.NavigateToWorkbench(WorkbenchDestination.Properties);

    private void HandleNavigateToDiagnostics_OnClick(object sender, RoutedEventArgs e)
        => this.NavigateToWorkbench(WorkbenchDestination.Diagnostics);

    private void HandleNavigateToEvents_OnClick(object sender, RoutedEventArgs e)
        => this.NavigateToWorkbench(WorkbenchDestination.Events);

    private void HandleNavigateToPreview_OnClick(object sender, RoutedEventArgs e)
        => this.NavigateToWorkbench(WorkbenchDestination.Preview);

    private void NavigateToWorkbench(WorkbenchDestination destination)
    {
        switch (destination)
        {
            case WorkbenchDestination.Tree:
                this.ShowWorkbenchPane(this.ShowTreePaneItem);
                this.filterComboBox.Focus();
                break;
            case WorkbenchDestination.Properties:
                this.NavigateToWorkbenchTab(this.ShowPropertiesPaneItem, this.PropertiesWorkbenchPane, this.WorkbenchPropertiesTab);
                break;
            case WorkbenchDestination.Diagnostics:
                this.NavigateToWorkbenchTab(this.ShowDiagnosticsPaneItem, this.DiagnosticsWorkbenchPane, this.WorkbenchDiagnosticsTab);
                break;
            case WorkbenchDestination.Events:
                this.NavigateToWorkbenchTab(this.ShowDiagnosticsPaneItem, this.DiagnosticsWorkbenchPane, this.WorkbenchEventsTab);
                break;
            case WorkbenchDestination.Preview:
                this.WorkbenchPreviewPane.Focus();
                break;
            case WorkbenchDestination.Output:
                this.NavigateToWorkbenchTab(this.ShowDiagnosticsPaneItem, this.DiagnosticsWorkbenchPane, this.WorkbenchOutputTab);
                break;
        }
    }

    private void NavigateToWorkbenchTab(MenuItem paneMenuItem, TabControl pane, TabItem tab)
    {
        this.ShowWorkbenchPane(paneMenuItem);
        pane.SelectedItem = tab;
        tab.Focus();
    }

    private void ShowWorkbenchPane(MenuItem paneMenuItem)
    {
        if (paneMenuItem.IsChecked)
        {
            return;
        }

        this.ReadExpandedWorkbenchSizes();
        paneMenuItem.IsChecked = true;
        this.ApplyWorkbenchPaneLayout();
    }

    private void ApplyWorkbenchPaneLayout()
    {
        var treeVisibility = this.ShowTreePaneItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        this.TreeWorkbenchPane.Visibility = treeVisibility;
        this.TreeHeaderLabel.Visibility = treeVisibility;
        this.TreeTypeComboBox.Visibility = treeVisibility;
        this.GridSplitter.Visibility = treeVisibility;
        this.PropertiesWorkbenchPane.Visibility = this.ShowPropertiesPaneItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        this.PropertiesSplitter.Visibility = this.PropertiesWorkbenchPane.Visibility;
        this.DiagnosticsWorkbenchPane.Visibility = this.ShowDiagnosticsPaneItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        this.DiagnosticsSplitter.Visibility = this.DiagnosticsWorkbenchPane.Visibility;

        this.ApplyWorkbenchColumns();
        this.ApplyWorkbenchRows();
    }

    private void HandleWorkbenchLayoutUpdated(object? sender, EventArgs e)
    {
        // The grid can measure larger than the space it was assigned when its pixel tracks are up against their minima.
        var slot = LayoutInformation.GetLayoutSlot(this.WorkbenchGrid);
        if (slot.Width <= 0 || slot.Height <= 0)
        {
            return;
        }

        var statusHeight = Math.Max(0, LayoutInformation.GetLayoutSlot(this.WorkbenchStatusBar).Height);
        var height = Math.Max(0, slot.Height - statusHeight);
        var widthChanged = SizeChangedFrom(slot.Width, this.workbenchViewportWidth);
        var heightChanged = SizeChangedFrom(height, this.workbenchViewportHeight);
        if (!widthChanged && !heightChanged)
        {
            return;
        }

        this.ReadExpandedWorkbenchSizes();
        if (widthChanged)
        {
            this.workbenchViewportWidth = slot.Width;
            this.ApplyWorkbenchColumns();
        }

        if (heightChanged)
        {
            this.workbenchViewportHeight = height;
            this.ApplyWorkbenchRows();
        }
    }

    private void ApplyWorkbenchColumns()
    {
        var showTree = this.ShowTreePaneItem.IsChecked;
        var showProperties = this.ShowPropertiesPaneItem.IsChecked;
        var treeSplitterWidth = showTree ? SplitterSize : 0;
        var propertiesSplitterWidth = showProperties ? SplitterSize : 0;
        this.TreeSplitterColumn.Width = new GridLength(treeSplitterWidth);
        this.PropertiesSplitterColumn.Width = new GridLength(propertiesSplitterWidth);
        var availableWidth = this.GetAvailableWorkbenchWidth();
        var centerMinimum = Math.Min(MinimumCenterWidth, availableWidth);
        var lateralBudget = availableWidth - centerMinimum;
        var treeMinimum = showTree ? MinimumTreeWidth : 0;
        var propertiesMinimum = showProperties ? MinimumPropertiesWidth : 0;

        // At narrow widths, leave room to move the splitters while keeping the preview usable.
        if (treeMinimum + propertiesMinimum > lateralBudget)
        {
            var scale = 0.8 * lateralBudget / (treeMinimum + propertiesMinimum);
            treeMinimum *= scale;
            propertiesMinimum *= scale;
        }

        var treeExtra = showTree ? Math.Max(0, this.expandedWorkbenchSizes.Tree - treeMinimum) : 0;
        var propertiesExtra = showProperties ? Math.Max(0, this.expandedWorkbenchSizes.Properties - propertiesMinimum) : 0;
        var totalExtra = treeExtra + propertiesExtra;
        var extraBudget = Math.Max(0, lateralBudget - treeMinimum - propertiesMinimum);
        var extraScale = totalExtra > 0
            ? Math.Min(1, extraBudget / totalExtra)
            : 0;

        this.appliedWorkbenchSizes.Tree = treeMinimum + (treeExtra * extraScale);
        this.appliedWorkbenchSizes.Properties = propertiesMinimum + (propertiesExtra * extraScale);
        this.TreePaneColumn.MinWidth = treeMinimum;
        this.TreePaneColumn.Width = new GridLength(this.appliedWorkbenchSizes.Tree);
        this.CenterPaneColumn.MinWidth = centerMinimum;
        this.InspectorPaneColumn.MinWidth = propertiesMinimum;
        this.InspectorPaneColumn.Width = new GridLength(this.appliedWorkbenchSizes.Properties);
        this.UpdateWorkbenchColumnLimits();
    }

    private double GetAvailableWorkbenchWidth()
    {
        return Math.Max(0, this.workbenchViewportWidth - this.TreeSplitterColumn.Width.Value - this.PropertiesSplitterColumn.Width.Value);
    }

    private void UpdateWorkbenchColumnLimits()
    {
        // Pixel-to-star splitters enforce the pixel pane's limits. Include the room needed by its neighbors.
        var lateralBudget = this.GetAvailableWorkbenchWidth() - this.CenterPaneColumn.MinWidth;
        this.TreePaneColumn.MaxWidth = Math.Max(this.TreePaneColumn.MinWidth, lateralBudget - this.InspectorPaneColumn.Width.Value);
        this.InspectorPaneColumn.MaxWidth = BoundSize(lateralBudget - this.TreePaneColumn.Width.Value, this.InspectorPaneColumn.MinWidth, MaximumPropertiesWidth);
    }

    private void ApplyWorkbenchRows()
    {
        var showDiagnostics = this.ShowDiagnosticsPaneItem.IsChecked;
        var splitterHeight = showDiagnostics ? SplitterSize : 0;
        var availableHeight = Math.Max(0, this.workbenchViewportHeight - splitterHeight);
        // The diagnostics tabs include two control rows before their first line of data.
        // When the command bar wraps, retain those rows and space for the preview tools.
        var diagnosticsBudget = Math.Max(availableHeight / 2, availableHeight - CompactPreviewHeight);
        var diagnosticsMinimum = showDiagnostics ? Math.Min(MinimumDiagnosticsHeight, diagnosticsBudget) : 0;
        var previewMinimum = Math.Min(MinimumPreviewHeight, availableHeight - diagnosticsMinimum);
        this.appliedWorkbenchSizes.Diagnostics = showDiagnostics
            ? Math.Min(this.expandedWorkbenchSizes.Diagnostics, availableHeight - previewMinimum)
            : 0;

        this.PreviewPaneRow.MinHeight = previewMinimum;
        this.DiagnosticsSplitterRow.Height = new GridLength(splitterHeight);
        this.DiagnosticsPaneRow.MinHeight = diagnosticsMinimum;
        this.DiagnosticsPaneRow.MaxHeight = Math.Min(MaximumDiagnosticsHeight, availableHeight - previewMinimum);
        this.DiagnosticsPaneRow.Height = new GridLength(this.appliedWorkbenchSizes.Diagnostics);
    }

    private void ReadExpandedWorkbenchSizes()
    {
        // Compare with the applied sizes so passive window clamps and folded panes are never saved as edits.
        if (this.GridSplitter.Visibility == Visibility.Visible)
        {
            ReadSplitterEdit(
                this.TreePaneColumn.Width.Value, ref this.appliedWorkbenchSizes.Tree, ref this.expandedWorkbenchSizes.Tree, MinimumTreeWidth);
        }

        if (this.PropertiesSplitter.Visibility == Visibility.Visible)
        {
            ReadSplitterEdit(
                this.InspectorPaneColumn.Width.Value, ref this.appliedWorkbenchSizes.Properties, ref this.expandedWorkbenchSizes.Properties, MinimumPropertiesWidth, MaximumPropertiesWidth);
        }

        if (this.DiagnosticsSplitter.Visibility == Visibility.Visible)
        {
            ReadSplitterEdit(
                this.DiagnosticsPaneRow.Height.Value, ref this.appliedWorkbenchSizes.Diagnostics, ref this.expandedWorkbenchSizes.Diagnostics, MinimumDiagnosticsHeight, MaximumDiagnosticsHeight);
        }
    }

    private static void ReadSplitterEdit(double current, ref double applied, ref double expanded, double minimum, double maximum = double.PositiveInfinity)
    {
        if (SizeChangedFrom(current, applied))
        {
            // In a compact window, apply the drag delta to the requested size for the expanded layout.
            expanded = BoundSize(expanded + (current - applied), minimum, maximum);
            applied = current;
        }
    }

    private void CaptureWorkbenchLayout()
    {
        this.ReadExpandedWorkbenchSizes();
        var settings = Settings.Default;
        // Save only changed axes so closing an untouched inspector cannot overwrite another window's layout.
        if (SizeChangedFrom(this.expandedWorkbenchSizes.Tree, this.savedWorkbenchSizes.Tree))
        {
            settings.WorkbenchTreeWidth = FormatPaneSize(this.expandedWorkbenchSizes.Tree);
        }

        if (SizeChangedFrom(this.expandedWorkbenchSizes.Properties, this.savedWorkbenchSizes.Properties))
        {
            settings.WorkbenchPropertiesWidth = FormatPaneSize(this.expandedWorkbenchSizes.Properties);
        }

        if (SizeChangedFrom(this.expandedWorkbenchSizes.Diagnostics, this.savedWorkbenchSizes.Diagnostics))
        {
            settings.WorkbenchDiagnosticsHeight = FormatPaneSize(this.expandedWorkbenchSizes.Diagnostics);
        }

        this.savedWorkbenchSizes = this.expandedWorkbenchSizes;
        var treeCollapsed = !this.ShowTreePaneItem.IsChecked;
        if (treeCollapsed != this.savedTreeCollapsed)
        {
            settings.WorkbenchTreeCollapsed = treeCollapsed;
            this.savedTreeCollapsed = treeCollapsed;
        }

        var propertiesCollapsed = !this.ShowPropertiesPaneItem.IsChecked;
        if (propertiesCollapsed != this.savedPropertiesCollapsed)
        {
            settings.WorkbenchPropertiesCollapsed = propertiesCollapsed;
            this.savedPropertiesCollapsed = propertiesCollapsed;
        }

        var diagnosticsCollapsed = !this.ShowDiagnosticsPaneItem.IsChecked;
        if (diagnosticsCollapsed != this.savedDiagnosticsCollapsed)
        {
            settings.WorkbenchDiagnosticsCollapsed = diagnosticsCollapsed;
            this.savedDiagnosticsCollapsed = diagnosticsCollapsed;
        }
    }

    private static double ReadLegacyPaneSize(string? value, double referenceLength, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            && fraction is > 0 and < 1 ? referenceLength * fraction : fallback;
    }

    private static double ReadPaneSize(string? value, double fallback, double minimum, double maximum = double.PositiveInfinity)
    {
        var size = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed is > 0 and < double.PositiveInfinity ? parsed : fallback;
        return BoundSize(size, minimum, maximum);
    }

    private static double BoundSize(double size, double minimum, double maximum)
        => Math.Max(minimum, Math.Min(maximum, size));

    private static bool SizeChangedFrom(double current, double previous)
        => Math.Abs(current - previous) > LayoutSizeTolerance;

    private static string? FormatPaneSize(double? size)
        => size?.ToString("R", CultureInfo.InvariantCulture);

    private void HandleResetWorkbenchLayout_OnClick(object sender, RoutedEventArgs e)
    {
        this.ApplyWorkbenchLayoutPreset(null);
    }

    private void HandlePreviewWorkbenchLayout_OnClick(object sender, RoutedEventArgs e)
    {
        this.ApplyWorkbenchLayoutPreset(PreviewLayout);
    }

    private void HandlePropertiesWorkbenchLayout_OnClick(object sender, RoutedEventArgs e)
    {
        this.ApplyWorkbenchLayoutPreset(PropertiesLayout);
    }

    private void ApplyWorkbenchLayoutPreset(WorkbenchPaneSizes? preset)
    {
        var settings = Settings.Default;
        settings.WorkbenchTreeFraction = null;
        settings.WorkbenchDiagnosticsFraction = null;
        settings.WorkbenchTreeWidth = FormatPaneSize(preset?.Tree);
        settings.WorkbenchPropertiesWidth = FormatPaneSize(preset?.Properties);
        settings.WorkbenchDiagnosticsHeight = FormatPaneSize(preset?.Diagnostics);
        settings.WorkbenchTreeCollapsed = false;
        settings.WorkbenchPropertiesCollapsed = false;
        settings.WorkbenchDiagnosticsCollapsed = false;
        this.RestoreWorkbenchLayout();
    }

    private void HandleWorkbenchLayoutMenuOpened(object sender, RoutedEventArgs e)
    {
        this.ReadExpandedWorkbenchSizes();
        this.RefreshWorkbenchLayoutMenu();
    }

    private void RefreshWorkbenchLayoutMenu()
    {
        this.DefaultWorkbenchLayoutItem.IsChecked = this.MatchesWorkbenchLayout(DefaultLayout);
        this.PreviewWorkbenchLayoutItem.IsChecked = this.MatchesWorkbenchLayout(PreviewLayout);
        this.PropertiesWorkbenchLayoutItem.IsChecked = this.MatchesWorkbenchLayout(PropertiesLayout);
        var custom = !this.DefaultWorkbenchLayoutItem.IsChecked && !this.PreviewWorkbenchLayoutItem.IsChecked && !this.PropertiesWorkbenchLayoutItem.IsChecked;
        this.CustomWorkbenchLayoutItem.IsChecked = custom;
        this.CustomWorkbenchLayoutItem.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool MatchesWorkbenchLayout(WorkbenchPaneSizes layout)
    {
        return !SizeChangedFrom(this.expandedWorkbenchSizes.Tree, layout.Tree)
            && !SizeChangedFrom(this.expandedWorkbenchSizes.Properties, layout.Properties)
            && !SizeChangedFrom(this.expandedWorkbenchSizes.Diagnostics, layout.Diagnostics);
    }

    private void HandleResetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, InspectorLocalization.Default["Workbench.ResetSettingsMessage"], string.Empty, MessageBoxButton.YesNo) == MessageBoxResult.No)
        {
            return;
        }

        Settings.Default.Reset();
        this.RestoreWorkbenchLayout();

        // load whether all properties are shown by default
        this.PropertyInspector.ShowDefaults = Settings.Default.ShowDefaults;

        // load whether the previewer is shown by default
        this.PreviewArea.IsActive = Settings.Default.ShowPreviewer;

        this.PropertyInspector.checkBoxClearAfterDelve.IsChecked = Settings.Default.ClearAfterDelve;

        this.eventsView.UpdateTrackers();
        this.eventsView.MaxEventsDisplayed = Settings.Default.MaximumTrackedEvents;

        this.debugListenerControl.FiltersViewModel.InitializeFilters(Settings.Default.SnoopDebugFilters);
    }

    private void HandleLaunchDebugger_OnClick(object sender, RoutedEventArgs e)
    {
        Debugger.Launch();
    }

    private void HandleSnoopSnoop_OnClick(object sender, RoutedEventArgs e)
    {
        SnoopPartsRegistry.IsSnoopingSnoop = true;

        new SnoopUI().Inspect(this);
    }

    private void HandleHighlightOptions_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SnoopBaseWindow
        {
            Content = new HighlightSettingsView(),
            Owner = this,
            MinWidth = 480,
            MinHeight = 360,
            Width = 480,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow
        };
        window.SetBinding(TitleProperty, new InspectorLocExtension("HighlightSettings.Title").CreateBinding());
        window.ShowDialog();
    }

    private void HandleSnoopVersion_OnClick(object sender, RoutedEventArgs e)
    {
        ClipboardHelper.SetText(this.SnoopVersionHeader.Text);
    }
}

public class PropertyValueInfo
{
    public PropertyValueInfo(string name, object? value)
    {
        this.PropertyName = name;
        this.PropertyValue = value;
    }

    public string PropertyName { get; }

    public object? PropertyValue { get; }
}

public static class EditedPropertiesHelper
{
    private static readonly object @lock = new();

    private static readonly Dictionary<Dispatcher, List<EditedObject>> itemsWithEditedProperties = new();

    public static void AddEditedProperty(Dispatcher dispatcher, TreeItem propertyOwner, PropertyInformation propInfo)
    {
        UpdateEditedProperty(dispatcher, (object?)propInfo.Filter ?? propertyOwner, propInfo.Target ?? propertyOwner.Target, propertyOwner,
            (object?)propInfo.DependencyProperty ?? propInfo.Name ?? propInfo.DisplayName, propInfo.DisplayName, propInfo.Value, true);
    }

    internal static void UpdateEditedProperty(Dispatcher dispatcher, object scope, object target, TreeItem? owner, object key, string name, object? value, bool edited)
    {
        lock (@lock)
        {
            if (!itemsWithEditedProperties.TryGetValue(dispatcher, out var groups))
            {
                if (!edited)
                {
                    return;
                }

                groups = new List<EditedObject>();
                itemsWithEditedProperties.Add(dispatcher, groups);
            }

            var group = groups.Find(item => item.Matches(scope, target));
            if (edited)
            {
                if (group is null)
                {
                    var label = owner?.ToString() ?? (target is FrameworkElement { Name.Length: > 0 } element
                        ? $"{element.Name} ({target.GetType().Name})" : target.GetType().Name);
                    group = new EditedObject(scope, target, label);
                    groups.Add(group);
                }

                var displayName = group.Properties.TryGetValue(key, out var previous) ? previous.PropertyName : name;
                group.Properties[key] = new PropertyValueInfo(displayName, value);
            }
            else if (group is not null && group.Properties.Remove(key) && group.Properties.Count == 0)
            {
                groups.Remove(group);
                if (groups.Count == 0)
                {
                    itemsWithEditedProperties.Remove(dispatcher);
                }
            }
        }
    }

    public static void DumpObjectsWithEditedProperties()
    {
        var report = FormatReport();
        LogHelper.WriteLine(report);
        ClipboardHelper.SetText(report);
    }

    internal static string FormatReport()
    {
        lock (@lock)
        {
            if (itemsWithEditedProperties.Count == 0)
            {
                return InspectorLocalization.Default["PropertyInspector.NoEditedProperties"];
            }

            var sb = new StringBuilder();
            sb.AppendFormat(
                "Snoop dump as of {0:yyyy-MM-dd HH:mm:ss}{1}--- OBJECTS WITH EDITED PROPERTIES ---{1}",
                DateTime.Now,
                Environment.NewLine);

            var dispatcherCount = 1;

            foreach (var dispatcherItemPair in itemsWithEditedProperties)
            {
                if (itemsWithEditedProperties.Count > 1)
                {
                    sb.AppendFormat("-- Dispatcher #{0} -- {1}", dispatcherCount++, Environment.NewLine);
                }

                foreach (var group in dispatcherItemPair.Value)
                {
                    sb.AppendFormat("Object: {0}{1}", group.Label, Environment.NewLine);
                    foreach (var propInfo in group.Properties.Values)
                    {
                        sb.AppendFormat(
                            "\tProperty: {0}, New Value: {1}{2}",
                            propInfo.PropertyName,
                            propInfo.PropertyValue,
                            Environment.NewLine);
                    }
                }

                if (itemsWithEditedProperties.Count > 1)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }

    private sealed class EditedObject
    {
        private readonly WeakReference<object> scope;
        private readonly WeakReference<object> target;

        public EditedObject(object scope, object target, string label)
        {
            this.scope = new WeakReference<object>(scope);
            this.target = new WeakReference<object>(target);
            this.Label = label;
        }

        public string Label { get; }

        public Dictionary<object, PropertyValueInfo> Properties { get; } = new();

        public bool Matches(object editor, object owner)
            => this.scope.TryGetTarget(out var savedEditor) && ReferenceEquals(savedEditor, editor)
               && this.target.TryGetTarget(out var savedOwner) && ReferenceEquals(savedOwner, owner);
    }
}
