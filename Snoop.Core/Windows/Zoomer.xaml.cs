// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Windows;

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Infrastructure;

public sealed partial class Zoomer
{
    private readonly TranslateTransform translation = new();
    private readonly ScaleTransform zoom = new();
    private readonly TransformGroup transform = new();
    private Point downPoint;
    private object? target;
    private Visual? targetVisual;
    private VisualTree3DView? visualTree3DView;
    private UIElement? waitingForLayout;
    private bool isThreeDimensional;

    private const double ZoomFactor = 1.1;

    static Zoomer()
    {
        ResetCommand = new RoutedCommand("Reset", typeof(Zoomer));
        ZoomInCommand = new RoutedCommand("ZoomIn", typeof(Zoomer));
        ZoomOutCommand = new RoutedCommand("ZoomOut", typeof(Zoomer));
        PanLeftCommand = new RoutedCommand("PanLeft", typeof(Zoomer));
        PanRightCommand = new RoutedCommand("PanRight", typeof(Zoomer));
        PanUpCommand = new RoutedCommand("PanUp", typeof(Zoomer));
        PanDownCommand = new RoutedCommand("PanDown", typeof(Zoomer));
        SwitchTo2DCommand = new RoutedCommand("SwitchTo2D", typeof(Zoomer));
        SwitchTo3DCommand = new RoutedCommand("SwitchTo3D", typeof(Zoomer));

        ResetCommand.InputGestures.Add(new MouseGesture(MouseAction.LeftDoubleClick));
        ResetCommand.InputGestures.Add(new KeyGesture(Key.F5));
        ZoomInCommand.InputGestures.Add(new KeyGesture(Key.OemPlus));
        ZoomInCommand.InputGestures.Add(new KeyGesture(Key.Up, ModifierKeys.Control));
        ZoomOutCommand.InputGestures.Add(new KeyGesture(Key.OemMinus));
        ZoomOutCommand.InputGestures.Add(new KeyGesture(Key.Down, ModifierKeys.Control));
        PanLeftCommand.InputGestures.Add(new KeyGesture(Key.Left));
        PanRightCommand.InputGestures.Add(new KeyGesture(Key.Right));
        PanUpCommand.InputGestures.Add(new KeyGesture(Key.Up));
        PanDownCommand.InputGestures.Add(new KeyGesture(Key.Down));
        SwitchTo2DCommand.InputGestures.Add(new KeyGesture(Key.F2));
        SwitchTo3DCommand.InputGestures.Add(new KeyGesture(Key.F3));
    }

    public Zoomer()
    {
        this.CommandBindings.Add(new CommandBinding(ResetCommand, this.HandleReset, this.CanReset));
        this.CommandBindings.Add(new CommandBinding(ZoomInCommand, this.HandleZoomIn));
        this.CommandBindings.Add(new CommandBinding(ZoomOutCommand, this.HandleZoomOut));
        this.CommandBindings.Add(new CommandBinding(PanLeftCommand, this.HandlePanLeft));
        this.CommandBindings.Add(new CommandBinding(PanRightCommand, this.HandlePanRight));
        this.CommandBindings.Add(new CommandBinding(PanUpCommand, this.HandlePanUp));
        this.CommandBindings.Add(new CommandBinding(PanDownCommand, this.HandlePanDown));
        this.CommandBindings.Add(new CommandBinding(SwitchTo2DCommand, this.HandleSwitchTo2D));
        this.CommandBindings.Add(new CommandBinding(SwitchTo3DCommand, this.HandleSwitchTo3D, this.CanSwitchTo3D));

        this.InitializeComponent();

        this.transform.Children.Add(this.zoom);
        this.transform.Children.Add(this.translation);

        this.Viewbox.RenderTransform = this.transform;
    }

    protected override void Load(object root)
    {
        if (root is Application application
            && application.CheckAccess())
        {
            // try to use the application's main window (if visible) as the root
            if (application.MainWindow is { } main && main.CheckAccess() && main.IsVisible && !main.IsPartOfSnoopVisualTree())
            {
                root = main;
            }
            else
            {
                // else search for the first visible window in the list of the application's windows
                foreach (Window? appWindow in application.Windows)
                {
                    if (appWindow is null)
                    {
                        continue;
                    }

                    if (appWindow.CheckAccess()
                        && appWindow.IsVisible && !appWindow.IsPartOfSnoopVisualTree())
                    {
                        root = appWindow;
                        break;
                    }
                }
            }
        }

        this.Target = root;
    }

    public override object? Target
    {
        get => this.target;

        set
        {
            this.StopWaitingForLayout();
            this.target = value;
            if (value is not Visual)
            {
                this.isThreeDimensional = false;
            }

            this.UpdateProjection();
        }
    }

    public static readonly RoutedCommand ResetCommand;
    public static readonly RoutedCommand ZoomInCommand;
    public static readonly RoutedCommand ZoomOutCommand;
    public static readonly RoutedCommand PanLeftCommand;
    public static readonly RoutedCommand PanRightCommand;
    public static readonly RoutedCommand PanUpCommand;
    public static readonly RoutedCommand PanDownCommand;
    public static readonly RoutedCommand SwitchTo2DCommand;
    public static readonly RoutedCommand SwitchTo3DCommand;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // load the window placement details from the user settings.
        SnoopWindowUtils.LoadWindowPlacement(this, Settings.Default.ZoomerWindowPlacement);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // persist the window placement details to the user settings.
        SnoopWindowUtils.SaveWindowPlacement(this, wp => Settings.Default.ZoomerWindowPlacement = wp);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        this.StopWaitingForLayout();
        this.Viewbox.Child = null;
        this.target = null;
        this.targetVisual = null;
        this.visualTree3DView = null;
        base.OnClosed(e);
    }

    private void HandleReset(object sender, ExecutedRoutedEventArgs args)
    {
        this.translation.X = this.translation.Y = 0;
        this.zoom.ScaleX = this.zoom.ScaleY = 1;
        this.ZScaleSlider.Value = 0;
        this.visualTree3DView?.Reset();
    }

    private void CanReset(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = true;
        args.Handled = true;
    }

    private void HandleZoomIn(object sender, ExecutedRoutedEventArgs args)
    {
        var offset = new Point(this.Viewbox.ActualWidth / 2, this.Viewbox.ActualHeight / 2);
        this.Zoom(ZoomFactor, offset);
    }

    private void HandleZoomOut(object sender, ExecutedRoutedEventArgs args)
    {
        var offset = new Point(this.Viewbox.ActualWidth / 2, this.Viewbox.ActualHeight / 2);
        this.Zoom(1 / ZoomFactor, offset);
    }

    private void HandlePanLeft(object sender, ExecutedRoutedEventArgs args)
    {
        this.translation.X -= 5;
    }

    private void HandlePanRight(object sender, ExecutedRoutedEventArgs args)
    {
        this.translation.X += 5;
    }

    private void HandlePanUp(object sender, ExecutedRoutedEventArgs args)
    {
        this.translation.Y -= 5;
    }

    private void HandlePanDown(object sender, ExecutedRoutedEventArgs args)
    {
        this.translation.Y += 5;
    }

    private void HandleSwitchTo2D(object sender, ExecutedRoutedEventArgs args)
    {
        if (this.isThreeDimensional)
        {
            this.isThreeDimensional = false;
            this.UpdateProjection();
        }
    }

    private void HandleSwitchTo3D(object sender, ExecutedRoutedEventArgs args)
    {
        if (!this.isThreeDimensional && this.targetVisual is not null)
        {
            this.isThreeDimensional = true;
            this.UpdateProjection();
        }
    }

    private void UpdateProjection()
    {
        this.targetVisual = VisualCaptureUtil.GetCaptureVisual(this.target as Visual);
        var bounds = this.targetVisual is not null ? VisualTreeHelper.GetDescendantBounds(this.targetVisual) : Rect.Empty;
        var waiting = this.target is Visual && (this.targetVisual is null || this.targetVisual is UIElement { IsVisible: false }
                                                || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0);
        if (waiting && this.waitingForLayout is null && this.target is UIElement pending)
        {
            // A newly shown window can acquire its client and drawing after Inspect() returns.
            this.waitingForLayout = pending;
            pending.LayoutUpdated += this.OnTargetLayoutUpdated;
        }
        else if (!waiting)
        {
            this.StopWaitingForLayout();
        }

        UIElement? projection = null;
        this.visualTree3DView = null;
        if (!waiting && this.isThreeDimensional && this.targetVisual is not null)
        {
            var dpi = int.Parse((string)((ComboBoxItem)this.dpiBox.SelectedItem).Content, CultureInfo.InvariantCulture);
            this.visualTree3DView = new VisualTree3DView(this.targetVisual, dpi) { ZScale = Math.Pow(10, this.ZScaleSlider.Value) };
            if (this.visualTree3DView.Camera is not null)
            {
                projection = this.visualTree3DView;
            }
        }
        else if (!waiting)
        {
            projection = ZoomerUtilities.CreateIfPossible(this.targetVisual ?? this.target);
            if (projection is Panel { Children.Count: 0 })
            {
                projection = null;
            }
        }

        this.Viewbox.Child = projection;
        this.Mode2D.IsChecked = !this.isThreeDimensional;
        this.Mode3D.IsChecked = this.isThreeDimensional;
        this.ThreeDViewControls.Visibility = this.isThreeDimensional ? Visibility.Visible : Visibility.Collapsed;
        this.EmptyState.Visibility = projection is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTargetLayoutUpdated(object? sender, EventArgs e) => this.UpdateProjection();

    private void StopWaitingForLayout()
    {
        if (this.waitingForLayout is not null)
        {
            this.waitingForLayout.LayoutUpdated -= this.OnTargetLayoutUpdated;
            this.waitingForLayout = null;
        }
    }

    private void CanSwitchTo3D(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = this.targetVisual is not null && this.Viewbox?.Child is not null;
        args.Handled = true;
    }

    private void Content_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && this.Viewbox.Child is not null)
        {
            this.DocumentRoot.Focus();
            this.downPoint = e.GetPosition(this.DocumentRoot);
            this.DocumentRoot.CaptureMouse();
        }
    }

    private void Content_MouseMove(object sender, MouseEventArgs e)
    {
        if (this.DocumentRoot.IsMouseCaptured)
        {
            var delta = e.GetPosition(this.DocumentRoot) - this.downPoint;
            this.translation.X += delta.X;
            this.translation.Y += delta.Y;

            this.downPoint = e.GetPosition(this.DocumentRoot);
        }
    }

    private void Content_MouseUp(object sender, MouseEventArgs e)
    {
        this.DocumentRoot.ReleaseMouseCapture();
    }

    private void Content_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var newZoom = Math.Pow(ZoomFactor, e.Delta / 120.0);
        var offset = e.GetPosition(this.Viewbox);
        this.Zoom(newZoom, offset);
    }

    private void ZScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (this.visualTree3DView is not null)
        {
            this.visualTree3DView.ZScale = Math.Pow(10, e.NewValue);
        }
    }

    private void Zoom(double newZoom, Point offset)
    {
        var v = new Vector((1 - newZoom) * offset.X, (1 - newZoom) * offset.Y);

        var translationVector = v * this.transform.Value;
        this.translation.X += translationVector.X;
        this.translation.Y += translationVector.Y;

        this.zoom.ScaleX *= newZoom;
        this.zoom.ScaleY *= newZoom;
    }

    private void DpiBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.visualTree3DView is not null)
        {
            this.UpdateProjection();
        }
    }

    private void FixTextFormattingModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.target is Visual selected)
        {
            TextOptions.SetTextFormattingMode(selected, TextFormattingMode.Ideal);
        }
    }
}

[ValueConversion(typeof(double), typeof(SolidColorBrush))]
[ValueConversion(typeof(float), typeof(SolidColorBrush))]
public sealed class DoubleToWhitenessConverter : IValueConverter
{
    public static readonly DoubleToWhitenessConverter Default = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var val = (float)(double)value;
        var c = new Color
        {
            ScR = val,
            ScG = val,
            ScB = val,
            ScA = 1
        };

        return new SolidColorBrush(c);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}