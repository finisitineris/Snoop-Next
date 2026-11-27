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
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Snoop.Core;
using Snoop.Infrastructure;
using Snoop.Next.Localization;
using Snoop.Views;
using Snoop.Windows;

public partial class AppChooser
{
    public static readonly RoutedCommand InspectCommand = new(nameof(InspectCommand), typeof(AppChooser));
    public static readonly RoutedCommand RefreshCommand = new(nameof(RefreshCommand), typeof(AppChooser));
    public static readonly RoutedCommand MagnifyCommand = new(nameof(MagnifyCommand), typeof(AppChooser));
    public static readonly RoutedCommand ClearTargetSearchCommand = new(nameof(ClearTargetSearchCommand), typeof(AppChooser));
    public static readonly RoutedCommand SettingsCommand = new(nameof(SettingsCommand), typeof(AppChooser));
    public static readonly RoutedCommand MinimizeCommand = new(nameof(MinimizeCommand), typeof(AppChooser));

    private static readonly DependencyPropertyKey SortColumnPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SortColumn), typeof(int), typeof(AppChooser), new PropertyMetadata(1));

    public static readonly DependencyProperty SortColumnProperty = SortColumnPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TargetSearchProperty = DependencyProperty.Register(
        nameof(TargetSearch), typeof(string), typeof(AppChooser),
        new PropertyMetadata(string.Empty, (d, _) => ((AppChooser)d).OnTargetSearchChanged()));

    private static readonly DependencyPropertyKey HasTargetFilterPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasTargetFilter), typeof(bool), typeof(AppChooser), new PropertyMetadata(false));

    public static readonly DependencyProperty HasTargetFilterProperty = HasTargetFilterPropertyKey.DependencyProperty;

    private readonly ObservableCollection<WindowInfo> windowInfos = new();
    private readonly double singleRowMinimumWidth;
    private double preferredWidth;

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

        this.CommandBindings.Add(new CommandBinding(RefreshCommand, this.HandleRefreshCommand));
        this.CommandBindings.Add(new CommandBinding(InspectCommand, this.HandleInspectCommand, this.HandleCanInspectOrMagnifyCommand));
        this.CommandBindings.Add(new CommandBinding(MagnifyCommand, this.HandleMagnifyCommand, this.HandleCanInspectOrMagnifyCommand));
        this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, this.HandleFindTarget));
        this.CommandBindings.Add(new CommandBinding(ClearTargetSearchCommand, (_, _) => this.TargetSearch = string.Empty,
                                                   (_, e) => e.CanExecute = !string.IsNullOrEmpty(this.TargetSearch)));
        this.CommandBindings.Add(new CommandBinding(SettingsCommand, this.HandleSettingsCommand));
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

    public bool HasTargetFilter => (bool)this.GetValue(HasTargetFilterProperty);

    private void OnTargetSearchChanged()
    {
        var term = this.TargetSearch?.Trim() ?? string.Empty;
        var selected = this.WindowInfos.CurrentItem;
        this.SetValue(HasTargetFilterPropertyKey, term.Length > 0);

        var hasPid = int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var pid);
        this.WindowInfos.Filter = term.Length == 0 ? null : item =>
        {
            var window = (WindowInfo)item;
            if (hasPid && window.OwningProcessId == pid)
            {
                return true;
            }

            try
            {
                return window.ProcessName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || window.WindowTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
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

    public void Refresh()
    {
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

                if (this.windowInfos.Count is 1 && this.WindowInfos.Contains(this.windowInfos[0]))
                {
                    this.WindowInfos.MoveCurrentTo(this.windowInfos[0]);
                }
            }
            finally
            {
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
        if (Settings.Default?.GlobalHotKey?.Matches(null, e) == true
            && !this.OwnedWindows.OfType<SettingsWindow>().Any(window => window.IsActive))
        {
            var thread = new Thread(AttachToForegroundWindow)
            {
                Name = "Snoop_AttachToForegroundWindow_Thread"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    private static void AttachToForegroundWindow()
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();

        if (foregroundWindow != IntPtr.Zero)
        {
            var windowInfo = WindowInfo.GetWindowInfo(foregroundWindow);

            if (windowInfo.IsValidProcess)
            {
                WindowFinder.AttachSnoop(windowInfo);
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // persist the window placement details to the user settings.
        SnoopWindowUtils.SaveWindowPlacement(this, wp => Settings.Default.AppChooserWindowPlacement = wp);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= this.HandleSystemPreferenceChanged;
        this.keyboardHook?.Stop();

        base.OnClosed(e);
    }

    private void HandleCanInspectOrMagnifyCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        if (this.WindowInfos.CurrentItem is not null)
        {
            e.CanExecute = true;
        }

        e.Handled = true;
    }

    private void HandleInspectCommand(object sender, ExecutedRoutedEventArgs e)
    {
        var window = (WindowInfo?)this.WindowInfos.CurrentItem;

        if (window is null)
        {
            return;
        }

        var result = window.OwningProcessInfo?.Snoop(window.HWnd);

        if (result?.Success == false)
        {
            ErrorDialog.ShowDialog(result.AttachException, LocalizationManager.Default["Chooser.InspectFailed"],
                                   LocalizationManager.Default.Format("Chooser.AttachFailureDetail", window.Description), true);
        }
    }

    private void HandleMagnifyCommand(object sender, ExecutedRoutedEventArgs e)
    {
        var window = (WindowInfo?)this.WindowInfos.CurrentItem;

        if (window is null)
        {
            return;
        }

        var result = window.OwningProcessInfo?.Magnify(window.HWnd);

        if (result?.Success == false)
        {
            ErrorDialog.ShowDialog(result.AttachException, LocalizationManager.Default["Chooser.MagnifyFailed"],
                                   LocalizationManager.Default.Format("Chooser.AttachFailureDetail", window.Description), true);
        }
    }

    private void HandleRefreshCommand(object sender, ExecutedRoutedEventArgs e)
    {
        // clear out cached process info to make the force refresh do the process check over again.
        WindowInfo.ClearCachedWindowHandleInfo();
        this.Refresh();
    }

    private void HandleSettingsCommand(object sender, ExecutedRoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
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
        if (this.windowInfos.Any())
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
