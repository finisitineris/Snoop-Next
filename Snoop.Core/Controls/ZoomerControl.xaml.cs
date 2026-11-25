// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Snoop.AttachedProperties;
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

    internal event Action<Visual, UIElement>? ElementPicked;

    internal bool IgnoreHitTestVisibility { get; set; } = true;

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
        this.CancelPointerGesture();
        var previousImage = this.contextImage;

        if (this.pooSniffer is null)
        {
            this.pooSniffer = this.TryFindResource("poo_sniffer_xpr") as Brush;
        }

        this.Cursor = ReferenceEquals(this.Target, this.pooSniffer) ? null : Cursors.SizeAll;

        this.SetSelectionOutline(Rect.Empty);
        this.RefreshPreview(replaceFallback: true);
        if (previousImage is null || !ReferenceEquals(previousImage, this.contextImage))
        {
            this.ResetZoomAndTranslation();
        }
    }
    #endregion

    internal void SetScopeTarget(object? target)
    {
        this.CancelPointerGesture();
        this.scopeTarget = target;
        this.ResetZoomAndTranslation();
        this.RefreshPreview(replaceFallback: true);
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        this.LayoutUpdated += this.HandleLayoutUpdated;
        this.RefreshPreview(replaceFallback: true);
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        this.CancelPointerGesture();
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
        if (this.Target is UIElement selected && this.TryGetPreviewContext(selected, out var scene, out var context))
        {
            var toBrush = GetContextTransform(context);
            var crop = Rect.Transform(new Rect(context.RenderSize), toBrush);
            if (HasArea(crop))
            {
                // A selected Window occupies the whole projected client. It remains the
                // logical selection and scope for commands and hit testing.
                this.UpdateContextImage(ReferenceEquals(selected, scene) ? context : selected, context, crop, toBrush);
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

    private static Matrix GetContextTransform(Visual context)
    {
        var transform = VisualTreeHelper.GetTransform(context)?.Value ?? Matrix.Identity;
        var offset = VisualTreeHelper.GetOffset(context);
        transform.Translate(offset.X, offset.Y);
        return transform;
    }

    private bool TryGetPreviewContext(UIElement selected, [NotNullWhen(true)] out UIElement? scene, [NotNullWhen(true)] out UIElement? client)
    {
        scene = null;
        client = null;
        if (!this.CanPreview(selected)
            || PresentationSource.FromVisual(selected) is not { CompositionTarget: not null, RootVisual: UIElement root }
            || root is SnoopBaseWindow)
        {
            return false;
        }

        scene = this.scopeTarget is null ? root : this.scopeTarget as UIElement;
        if (scene is null || !this.CanPreview(scene) || !scene.IsVisible)
        {
            return false;
        }

        client = VisualCaptureUtil.GetCaptureVisual(scene) as UIElement;
        return client is not null
            && (ReferenceEquals(scene, selected) || ReferenceEquals(client, selected) || selected.IsDescendantOf(client));
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
        if (this.pointerButton is not null
            || !this.IsValidTarget
            || (e.OriginalSource is Visual source
                && (ReferenceEquals(source, this.ColorSlider) || this.ColorSlider.IsAncestorOf(source))))
        {
            return;
        }

        this.Focus();
        if (!this.DocumentRoot.CaptureMouse() || !this.DocumentRoot.IsMouseCaptured)
        {
            return;
        }

        this.BeginPointerGesture(e.GetPosition(this.DocumentRoot), e.ChangedButton, ReferenceEquals(e.OriginalSource, this.contextImage));
        e.Handled = true;
    }

    private void Content_MouseMove(object sender, MouseEventArgs e)
    {
        if (this.DocumentRoot.IsMouseCaptured && this.pointerButton is not null)
        {
            this.MovePointerGesture(e.GetPosition(this.DocumentRoot));
            e.Handled = true;
        }
    }

    private void Content_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (this.DocumentRoot.IsMouseCaptured && this.pointerButton == e.ChangedButton)
        {
            this.CompletePointerGesture(e.GetPosition(this.DocumentRoot), e.ChangedButton);
            e.Handled = true;
        }
    }

    private void Content_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!this.DocumentRoot.IsMouseCaptured)
        {
            this.CancelPointerGesture();
        }
    }

    private void BeginPointerGesture(Point position, MouseButton button, bool fromImage)
    {
        this.pointerButton = button;
        this.pointerStart = position;
        this.panAnchor = position;
        this.pressedImage = button == MouseButton.Left && fromImage ? this.contextImage : null;
        this.isPanning = false;
    }

    private void MovePointerGesture(Point position)
    {
        if (this.pointerButton is null || (!this.isPanning && !this.ExceedsDragThreshold(position)))
        {
            return;
        }

        this.isPanning = true;
        var delta = position - this.panAnchor;
        this.translation.X += delta.X;
        this.translation.Y += delta.Y;
        this.panAnchor = position;
        this.RefreshPreview();
    }

    private bool ExceedsDragThreshold(Point position)
        => Math.Abs(position.X - this.pointerStart.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - this.pointerStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void CompletePointerGesture(Point position, MouseButton button)
    {
        if (this.pointerButton != button)
        {
            return;
        }

        var shouldPick = this.pressedImage is not null
            && ReferenceEquals(this.pressedImage, this.contextImage)
            && !this.isPanning
            && !this.ExceedsDragThreshold(position);
        this.CancelPointerGesture();
        if (shouldPick)
        {
            this.SelectPreviewElementAt(position);
        }
    }

    private void CancelPointerGesture()
    {
        this.pointerButton = null;
        this.pressedImage = null;
        this.isPanning = false;
        if (this.DocumentRoot.IsMouseCaptured)
        {
            this.DocumentRoot.ReleaseMouseCapture();
        }
    }

    private void SelectPreviewElementAt(Point position)
    {
        if (this.contextImage is not { IsArrangeValid: true, Fill: VisualBrush { Visual: UIElement context } brush } image
            || !ReferenceEquals(this.Viewbox.Child, image)
            || this.Target is not UIElement selected
            || !this.TryGetPreviewContext(selected, out var scene, out var client)
            || !ReferenceEquals(client, context)
            || !new Rect(this.DocumentRoot.RenderSize).Contains(position))
        {
            return;
        }

        var imageBounds = new Rect(image.RenderSize);
        var toImage = image.TransformToAncestor(this.DocumentRoot).Inverse;
        if (!HasArea(imageBounds) || toImage is null || !toImage.TryTransform(position, out var imagePoint)
            || !imageBounds.Contains(imagePoint))
        {
            return;
        }

        var crop = brush.Viewbox;
        var toContext = GetContextTransform(context);
        var contextBounds = new Rect(context.RenderSize);
        if (!toContext.HasInverse || !HasArea(crop) || crop != Rect.Transform(contextBounds, toContext))
        {
            return;
        }

        var brushPoint = new Point(
            crop.X + ((imagePoint.X / imageBounds.Width) * crop.Width),
            crop.Y + ((imagePoint.Y / imageBounds.Height) * crop.Height));
        toContext.Invert();
        var contextPoint = toContext.Transform(brushPoint);
        if (!contextBounds.Contains(contextPoint))
        {
            return;
        }

        Visual? hit = null;
        VisualTreeHelper.HitTest(context, this.FilterPreviewHitTest, result =>
        {
            hit = result.VisualHit as Visual;
            return hit is null ? HitTestResultBehavior.Continue : HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(contextPoint));

        // Updating selection can change adorners, so it must happen after the hit test.
        if (hit is not null)
        {
            this.ElementPicked?.Invoke(hit, scene);
        }
    }

    private HitTestFilterBehavior FilterPreviewHitTest(DependencyObject candidate)
    {
        if (SnoopAttachedProperties.GetIsSnoopPart(candidate)
            || (candidate is Visual visual && VisualTreeHelper.GetOpacity(visual) == 0)
            || (candidate is UIElement element && (!element.IsVisible || (!this.IgnoreHitTestVisibility && !element.IsHitTestVisible))))
        {
            return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
        }

        return HitTestFilterBehavior.Continue;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (this.IsValidTarget)
        {
            e.Handled = true;
            this.pressedImage = null;

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
    private MouseButton? pointerButton;
    private Point pointerStart;
    private Point panAnchor;
    private Rectangle? pressedImage;
    private bool isPanning;

    private const double ZoomFactor = 1.1;
}
