// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Snoop.Infrastructure;
using Snoop.Windows;

public partial class ZoomerControl
{
    public ZoomerControl()
    {
        this.InitializeComponent();

        this.transform.Children.Add(this.zoom);
        this.transform.Children.Add(this.translation);

        this.Viewbox.RenderTransform = this.transform;
        this.Loaded += this.HandleLoaded;
        this.Unloaded += this.HandleUnloaded;
    }

    #region Target

    /// <summary>
    /// Gets or sets the Target property.
    /// </summary>
    public object? Target
    {
        get { return (object?)this.GetValue(TargetProperty); }
        set { this.SetValue(TargetProperty, value); }
    }

    /// <summary>
    /// Target Dependency Property
    /// </summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(
            nameof(Target),
            typeof(object),
            typeof(ZoomerControl),
            new FrameworkPropertyMetadata(
                null,
                OnTargetChanged));

    /// <summary>
    /// Handles changes to the Target property.
    /// </summary>
    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ZoomerControl)d).OnTargetChanged(e);
    }

    /// <summary>
    /// Provides derived classes an opportunity to handle changes to the Target property.
    /// </summary>
    protected virtual void OnTargetChanged(DependencyPropertyChangedEventArgs e)
    {
        this.ResetZoomAndTranslation();

        if (this.pooSniffer is null)
        {
            this.pooSniffer = this.TryFindResource("poo_sniffer_xpr") as Brush;
        }

        this.Cursor = ReferenceEquals(this.Target, this.pooSniffer) ? null : Cursors.SizeAll;

        this.SetSelectionOutline(Rect.Empty);
        this.RefreshPreview(replaceFallback: true);
    }
    #endregion

    internal void SetScopeTarget(object? target)
    {
        this.scopeTarget = target;
        this.RefreshPreview(replaceFallback: true);
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        this.LayoutUpdated += this.HandleLayoutUpdated;
        this.RefreshPreview(replaceFallback: true);
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        this.LayoutUpdated -= this.HandleLayoutUpdated;
        this.contextImage = null;
        this.Viewbox.Child = null;
        this.SetSelectionOutline(Rect.Empty);
    }

    private void HandleLayoutUpdated(object? sender, EventArgs e)
    {
        if (this.Target is UIElement)
        {
            this.RefreshPreview();
        }
    }

    private void RefreshPreview(bool replaceFallback = false)
    {
        if (this.Target is UIElement selected && this.GetPreviewContext(selected) is { } context)
        {
            var toBrush = VisualTreeHelper.GetTransform(context)?.Value ?? Matrix.Identity;
            var offset = VisualTreeHelper.GetOffset(context);
            toBrush.Translate(offset.X, offset.Y);
            var crop = Rect.Transform(new Rect(context.RenderSize), toBrush);
            if (HasArea(crop))
            {
                this.UpdateContextImage(selected, context, crop, toBrush);
                return;
            }
        }

        if (replaceFallback || this.contextImage is not null)
        {
            this.contextImage = null;
            this.Viewbox.Child = this.CreateFallbackPreview();
        }

        this.SetSelectionOutline(Rect.Empty);
    }

    private UIElement? GetPreviewContext(UIElement selected)
    {
        if (!this.CanPreview(selected)
            || PresentationSource.FromVisual(selected) is not { CompositionTarget: not null, RootVisual: UIElement root }
            || root is SnoopBaseWindow)
        {
            return null;
        }

        var context = this.scopeTarget is null ? root : this.scopeTarget as UIElement;
        if (context is null || !this.CanPreview(context) || !context.IsVisible
            || (!ReferenceEquals(context, selected) && !selected.IsDescendantOf(context)))
        {
            return null;
        }

        return context;
    }

    private bool CanPreview(Visual visual)
    {
        return visual.Dispatcher == this.Dispatcher
            && visual is not SnoopBaseWindow
            && !ReferenceEquals(visual, this)
            && !visual.IsAncestorOf(this)
            && !this.IsAncestorOf(visual);
    }

    private UIElement? CreateFallbackPreview()
    {
        if (this.Target is Visual visual && (!this.CanPreview(visual)
            || (visual is UIElement && PresentationSource.FromVisual(visual) is null)))
        {
            // The fallback brush auto-lays out detached controls, so do not give it disconnected elements.
            return null;
        }

        return ZoomerUtilities.CreateIfPossible(this.Target);
    }

    private void UpdateContextImage(UIElement selected, UIElement context, Rect crop, Matrix toBrush)
    {
        var image = this.contextImage;
        if (image?.Fill is not VisualBrush brush || !ReferenceEquals(brush.Visual, context))
        {
            // Keep the brush and outline in the same source coordinates, even for a transformed scope root.
            brush = new VisualBrush
            {
                AutoLayoutContent = false,
                Visual = context,
                Stretch = Stretch.Fill,
                Viewbox = crop,
                ViewboxUnits = BrushMappingMode.Absolute,
            };
            image = new Rectangle { Fill = brush, Width = crop.Width, Height = crop.Height };
            this.contextImage = image;
            this.Viewbox.Child = image;
        }
        else if (brush.Viewbox != crop)
        {
            brush.Viewbox = crop;
            image.Width = crop.Width;
            image.Height = crop.Height;
        }

        if (!selected.IsVisible || !image.IsArrangeValid)
        {
            this.SetSelectionOutline(Rect.Empty);
            return;
        }

        var bounds = GetSelectionBounds(selected, context, toBrush, out var visibleBounds);
        visibleBounds.Intersect(crop);
        if (!HasArea(visibleBounds))
        {
            this.SetSelectionOutline(Rect.Empty);
            return;
        }

        var toViewport = image.TransformToAncestor(this.DocumentRoot);
        var outline = Project(bounds);
        var clip = Project(visibleBounds);
        clip.Intersect(new Rect(this.DocumentRoot.RenderSize));
        if (!HasArea(outline) || !HasArea(clip))
        {
            this.SetSelectionOutline(Rect.Empty);
            return;
        }

        clip.Offset(-outline.X, -outline.Y);
        this.SetSelectionOutline(outline, clip);

        Rect Project(Rect rect)
        {
            rect.Offset(-crop.X, -crop.Y);
            rect.Scale(image.RenderSize.Width / crop.Width, image.RenderSize.Height / crop.Height);
            return toViewport.TransformBounds(rect);
        }
    }

    private static Rect GetSelectionBounds(UIElement selected, UIElement context, Matrix toBrush, out Rect visibleBounds)
    {
        var bounds = ToBrush(selected, new Rect(selected.RenderSize));
        visibleBounds = bounds;
        for (Visual? visual = selected; visual is not null; visual = VisualTreeHelper.GetParent(visual) as Visual)
        {
            if (VisualTreeHelper.GetClip(visual) is { } clip)
            {
                visibleBounds.Intersect(ToBrush(visual, clip.Bounds));
            }

            if (ReferenceEquals(visual, context) || visibleBounds.IsEmpty)
            {
                break;
            }
        }

        return bounds;

        Rect ToBrush(Visual visual, Rect rect)
        {
            if (rect.IsEmpty || ReferenceEquals(visual, context))
            {
                return Rect.Transform(rect, toBrush);
            }

            // Compose the transforms before boxing the corners, so rotations in the scope and its children can cancel.
            var toContext = visual.TransformToAncestor(context);
            if (!toContext.TryTransform(rect.TopLeft, out var topLeft)
                || !toContext.TryTransform(rect.TopRight, out var topRight)
                || !toContext.TryTransform(rect.BottomLeft, out var bottomLeft)
                || !toContext.TryTransform(rect.BottomRight, out var bottomRight))
            {
                return Rect.Empty;
            }

            var transformed = new Rect(toBrush.Transform(topLeft), toBrush.Transform(bottomRight));
            transformed.Union(toBrush.Transform(topRight));
            transformed.Union(toBrush.Transform(bottomLeft));
            return transformed;
        }
    }

    private static bool HasArea(Rect bounds)
    {
        return bounds.Width is > 0 and < double.PositiveInfinity
            && bounds.Height is > 0 and < double.PositiveInfinity
            && Math.Abs(bounds.X) < double.PositiveInfinity
            && Math.Abs(bounds.Y) < double.PositiveInfinity;
    }

    private void SetSelectionOutline(Rect bounds, Rect? clip = null)
    {
        if (bounds == this.outlineBounds && clip == this.outlineClip)
        {
            return;
        }

        this.outlineBounds = bounds;
        this.outlineClip = clip;
        if (bounds.IsEmpty)
        {
            this.PreviewSelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(this.PreviewSelectionOutline, bounds.X);
        Canvas.SetTop(this.PreviewSelectionOutline, bounds.Y);
        this.PreviewSelectionOutline.Width = bounds.Width;
        this.PreviewSelectionOutline.Height = bounds.Height;
        this.PreviewSelectionOutline.Clip = clip is { } visible && visible != new Rect(bounds.Size)
            ? new RectangleGeometry(visible)
            : null;
        this.PreviewSelectionOutline.Visibility = Visibility.Visible;
    }

    private void Content_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.Focus();
        this.downPoint = e.GetPosition(this.DocumentRoot);
        this.DocumentRoot.CaptureMouse();
    }

    private void Content_MouseMove(object sender, MouseEventArgs e)
    {
        if (this.IsValidTarget
            && this.DocumentRoot.IsMouseCaptured)
        {
            var delta = e.GetPosition(this.DocumentRoot) - this.downPoint;
            this.translation.X += delta.X;
            this.translation.Y += delta.Y;

            this.downPoint = e.GetPosition(this.DocumentRoot);
            this.RefreshPreview();
        }
    }

    private void Content_MouseUp(object sender, MouseEventArgs e)
    {
        this.DocumentRoot.ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (this.IsValidTarget)
        {
            e.Handled = true;

            var zoom = Math.Pow(ZoomFactor, e.Delta / 120.0);
            var offset = e.GetPosition(this.Viewbox);

            this.Zoom(zoom, offset);
        }
    }

    private void ResetZoomAndTranslation()
    {
        //Zoom(0, new Point(-this.translation.X, -this.translation.Y));
        //Zoom(1.0 / zoom.ScaleX, new Point());
        this.zoom.ScaleX = 1.0;
        this.zoom.ScaleY = 1.0;

        this.translation.X = 0.0;
        this.translation.Y = 0.0;
    }

    private void Zoom(double newZoom, Point offset)
    {
        var v = new Vector((1 - newZoom) * offset.X, (1 - newZoom) * offset.Y);

        var translationVector = v * this.transform.Value;
        this.translation.X += translationVector.X;
        this.translation.Y += translationVector.Y;

        this.zoom.ScaleX *= newZoom;
        this.zoom.ScaleY *= newZoom;
        this.RefreshPreview();
    }

    private bool IsValidTarget => this.Target is not null
                                  && ReferenceEquals(this.Target, this.pooSniffer) == false;

    private Brush? pooSniffer;
    private object? scopeTarget;
    private Rectangle? contextImage;
    private Rect outlineBounds = Rect.Empty;
    private Rect? outlineClip;

    private readonly TranslateTransform translation = new();
    private readonly ScaleTransform zoom = new();
    private readonly TransformGroup transform = new();
    private Point downPoint;

    private const double ZoomFactor = 1.1;
}
