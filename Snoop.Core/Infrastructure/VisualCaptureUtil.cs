// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Infrastructure;

using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class VisualCaptureUtil
{
    private const double BaseDpi = 96;

    public static void SaveVisual(Visual? visual, int dpi, string filename)
    {
        // sometimes RenderTargetBitmap doesn't render the Visual or doesn't render the Visual properly
        // below i am using the trick that jamie rodriguez posted on his blog
        // where he wraps the Visual inside of a VisualBrush and then renders it.
        // http://blogs.msdn.com/b/jaimer/archive/2009/07/03/rendertargetbitmap-tips.aspx

        if (GetCaptureVisual(visual) is not { } source)
        {
            return;
        }

        var renderTargetBitmap = RenderVisualWithHighQuality(source, dpi, dpi);

        SaveAsPng(renderTargetBitmap, filename);
    }

    public static VisualBrush? CreateVisualBrushSafe(Visual? visual)
    {
        return GetCaptureVisual(visual) is { } source ? new VisualBrush(source) : null;
    }

    public static bool IsSafeToVisualize(Visual? visual) => GetCaptureVisual(visual) is not null;

    internal static Visual? GetCaptureVisual(Visual? visual)
    {
        if (visual is not Window window)
        {
            return visual;
        }

        // A top-level Window has no framework parent. WPF can give it the inspector's
        // inheritance context through VisualBrush.Visual, invalidating the host's bindings
        // and styles. Its existing client visual already has a parent in the target tree.
        return PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: not null }
            && VisualTreeHelper.GetChildrenCount(window) > 0
            ? VisualTreeHelper.GetChild(window, 0) as Visual
            : null;
    }

    private static void SaveAsPng(RenderTargetBitmap bitmap, string filename)
    {
        var pngBitmapEncoder = new PngBitmapEncoder();
        pngBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmap));

        using (var fileStream = File.Create(filename))
        {
            pngBitmapEncoder.Save(fileStream);
        }
    }

    /// <summary>
    /// Draws <paramref name="visual"/> in smaller tiles using multiple <see cref="VisualBrush"/>.
    /// </summary>
    /// <remarks>
    /// This way we workaround a limitation in <see cref="VisualBrush"/> which causes poor quality for larger visuals.
    /// </remarks>
    public static RenderTargetBitmap RenderVisualWithHighQuality(Visual visual, int dpiX, int dpiY, PixelFormat? pixelFormat = null, Viewport3D? viewport3D = null)
    {
        visual = GetCaptureVisual(visual) ?? throw new InvalidOperationException("The window has no attached client visual.");
        var localBounds = visual is UIElement element ? new Rect(element.RenderSize) : VisualTreeHelper.GetDescendantBounds(visual);
        var sourceBounds = Rect.Transform(localBounds, GetVisualBrushTransform(visual));

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            DrawVisualInTiles(visual, drawingContext, localBounds.Size, sourceBounds);
        }

        return RenderVisual(drawingVisual, localBounds.Size, dpiX, dpiY, pixelFormat, viewport3D);
    }

    public static RenderTargetBitmap RenderVisual(Visual visual, Size bounds, int dpiX, int dpiY, PixelFormat? pixelFormat = null, Viewport3D? viewport3D = null)
    {
        var scaleX = dpiX / BaseDpi;
        var scaleY = dpiY / BaseDpi;

        pixelFormat ??= PixelFormats.Pbgra32;

        var renderTargetBitmap = new RenderTargetBitmap((int)Math.Ceiling(scaleX * bounds.Width), (int)Math.Ceiling(scaleY * bounds.Height), dpiX, dpiY, pixelFormat.Value);

        if (viewport3D is not null)
        {
            typeof(RenderTargetBitmap)
                .GetMethod("RenderForBitmapEffect", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(renderTargetBitmap, new object[] { visual, Matrix.Identity, Rect.Empty });
        }
        else
        {
            renderTargetBitmap.Render(visual);
        }

        return renderTargetBitmap;
    }

    internal static Matrix GetVisualBrushTransform(Visual visual)
    {
        var transform = VisualTreeHelper.GetTransform(visual)?.Value ?? Matrix.Identity;
        var offset = VisualTreeHelper.GetOffset(visual);
        transform.Translate(offset.X, offset.Y);
        return transform;
    }

    /// <summary>
    /// Draws <paramref name="visual"/> in smaller tiles using multiple <see cref="VisualBrush"/> to <paramref name="drawingContext"/>.
    /// This way we workaround a limitation in <see cref="VisualBrush"/> which causes poor quality for larger visuals.
    /// </summary>
    /// <param name="visual">The visual to be drawn.</param>
    /// <param name="drawingContext">The <see cref="DrawingContext"/> to use.</param>
    /// <param name="size">Output dimensions before applying the requested DPI.</param>
    /// <param name="sourceBounds">Bounds in the absolute coordinate space of the visual brush.</param>
    /// <param name="tileWidth">The width of one tile.</param>
    /// <param name="tileHeight">The height of one tile.</param>
    /// <remarks>
    /// Original version of this method was copied from https://srndolha.wordpress.com/2012/10/16/exported-drawingvisual-quality-when-using-visualbrush/
    ///
    /// A tile size of 32x32 turned out deliver the best quality while not increasing computation time too much.
    /// </remarks>
    private static void DrawVisualInTiles(Visual visual, DrawingContext drawingContext, Size size, Rect sourceBounds, double tileWidth = 32, double tileHeight = 32)
    {
        if (size.Width <= 0 || size.Height <= 0 || sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
        {
            return;
        }

        // Absolute brush coordinates include the offset and transform of the visual.
        // Tile the output at the original layout size, mapping each tile back to that space.
        var scaleX = sourceBounds.Width / size.Width;
        var scaleY = sourceBounds.Height / size.Height;
        for (var y = 0d; y < size.Height; y += tileHeight)
        {
            for (var x = 0d; x < size.Width; x += tileWidth)
            {
                var tile = new Rect(x, y, Math.Min(tileWidth, size.Width - x), Math.Min(tileHeight, size.Height - y));
                var viewbox = new Rect(sourceBounds.X + (x * scaleX), sourceBounds.Y + (y * scaleY), tile.Width * scaleX, tile.Height * scaleY);
                var brush = new VisualBrush(visual)
                {
                    Stretch = Stretch.Fill,
                    Viewbox = viewbox,
                    ViewboxUnits = BrushMappingMode.Absolute
                };
                drawingContext.DrawRectangle(brush, null, tile);
            }
        }
    }
}
