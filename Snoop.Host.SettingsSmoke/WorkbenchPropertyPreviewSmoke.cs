namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snoop.Controls;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchPropertyPreviewSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var scene = new Canvas { Width = 240, Height = 96, Background = Brushes.White };
        var button = new Button { Name = "SaveButton", Width = 160, Height = 48, FontSize = 18, Content = "Save" };
        scene.Children.Add(button);
        Canvas.SetLeft(button, 40);
        Canvas.SetTop(button, 24);
        using var source = new HwndSource(new HwndSourceParameters("Snoop property preview smoke")
        {
            Width = 240, Height = 96, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000),
        });
        source.RootVisual = scene;
        WorkbenchLayoutSmoke.Arrange(scene, new Rect(0, 0, scene.Width, scene.Height));

        var properties = (PropertyInspector)inspector.FindName("PropertyInspector");
        var grid = (PropertyGrid2)properties.FindName("PropertyGrid");
        var list = (ListView)grid.FindName("ListView");
        var preview = (Previewer)inspector.FindName("PreviewArea");
        var zoomer = (ZoomerControl)preview.FindName("Zoomer");
        var viewbox = (Viewbox)zoomer.FindName("Viewbox");
        preview.SetCurrentValue(Previewer.IsActiveProperty, true);
        inspector.ScopeTo(scene);
        SnoopUI.RefreshCommand.Execute(null, inspector);
        inspector.CurrentSelection = inspector.RootTreeItem?.FindNode(button)
            ?? throw new InvalidOperationException("Missing SaveButton in the inspector's live tree.");
        ((ComboBox)properties.FindName("PropertyOriginFilterCombo")).SetCurrentValue(Selector.SelectedValueProperty, PropertyOriginFilter.Local);
        PropertyOriginFilterSmoke.Await(() => Rows().Any(row => row.DependencyProperty == FrameworkElement.WidthProperty)
                && Rows().Any(row => row.DependencyProperty == ContentControl.ContentProperty)
                && ReferenceEquals(preview.Target, button) && viewbox.Child is Rectangle { Fill: VisualBrush brush } && ReferenceEquals(brush.Visual, scene),
            "The property inspector and preview must show the same SaveButton before editing.");
        var width = Rows().Single(row => row.DependencyProperty == FrameworkElement.WidthProperty);
        var content = Rows().Single(row => row.DependencyProperty == ContentControl.ContentProperty);

        var widthEditor = PropertyOriginFilterSmoke.SelectTextEditor(list, width);
        var before = Snapshot();
        PropertyOriginFilterSmoke.CommitText(widthEditor, "-1");
        if (button.Width != 160 || button.ActualWidth != 160 || width.IsValueChangedByUser || HasEditedMarker(width) || !before.SequenceEqual(Snapshot()))
        {
            throw new InvalidOperationException("Rejecting a negative Width must preserve the button, edit history and rendered preview pixels.");
        }

        var contentEditor = PropertyOriginFilterSmoke.SelectTextEditor(list, content);
        PropertyOriginFilterSmoke.CommitText(contentEditor, "完成");
        PropertyOriginFilterSmoke.Await(() => Equals(button.Content, "完成") && Equals(content.Value, "完成") && content.IsValueChangedByUser
                && HasEditedMarker(content) && contentEditor.Text == "完成" && InspectorTableSmoke.Descendants(button).OfType<TextBlock>().Any(text => text.Text == "完成")
                && !before.SequenceEqual(Snapshot()),
            "Committing Content must update the property cell, the target's rendered text and the pixels in its live preview.");
        Console.WriteLine("PASS: SaveButton retains identical preview pixels for rejected Width and renders 完成 when its Content edit commits.");

        IEnumerable<PropertyInformation> Rows() => list.Items.Cast<PropertyInformation>().Where(row => row.Target == button);

        bool HasEditedMarker(PropertyInformation property)
        {
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(property);
            var label = InspectorLocalization.Default["PropertyGrid.Edited"];
            return InspectorTableSmoke.Descendants(row).OfType<TextBlock>().Any(text => text.Name == "EditedMarker" && text.IsVisible && text.Text == label)
                   && UIElementAutomationPeer.CreatePeerForElement(row).GetItemStatus() == label;
        }

        byte[] Snapshot()
        {
            scene.UpdateLayout();
            inspector.UpdateLayout();
            if (viewbox.Child is not Rectangle { Fill: VisualBrush brush } image || !ReferenceEquals(brush.Visual, scene))
            {
                throw new InvalidOperationException("Expected a live preview of the selected button's scene.");
            }

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(image.ActualWidth), (int)Math.Ceiling(image.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(image);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            return pixels;
        }
    }
}
