// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using JetBrains.Annotations;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

public partial class PropertyGrid2 : INotifyPropertyChanged
{
    public static readonly RoutedCommand ShowBindingErrorsCommand = new(nameof(ShowBindingErrorsCommand), typeof(PropertyGrid2));
    public static readonly RoutedCommand ShowPropertyOriginCommand = new(nameof(ShowPropertyOriginCommand), typeof(PropertyGrid2));
    public static readonly RoutedCommand ClearCommand = new(nameof(ClearCommand), typeof(PropertyGrid2));
    public static readonly RoutedCommand SortCommand = new(nameof(SortCommand), typeof(PropertyGrid2));

    public PropertyGrid2()
    {
        this.propertiesView = CollectionViewSource.GetDefaultView(this.Properties);
        this.propertiesView.Filter = item => ((PropertyInformation)item).IsVisible;
        if (this.propertiesView is ICollectionViewLiveShaping live)
        {
            live.LiveFilteringProperties.Add(nameof(PropertyInformation.IsVisible));
            live.LiveSortingProperties.Add(nameof(PropertyInformation.OriginSortKey));
        }

        this.Sort(".", ListSortDirection.Ascending);

        this.processIncrementalCall = new DelayedCall(this.ProcessIncrementalPropertyAdd, DispatcherPriority.Background);
        this.filterCall = new DelayedCall(this.RefreshFilterView, DispatcherPriority.Background);

        this.InitializeComponent();

        this.Loaded += this.HandleLoaded;
        this.Unloaded += this.HandleUnloaded;

        this.CommandBindings.Add(new CommandBinding(ShowBindingErrorsCommand, this.HandleShowBindingErrors, this.CanShowBindingErrors));
        this.CommandBindings.Add(new CommandBinding(ShowPropertyOriginCommand, this.ShowPropertyOrigin,
            (_, e) => e.CanExecute = !this.NameValueOnly && (e.Parameter as PropertyInformation ?? this.Selection) is not null));
        this.CommandBindings.Add(new CommandBinding(ClearCommand, this.HandleClear, this.CanClear));
        this.CommandBindings.Add(new CommandBinding(SortCommand, this.HandleSort));

        // The sort descriptions of propertiesView are the single source of truth for sorting,
        // so the sort indicators in the column headers just follow them.
        ((INotifyCollectionChanged)this.propertiesView.SortDescriptions).CollectionChanged += this.HandleSortDescriptionsChanged;
        this.UpdateSortIndicators();

        this.filterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(0.3)
        };
        this.filterTimer.Tick += (_, _) =>
        {
            this.filterCall.Enqueue(this.Dispatcher);
            this.filterTimer.Stop();
        };
    }

    public bool NameValueOnly
    {
        get
        {
            return this.nameValueOnly;
        }

        set
        {
            this.nameValueOnly = value;
            var gridView = this.ListView is not null && this.ListView.View is not null ? this.ListView.View as GridView : null;
            if (this.nameValueOnly && gridView is not null && gridView.Columns.Count != 2)
            {
                gridView.Columns.RemoveAt(0);
                while (gridView.Columns.Count > 2)
                {
                    gridView.Columns.RemoveAt(2);
                }
            }
        }
    }

    private bool nameValueOnly;

    public ObservableCollection<PropertyInformation> Properties { get; } = new();

    internal event Action<DependencyObject, DependencyProperty, object?, bool>? EditReplayed;

    internal bool CanUndoEdit => this.Filter?.EditHistory.CanReplay(undo: true) == true;

    internal bool CanRedoEdit => this.Filter?.EditHistory.CanReplay(undo: false) == true;

    internal bool UndoEdit() => this.ReplayEdit(undo: true);

    internal bool RedoEdit() => this.ReplayEdit(undo: false);

    private bool ReplayEdit(bool undo)
    {
        if (this.Filter is not { } filter)
        {
            return false;
        }

        return filter.EditHistory.Replay(undo, (target, dp, value, edited) =>
        {
            filter.SetEdited(target, dp, edited);
            foreach (var row in this.Properties.Where(p => ReferenceEquals(p.Target, target) && p.DependencyProperty == dp).ToList())
            {
                row.SetEditValidation(PropertyEditValidation.None);
                row.IsValueChangedByUser = edited;
            }

            this.EditReplayed?.Invoke(target, dp, value, edited);
        });
    }

    public object? Target
    {
        get { return this.GetValue(TargetProperty); }
        set { this.SetValue(TargetProperty, value); }
    }

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(
            nameof(Target),
            typeof(object),
            typeof(PropertyGrid2),
            new PropertyMetadata(OnTargetChanged));

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var propertyGrid = (PropertyGrid2)d;
        propertyGrid.ChangeTarget(e.NewValue);
    }

    private void ChangeTarget(object newTarget)
    {
        if (this.target != newTarget)
        {
            this.ClosePropertyOrigin();
            this.target = newTarget;

            foreach (var property in this.Properties)
            {
                property.Teardown();
            }

            this.RefreshPropertyGrid();

            this.OnPropertyChanged(nameof(this.Type));
        }
    }

    public PropertyInformation? Selection
    {
        get { return this.selection; }

        set
        {
            if (ReferenceEquals(this.selection, value))
            {
                return;
            }

            this.ClosePropertyOrigin();
            this.selection?.SetEditValidation(PropertyEditValidation.None);
            this.selection = value;
            this.OnPropertyChanged(nameof(this.Selection));
        }
    }

    private PropertyInformation? selection;

    public BindableType? Type
    {
        get
        {
            if (this.target is not null)
            {
                return this.target.GetType();
            }

            return null;
        }
    }

    private void RefreshFilterView()
    {
        var needsLiveFilter = this.Filter is { EditedOnly: true } or { OriginFilter: not PropertyOriginFilter.All };
        if (this.propertiesView is ICollectionViewLiveShaping { CanChangeLiveFiltering: true } live && live.IsLiveFiltering != needsLiveFilter)
        {
            // Defer so WPF sends a view reset when it rebuilds the list for the new live-filter mode.
            // Otherwise existing ListViewItem containers can keep displaying the previous rows.
            using (this.propertiesView.DeferRefresh())
            {
                live.IsLiveFiltering = needsLiveFilter;
            }
        }
        else
        {
            this.propertiesView.Refresh();
        }
    }

    protected override void OnFilterChanged()
    {
        base.OnFilterChanged();

        this.filterTimer.Stop();
        this.filterTimer.Start();
    }

    /// <summary>
    /// Delayed loading of the property inspector to avoid creating the entire list of property
    /// editors immediately after selection. Keeps that app running smooth.
    /// </summary>
    private void ProcessIncrementalPropertyAdd()
    {
        var target = this.target;
        if (target is null)
        {
            return;
        }

        var pendingProperties = this.propertiesToAdd;
        if (pendingProperties is null)
        {
            var properties = PropertyInformation.GetProperties(target);
            if (ReferenceEquals(target, this.target))
            {
                this.propertiesToAdd = properties.GetEnumerator();
                this.processIncrementalCall.Enqueue(this.Dispatcher);
            }

            return;
        }

        const int numberToAdd = 10;
        var i = 0;
        for (; i < numberToAdd && pendingProperties.MoveNext(); ++i)
        {
            // iterate over the PropertyInfo objects,
            // setting the property grid's filter on each object,
            // and adding those properties to the observable collection of propertiesToSort (this.properties)
            var property = pendingProperties.Current;
            property.Filter = this.Filter;

            this.Properties.Add(property);
            if (!ReferenceEquals(pendingProperties, this.propertiesToAdd))
            {
                return;
            }
        }

        if (i == numberToAdd)
        {
            this.processIncrementalCall.Enqueue(this.Dispatcher);
        }
        else
        {
            pendingProperties.Dispose();
            this.propertiesToAdd = null;
        }
    }

    private void ShowPropertyOrigin(object sender, ExecutedRoutedEventArgs e)
    {
        var property = e.Parameter as PropertyInformation ?? this.Selection;
        if (property is null || !this.ListView.Items.Contains(property))
        {
            return;
        }

        this.ListView.SetCurrentValue(ListView.SelectedItemProperty, property);
        // A replacement binding can produce the same effective value without a change notification.
        property.RefreshValueSource();
        if (!ReferenceEquals(this.OriginDetailsPanel.DataContext, property))
        {
            this.ClosePropertyOrigin();
            this.OriginDetailsPanel.DataContext = property;
            this.OriginDetailsPanel.Visibility = Visibility.Visible;
            PropertyChangedEventManager.AddHandler(InspectorLocalization.Default, this.OriginLanguageChanged, nameof(InspectorLocalization.LanguageTag));
        }

        this.OriginDetailsText.Focus();
        this.OriginDetailsText.ScrollToHome();
        e.Handled = true;
    }

    private void OriginLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.Dispatcher.CheckAccess())
        {
            this.RefreshOriginText();
        }
        else if (!this.Dispatcher.HasShutdownStarted)
        {
            this.Dispatcher.InvokeAsync(this.RefreshOriginText);
        }
    }

    private void RefreshOriginText() => this.OriginDetailsText.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

    private void ClosePropertyOrigin()
    {
        if (this.OriginDetailsPanel?.DataContext is not PropertyInformation)
        {
            return;
        }

        PropertyChangedEventManager.RemoveHandler(InspectorLocalization.Default, this.OriginLanguageChanged, nameof(InspectorLocalization.LanguageTag));
        this.OriginDetailsPanel.DataContext = null;
        this.OriginDetailsPanel.Visibility = Visibility.Collapsed;
    }

    private void CloseOriginDetails_Click(object sender, RoutedEventArgs e)
    {
        this.ClosePropertyOrigin();
        var selectedRow = this.ListView.ItemContainerGenerator.ContainerFromItem(this.ListView.SelectedItem) as ListViewItem;
        (selectedRow as UIElement ?? this.ListView).Focus();
    }

    private void HandleShowBindingErrors(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        var propertyInformation = (PropertyInformation)eventArgs.Parameter;

        if (string.IsNullOrEmpty(propertyInformation.BindingError))
        {
            propertyInformation.UpdateBindingError();
        }

        var textBox = new TextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        textBox.SetBinding(TextBox.TextProperty, new Binding(nameof(propertyInformation.BindingError)) { Source = propertyInformation, Mode = BindingMode.OneWay });

        var window = new SnoopBaseWindow
        {
            Content = textBox,
            Width = 400,
            Height = 300,
            Title = string.Format(
                CultureInfo.CurrentCulture,
                InspectorLocalization.Default["PropertyGrid.BindingErrorsFor"],
                propertyInformation.DisplayName)
        };

        window.Show();
    }

    private void CanShowBindingErrors(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Parameter is PropertyInformation propertyInformation)
        {
            e.CanExecute = propertyInformation.IsInvalidBinding;
        }

        e.Handled = true;
    }

    private void CanClear(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = e.Parameter is PropertyInformation property && this.CanClearRow(property);
        e.Handled = true;
    }

    private void HandleClear(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is PropertyInformation property && this.CanClearRow(property))
        {
            property.Clear();
        }
    }

    private bool CanClearRow(PropertyInformation property)
        => this.Properties.Contains(property) && property.CanEdit && property.IsLocallySet;

    private void HandleSort(object sender, ExecutedRoutedEventArgs args)
    {
        var headerClicked = (GridViewColumnHeader)args.OriginalSource;

        if (headerClicked.Column is null)
        {
            return;
        }

        var propertyName = GridViewSort.GetPropertyName(headerClicked.Column);

        if (string.IsNullOrEmpty(propertyName))
        {
            return;
        }

        this.Sort(propertyName!);
    }

    private void HandleSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.UpdateSortIndicators();
    }

    private void UpdateSortIndicators()
    {
        GridViewSort.UpdateSortIndicators(this.ListView?.View as GridView, this.propertiesView.SortDescriptions);
    }

    private void HandleLoaded(object sender, EventArgs e)
    {
        if (this.unloaded)
        {
            this.RefreshPropertyGrid();
            this.unloaded = false;
        }
    }

    private void HandleUnloaded(object sender, EventArgs e)
    {
        this.ClosePropertyOrigin();
        foreach (var property in this.Properties)
        {
            property.Teardown();
        }

        this.unloaded = true;
    }

    private void HandleNameClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var property = (PropertyInformation)((FrameworkElement)sender).DataContext;

            object? newTarget = null;

            if (Keyboard.Modifiers is ModifierKeys.Shift)
            {
                newTarget = property.Binding;
            }
            else if (Keyboard.Modifiers is ModifierKeys.Control)
            {
                newTarget = property.BindingExpression;
            }
            else if (Keyboard.Modifiers is ModifierKeys.None)
            {
                newTarget = property.Value;
            }

            if (newTarget is not null)
            {
                PropertyInspector.DelveCommand.Execute(property, this);
            }
        }
    }

    private void Sort(string propertyPath, ListSortDirection? newDirection = null)
    {
        using (this.propertiesView.DeferRefresh())
        {
            // DisplayName is an implicit tie-breaker. The name header itself sorts by ".".
            var tie = new SortDescription(nameof(PropertyInformation.DisplayName), ListSortDirection.Ascending);
            this.propertiesView.SortDescriptions.Remove(tie);
            var doesSortExist = this.propertiesView.SortDescriptions.Any(x => x.PropertyName == propertyPath);
            var existingSort = this.propertiesView.SortDescriptions.FirstOrDefault(x => x.PropertyName == propertyPath);

            newDirection ??= doesSortExist is false
                ? ListSortDirection.Ascending
                : existingSort.Direction is ListSortDirection.Descending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;

            var clearExistingSortDescriptions = (Keyboard.Modifiers & ModifierKeys.Shift) is not ModifierKeys.Shift;
            if (clearExistingSortDescriptions)
            {
                this.propertiesView.SortDescriptions.Clear();
            }
            else if (doesSortExist)
            {
                this.propertiesView.SortDescriptions.Remove(existingSort);
            }

            this.propertiesView.SortDescriptions.Add(new SortDescription(propertyPath, newDirection.Value));
            var sortingByOrigin = this.propertiesView.SortDescriptions.Any(x => x.PropertyName == nameof(PropertyInformation.OriginSortKey));
            if (sortingByOrigin && !this.propertiesView.SortDescriptions.Any(x => x.PropertyName == "."))
            {
                this.propertiesView.SortDescriptions.Add(tie);
            }

            if (this.propertiesView is ICollectionViewLiveShaping { CanChangeLiveSorting: true } live && live.IsLiveSorting != sortingByOrigin)
            {
                live.IsLiveSorting = sortingByOrigin;
            }
        }
    }

    public void RefreshPropertyGrid()
    {
        this.ClosePropertyOrigin();
        this.Properties.Clear();

        this.propertiesToAdd?.Dispose();
        this.propertiesToAdd = null;
        if (this.target is not null)
        {
            this.processIncrementalCall.Enqueue(this.Dispatcher);
        }
    }

    internal void EndInspection()
    {
        this.SetCurrentValue(TargetProperty, null);
        this.Selection = null;
        this.filterTimer.Stop();
    }

    private object? target;

    private IEnumerator<PropertyInformation>? propertiesToAdd;
    private readonly DelayedCall processIncrementalCall;
    private readonly DelayedCall filterCall;
    private bool unloaded;

    private readonly DispatcherTimer filterTimer;
    private readonly ICollectionView propertiesView;

    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected void OnPropertyChanged(string propertyName)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
