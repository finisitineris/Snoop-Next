// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop;

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Snoop.Core;
using Snoop.Data;
using Snoop.Infrastructure;
using Snoop.Next.Localization;
using Snoop.Views;

public partial class AppChooser
{
    public static readonly RoutedCommand InspectCommand = new(nameof(InspectCommand), typeof(AppChooser));
    public static readonly RoutedCommand RefreshCommand = new(nameof(RefreshCommand), typeof(AppChooser));
    public static readonly RoutedCommand MagnifyCommand = new(nameof(MagnifyCommand), typeof(AppChooser));
    public static readonly RoutedCommand ClearTargetFiltersCommand = new(nameof(ClearTargetFiltersCommand), typeof(AppChooser));
    public static readonly RoutedCommand SettingsCommand = new(nameof(SettingsCommand), typeof(AppChooser));
    public static readonly RoutedCommand MinimizeCommand = new(nameof(MinimizeCommand), typeof(AppChooser));

    private static readonly DependencyPropertyKey SortColumnPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SortColumn), typeof(int), typeof(AppChooser), new PropertyMetadata(1));

    public static readonly DependencyProperty SortColumnProperty = SortColumnPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TargetSearchProperty = DependencyProperty.Register(
        nameof(TargetSearch), typeof(string), typeof(AppChooser),
        new PropertyMetadata(string.Empty, (d, _) => ((AppChooser)d).RefreshTargetFilter()));

    public static readonly DependencyProperty TargetRuntimeFilterProperty = DependencyProperty.Register(
        nameof(TargetRuntimeFilter), typeof(TargetRuntime), typeof(AppChooser),
        new PropertyMetadata(TargetRuntime.None, (d, _) => ((AppChooser)d).RefreshTargetFilter()));

    public static readonly DependencyProperty TargetArchitectureFilterProperty = DependencyProperty.Register(
        nameof(TargetArchitectureFilter), typeof(string), typeof(AppChooser),
        new PropertyMetadata(string.Empty, (d, _) => ((AppChooser)d).RefreshTargetFilter()));

    private static readonly DependencyPropertyKey TargetFilterTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TargetFilterText), typeof(string), typeof(AppChooser), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TargetFilterTextProperty = TargetFilterTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasTargetFilterPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasTargetFilter), typeof(bool), typeof(AppChooser), new PropertyMetadata(false));

    public static readonly DependencyProperty HasTargetFilterProperty = HasTargetFilterPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsAttachingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsAttaching), typeof(bool), typeof(AppChooser), new PropertyMetadata(false));

    public static readonly DependencyProperty IsAttachingProperty = IsAttachingPropertyKey.DependencyProperty;

    private readonly ObservableCollection<WindowInfo> windowInfos = new();
    private readonly double singleRowMinimumWidth;
    private double preferredWidth;
    private bool? selectSingleTargetAfterRefresh;
    private bool closeAfterAttach;
    private bool isClosed;
    private bool returnToPickerPending;
    private Guid launcherReturnToken;
    private EventWaitHandle? launcherReturnEvent;
    private RegisteredWaitHandle? launcherReturnWait;

    private LowLevelKeyboardHook? keyboardHook;

    static AppChooser()
    {
        RefreshCommand.InputGestures.Add(new KeyGesture(Key.F5));
    }

    public AppChooser()
    {
        this.WindowInfos = CollectionViewSource.GetDefaultView(this.windowInfos);
        this.WindowInfos.SortDescriptions.Add(new SortDescription(nameof(WindowInfo.OwningProcessId), ListSortDirection.Ascending));

        this.InitializeComponent();
        this.singleRowMinimumWidth = this.MinWidth;
        this.preferredWidth = this.Width;
        this.Loaded += this.HandleChooserLoaded;

        ThemeManager.Current.ApplyTheme(ThemeMode.Auto, this);

        this.CommandBindings.Add(new CommandBinding(RefreshCommand, this.HandleRefreshCommand, this.HandleCanUseLauncherCommand));
        this.CommandBindings.Add(new CommandBinding(InspectCommand, this.HandleInspectCommand, this.HandleCanInspectOrMagnifyCommand));
        this.CommandBindings.Add(new CommandBinding(MagnifyCommand, this.HandleMagnifyCommand, this.HandleCanInspectOrMagnifyCommand));
        this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, this.HandleFindTarget, this.HandleCanUseLauncherCommand));
        this.CommandBindings.Add(new CommandBinding(ClearTargetFiltersCommand, (_, _) => this.ClearTargetFilters(),
                                                   (_, e) => e.CanExecute = !this.IsAttaching && (this.HasTargetFilter || !string.IsNullOrEmpty(this.TargetSearch))));
        this.CommandBindings.Add(new CommandBinding(SettingsCommand, this.HandleSettingsCommand, this.HandleCanUseLauncherCommand));
        this.CommandBindings.Add(new CommandBinding(MinimizeCommand, this.HandleMinimizeCommand));
        this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, this.HandleCloseCommand));

        if (Settings.Default.StartAppChooserMinimized)
        {
            this.WindowState = WindowState.Minimized;
        }
    }

    public ICollectionView WindowInfos { get; }

    public int SortColumn => (int)this.GetValue(SortColumnProperty);

    public string TargetSearch
    {
        get => (string)this.GetValue(TargetSearchProperty);
        set => this.SetValue(TargetSearchProperty, value);
    }

    public TargetRuntime TargetRuntimeFilter
    {
        get => (TargetRuntime)this.GetValue(TargetRuntimeFilterProperty);
        set => this.SetValue(TargetRuntimeFilterProperty, value);
    }

    public string TargetArchitectureFilter
    {
        get => (string)this.GetValue(TargetArchitectureFilterProperty);
        set => this.SetValue(TargetArchitectureFilterProperty, value);
    }

    public string TargetFilterText => (string)this.GetValue(TargetFilterTextProperty);

    public bool HasTargetFilter => (bool)this.GetValue(HasTargetFilterProperty);

    public bool IsAttaching => (bool)this.GetValue(IsAttachingProperty);

    private bool CanStartAttach => !this.IsAttaching && !this.isClosed && this.IsEnabled && !ComponentDispatcher.IsThreadModal;

    private void ClearTargetFilters()
    {
        if (this.IsAttaching)
        {
            return;
        }

        this.TargetSearch = string.Empty;
        this.TargetRuntimeFilter = TargetRuntime.None;
        this.TargetArchitectureFilter = string.Empty;
    }

    private void RefreshTargetFilter()
    {
        var term = this.TargetSearch?.Trim() ?? string.Empty;
        var runtime = this.TargetRuntimeFilter;
        var architecture = this.TargetArchitectureFilter;
        var selected = this.WindowInfos.CurrentItem;
        var hasFilter = term.Length > 0 || runtime != TargetRuntime.None || !string.IsNullOrEmpty(architecture);
        this.SetValue(TargetFilterTextPropertyKey, term);
        this.SetValue(HasTargetFilterPropertyKey, hasFilter);

        var hasPid = int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var pid);
        this.WindowInfos.Filter = !hasFilter ? null : item =>
        {
            var window = (WindowInfo)item;
            try
            {
                var matchesText = term.Length == 0
                    || (hasPid && window.OwningProcessId == pid)
                    || window.ProcessName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || window.WindowTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                return matchesText
                    && (runtime == TargetRuntime.None || window.OwningProcessInfo?.TargetRuntimes.HasFlag(runtime) == true)
                    && (string.IsNullOrEmpty(architecture) || window.OwningProcessInfo?.TargetArchitecture == architecture);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // A window may have disappeared since the last refresh.
                return false;
            }
        };

        // Refreshing a WPF view can select the first remaining row on its own.
        // Keep the user's selection only while the same target is still visible.
        if (selected is null || !this.WindowInfos.Contains(selected))
        {
            this.WindowInfos.MoveCurrentToPosition(-1);
        }
        else
        {
            this.WindowInfos.MoveCurrentTo(selected);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void HandleFindTarget(object sender, ExecutedRoutedEventArgs e)
    {
        if (this.IsAttaching)
        {
            return;
        }

        this.windowsComboBox.IsDropDownOpen = true;
        // ComboBox queues focus navigation when its popup opens. Focus the search
        // field after that navigation, once the popup has its own presentation source.
        this.Dispatcher.InvokeAsync(() =>
        {
            if (this.windowsComboBox.IsDropDownOpen
                && this.windowsComboBox.Template.FindName("TargetSearchBox", this.windowsComboBox) is TextBox search)
            {
                search.Focus();
                search.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void HandleChooserLoaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= this.HandleChooserLoaded;
        SystemEvents.UserPreferenceChanged += this.HandleSystemPreferenceChanged;
        if (this.WindowState == WindowState.Minimized)
        {
            this.StateChanged += this.HandleFirstRestore;
            return;
        }

        this.FitChooserToWorkArea();
    }

    private void HandleSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // SystemEvents can run on another thread. Resolve the current system palette on the UI Dispatcher.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle
            && !this.Dispatcher.HasShutdownStarted)
        {
            this.Dispatcher.InvokeAsync(() => ThemeManager.Current.ApplyTheme(ThemeMode.Auto, this));
        }
    }

    private void HandleFirstRestore(object? sender, EventArgs e)
    {
        if (this.WindowState != WindowState.Minimized)
        {
            this.StateChanged -= this.HandleFirstRestore;
            this.FitChooserToWorkArea();
        }
    }

    private void FitChooserToWorkArea()
    {
        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (target is null)
        {
            return;
        }

        // Loaded windows with automatic startup placement can still have NaN Left/Top.
        // Translate their actual desktop origin using the HWND's current DPI.
        var origin = this.PointToScreen(default);
        this.ApplyWorkArea(SnoopWindowUtils.GetWindowWorkArea(this), target.TransformFromDevice.Transform(origin));
    }

    private void ApplyWorkArea(Rect work, Point origin)
    {
        var compact = work.Width < this.singleRowMinimumWidth;
        this.BrandLabel.Visibility = this.WindowActionsDivider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(this.TargetActions, compact ? 1 : 0);
        Grid.SetColumn(this.TargetActions, compact ? 0 : 2);
        Grid.SetColumnSpan(this.TargetActions, compact ? 4 : 1);
        this.TargetActions.Margin = compact ? new Thickness(0, 0, 0, 6) : new Thickness(3, 0, 0, 0);

        this.MinWidth = Math.Min(this.singleRowMinimumWidth, work.Width);
        this.Width = Math.Min(this.preferredWidth, work.Width);
        this.ChooserSurface.Measure(new Size(this.Width, double.PositiveInfinity));
        this.Height = this.ChooserSurface.DesiredSize.Height;
        this.Left = Math.Max(work.Left, Math.Min(origin.X, work.Right - this.Width));
        this.Top = Math.Max(work.Top, Math.Min(origin.Y, work.Bottom - this.Height));
    }

    public void Refresh() => this.Refresh(selectSingleTarget: true);

    private void Refresh(bool selectSingleTarget)
    {
        if (this.IsAttaching)
        {
            return;
        }

        if (this.selectSingleTargetAfterRefresh is { } pendingSelection)
        {
            this.selectSingleTargetAfterRefresh = pendingSelection && selectSingleTarget;
            return;
        }

        this.selectSingleTargetAfterRefresh = selectSingleTarget;
        this.windowInfos.Clear();

        this.RunInDispatcherAsync(() =>
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                using var currentProcess = Process.GetCurrentProcess();
                var currentProcessId = currentProcess.Id;

                var processesAndWindows = NativeMethods.GetProcessesAndWindows();

                var processToWindowInfo = new ConcurrentDictionary<int, WindowInfo>();

                foreach (var processId in processesAndWindows.Keys)
                {
                    if (processId == currentProcessId
                        || processesAndWindows.TryGetValue(processId, out var windows) == false)
                    {
                        continue;
                    }

                    var windowInfoCollection = windows
                        .OrderByDescending(NativeMethods.IsWindowVisible)
                        .Select(h => WindowInfo.GetWindowInfo(h));

                    foreach (var windowInfo in windowInfoCollection)
                    {
                        if (processToWindowInfo.ContainsKey(processId)
                            || windowInfo.IsValidProcess == false)
                        {
                            break;
                        }

                        while (processToWindowInfo.TryAdd(processId, windowInfo) == false)
                        {
                        }
                    }
                }

                foreach (var windowInfo in processToWindowInfo.Values)
                {
                    this.windowInfos.Add(windowInfo);
                }

                if (this.selectSingleTargetAfterRefresh == false)
                {
                    this.WindowInfos.MoveCurrentToPosition(-1);
                }
                else if (!this.HasTargetFilter && this.windowInfos.Count is 1)
                {
                    this.WindowInfos.MoveCurrentTo(this.windowInfos[0]);
                }
            }
            finally
            {
                this.selectSingleTargetAfterRefresh = null;
                Mouse.OverrideCursor = null;
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        this.keyboardHook = new LowLevelKeyboardHook(PresentationSource.FromVisual(this)!);
        this.keyboardHook.LowLevelKeyUp += this.KeyboardHook_LowLevelKeyUp;
        this.keyboardHook.Start();

        // load the window placement details from the user settings.
        SnoopWindowUtils.LoadWindowPlacement(this, Settings.Default.AppChooserWindowPlacement);
        if (this.Width >= this.singleRowMinimumWidth)
        {
            this.preferredWidth = this.Width;
        }
    }

    private void KeyboardHook_LowLevelKeyUp(object sender, KeyEventArgs e)
    {
        if (this.CanStartAttach && Settings.Default?.GlobalHotKey?.Matches(null, e) == true)
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            // Defer discovery and attachment until the low-level hook has returned.
            this.Dispatcher.InvokeAsync(() => this.AttachToForegroundWindow(foregroundWindow));
        }
    }

    private void AttachToForegroundWindow(IntPtr foregroundWindow)
    {
        if (this.CanStartAttach && foregroundWindow != IntPtr.Zero)
        {
            var windowInfo = WindowInfo.GetWindowInfo(foregroundWindow);

            if (windowInfo.IsValidProcess)
            {
                this.AttachWindow(windowInfo, SnoopStartTarget.SnoopUI);
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (this.IsAttaching)
        {
            this.closeAfterAttach = true;
            base.OnClosing(e);
            e.Cancel = true;
            return;
        }

        // persist the window placement details to the user settings.
        SnoopWindowUtils.SaveWindowPlacement(this, wp => Settings.Default.AppChooserWindowPlacement = wp);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        this.isClosed = true;
        this.launcherReturnWait?.Unregister(null);
        this.launcherReturnEvent?.Dispose();
        SystemEvents.UserPreferenceChanged -= this.HandleSystemPreferenceChanged;
        this.keyboardHook?.Stop();

        base.OnClosed(e);
    }

    private void HandleCanInspectOrMagnifyCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !this.IsAttaching && this.WindowInfos.CurrentItem is not null;
        e.Handled = true;
    }

    private void HandleCanUseLauncherCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !this.IsAttaching;
        e.Handled = true;
    }

    private void HandleInspectCommand(object sender, ExecutedRoutedEventArgs e)
        => this.AttachToSelection(SnoopStartTarget.SnoopUI);

    private void HandleMagnifyCommand(object sender, ExecutedRoutedEventArgs e)
        => this.AttachToSelection(SnoopStartTarget.Zoomer);

    private void AttachToSelection(SnoopStartTarget startTarget)
    {
        if (this.WindowInfos.CurrentItem is WindowInfo window)
        {
            this.AttachWindow(window, startTarget);
        }
    }

    internal async void AttachWindow(WindowInfo window, SnoopStartTarget startTarget)
    {
        if (!this.CanStartAttach)
        {
            return;
        }

        var target = window.Description;
        this.windowsComboBox.IsDropDownOpen = false;
        this.SetValue(IsAttachingPropertyKey, true);
        CommandManager.InvalidateRequerySuggested();

        var returnToken = startTarget == SnoopStartTarget.SnoopUI ? this.GetLauncherReturnToken() : Guid.Empty;
        var result = await window.AttachAsync(startTarget, returnToken).ConfigureAwait(false);
        _ = this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => this.CompleteAttach(startTarget, target, result)));
    }

    private Guid GetLauncherReturnToken()
    {
        if (this.launcherReturnEvent is null)
        {
            var token = Guid.NewGuid();
            try
            {
                this.launcherReturnEvent = new EventWaitHandle(false, EventResetMode.AutoReset, TransientSettingsData.GetLauncherReturnEventName(token));
                this.launcherReturnWait = ThreadPool.RegisterWaitForSingleObject(
                    this.launcherReturnEvent, this.QueueReturnToTargetPicker, null, Timeout.Infinite, executeOnlyOnce: false);
                this.launcherReturnToken = token;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
            {
                this.launcherReturnEvent?.Dispose();
                this.launcherReturnEvent = null;
                LogHelper.WriteWarning(ex);
            }
        }

        return this.launcherReturnToken;
    }

    private void QueueReturnToTargetPicker(object? state, bool timedOut)
    {
        if (!this.Dispatcher.HasShutdownStarted)
        {
            _ = this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                this.returnToPickerPending = true;
                this.TryReturnToTargetPicker();
            }));
        }
    }

    private void TryReturnToTargetPicker()
    {
        if (!this.returnToPickerPending || !this.CanStartAttach)
        {
            return;
        }

        this.returnToPickerPending = false;
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }

        if (!this.IsVisible)
        {
            this.Show();
        }

        this.Activate();
        this.WindowInfos.MoveCurrentToPosition(-1);
        CommandManager.InvalidateRequerySuggested();
        WindowInfo.ClearCachedWindowHandleInfo();
        this.Refresh(selectSingleTarget: false);
        ApplicationCommands.Find.Execute(null, this);
    }

    private void CompleteAttach(SnoopStartTarget startTarget, string target, AttachResult result)
    {
        if (this.isClosed)
        {
            return;
        }

        if (!result.Success)
        {
            this.WindowInfos.MoveCurrentToPosition(-1);
        }

        this.SetValue(IsAttachingPropertyKey, false);
        CommandManager.InvalidateRequerySuggested();

        if (this.closeAfterAttach)
        {
            this.closeAfterAttach = false;
            this.Close();
            return;
        }

        if (result.Success)
        {
            this.TryReturnToTargetPicker();
            return;
        }

        var failureTitleKey = startTarget == SnoopStartTarget.Zoomer ? "Chooser.MagnifyFailed" : "Chooser.InspectFailed";
        var failure = new AttachFailureDialog(failureTitleKey, target, result.AttachException);
        if (this.IsVisible)
        {
            failure.Owner = this;
        }

        if (failure.ShowDialog() == true)
        {
            WindowInfo.ClearCachedWindowHandleInfo();
            this.Refresh(selectSingleTarget: false);
            ApplicationCommands.Find.Execute(null, this);
        }

        this.TryReturnToTargetPicker();
    }

    private void HandleRefreshCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (this.IsAttaching)
        {
            return;
        }

        // clear out cached process info to make the force refresh do the process check over again.
        WindowInfo.ClearCachedWindowHandleInfo();
        this.Refresh();
    }

    private void HandleSettingsCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (!this.IsAttaching)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
            this.TryReturnToTargetPicker();
        }
    }

    private void HandleMinimizeCommand(object sender, ExecutedRoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void HandleCloseCommand(object sender, ExecutedRoutedEventArgs e)
    {
        this.Close();
    }

    private void HandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.DragMove();
        this.FitChooserToWorkArea();
    }

    private void HandleWindowsComboBox_OnDropDownOpened(object sender, EventArgs e)
    {
        if (this.selectSingleTargetAfterRefresh is not null || this.windowInfos.Any())
        {
            return;
        }

        RefreshCommand.Execute(null, this);
    }

    /// <summary>
    /// Catch rmb from TextBlock of combobox drop down list to implement sorting.
    /// </summary>
    /// <param name="sender">Sender object</param>
    /// <param name="e">Arguments</param>
    private void HandleDropDownColumn_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        var gridColumn = (int)textBlock.GetValue(Grid.ColumnProperty);
        this.SetValue(SortColumnPropertyKey, gridColumn);

        var propertyName = string.Empty;
        switch (gridColumn)
        {
            //by pid
            case 1:
                propertyName = nameof(WindowInfo.OwningProcessId);
                break;
            //by process name
            case 2:
                propertyName = nameof(WindowInfo.ProcessName);
                break;
            //by window name
            case 3:
                propertyName = nameof(WindowInfo.WindowTitle);
                break;
        }

        //read current sort order
        var sortDirection = this.WindowInfos.SortDescriptions.FirstOrDefault().Direction;

        //1 - read current target property for sorting.
        //2 - if current property not changed from last click - invert direction
        //3 - if property changed - direction does not change
        var currentSortProperty = this.WindowInfos.SortDescriptions.FirstOrDefault().PropertyName;
        if (string.Equals(currentSortProperty, propertyName))
        {
            //toggle sort direction
            sortDirection = sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }

        this.WindowInfos.SortDescriptions.Clear();
        this.WindowInfos.SortDescriptions.Add(new SortDescription(propertyName, sortDirection));
    }
}

public class AttachResult
{
    public AttachResult()
    {
        this.Success = true;
    }

    public AttachResult(Exception attachException)
    {
        this.Success = false;

        this.AttachException = attachException;
    }

    public bool Success { get; }

    public Exception? AttachException { get; }
}
