namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snoop.Controls;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class WorkbenchPropertyPreviewSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var language = InspectorLocalization.Default.LanguageTag;
        try
        {
            VerifyHistory(inspector);
        }
        finally
        {
            InspectorLocalization.Default.SetLanguage(language);
        }
    }

    private static void VerifyHistory(SnoopUI inspector)
    {
        var scene = new Canvas { Width = 240, Height = 96, Background = Brushes.White };
        var button = new Button { Name = "SaveButton", Width = 160, Height = 48, FontSize = 18, Content = "保存" };
        // Give completion a distinct appearance even on runners without CJK glyphs.
        button.Style = new Style(typeof(Button))
        {
            Triggers = { new Trigger { Property = ContentControl.ContentProperty, Value = "完成", Setters = { new Setter(Control.ForegroundProperty, Brushes.Green) } } },
        };
        var cancel = new Button { Name = "CancelButton", Width = 32, Height = 48, Content = "取消" };
        scene.Children.Add(button);
        scene.Children.Add(cancel);
        Canvas.SetLeft(button, 40);
        Canvas.SetTop(button, 24);
        Canvas.SetLeft(cancel, 204);
        Canvas.SetTop(cancel, 24);
        using var source = new HwndSource(new HwndSourceParameters("Snoop property preview smoke")
        {
            Width = 240, Height = 96, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000),
        });
        source.RootVisual = scene;
        WorkbenchLayoutSmoke.Arrange(scene, new Rect(0, 0, scene.Width, scene.Height));

        var properties = (PropertyInspector)inspector.FindName("PropertyInspector");
        var grid = (PropertyGrid2)properties.FindName("PropertyGrid");
        var list = (ListView)grid.FindName("ListView");
        var undo = (Button)properties.FindName("UndoPropertyEditButton");
        var redo = (Button)properties.FindName("RedoPropertyEditButton");
        var chrome = (DockPanel)inspector.FindName("WorkbenchChrome");
        var preview = (Previewer)inspector.FindName("PreviewArea");
        var zoomer = (ZoomerControl)preview.FindName("Zoomer");
        var viewbox = (Viewbox)zoomer.FindName("Viewbox");
        preview.SetCurrentValue(Previewer.IsActiveProperty, true);
        inspector.ScopeTo(scene);
        SnoopUI.RefreshCommand.Execute(null, inspector);
        ((ComboBox)properties.FindName("PropertyOriginFilterCombo")).SetCurrentValue(Selector.SelectedValueProperty, PropertyOriginFilter.Local);
        SelectTarget(button);
        ExpectLabels("en-US", "Undo", "Redo");
        ExpectHistory(false, false);
        var before = Snapshot();

        var width = Row(button, FrameworkElement.WidthProperty);
        var widthEditor = SelectTextEditor(list, width);
        CommitText(widthEditor, "-1");
        Await(() => button.Width == 160 && button.ActualWidth == 160 && MarkerMatches(width, false) && before.SequenceEqual(Snapshot()),
            "Rejecting a negative Width must preserve the button, its unedited row and the rendered preview pixels.");
        ExpectHistory(false, false);

        CommitText(SelectTextEditor(list, Row(button, ContentControl.ContentProperty)), "完成");
        ExpectContent(button, "完成", true);
        ExpectHistory(true, false);
        Await(() => !before.SequenceEqual(Snapshot()), "The preview must render the committed Content.");
        var after = Snapshot();

        ExpectLabels("zh-CN", "撤销", "重做");
        SelectTarget(cancel);
        ExpectContent(cancel, "取消", false);
        ExpectHistory(true, false);
        CheckReport(true);
        Invoke(undo);
        ExpectHistory(false, true);
        ExpectContent(cancel, "取消", false);
        CheckReport(false);
        Await(() => Equals(button.Content, "保存") && ReferenceEquals(inspector.CurrentSelection?.Target, cancel),
            "Undo from CancelButton must restore SaveButton without changing selection or the sibling's value.");

        SelectTarget(button);
        ExpectContent(button, "保存", false);
        Await(() => before.SequenceEqual(Snapshot()), "Undo must restore the original preview pixels after reselection.");
        LauncherAppearanceSmoke.CaptureIfRequested(chrome, "property-history-undo-zh-CN");

        widthEditor = SelectTextEditor(list, Row(button, FrameworkElement.WidthProperty));
        CommitText(widthEditor, "-1");
        ExpectHistory(false, true);
        CommitText(widthEditor, "0160");
        ExpectHistory(false, true);
        Invoke(redo);
        ExpectHistory(true, false);
        ExpectContent(button, "完成", true);
        CheckReport(true);
        Await(() => after.SequenceEqual(Snapshot()), "Redo must restore the edited preview pixels.");
        LauncherAppearanceSmoke.CaptureIfRequested(chrome, "property-history-redo-zh-CN");

        Invoke(undo);
        ExpectHistory(false, true);
        ExpectContent(button, "保存", false);
        CommitText(SelectTextEditor(list, Row(button, ContentControl.ContentProperty)), "另存");
        ExpectContent(button, "另存", true);
        ExpectHistory(true, false);

        CommitText(SelectTextEditor(list, Row(button, ContentControl.ContentProperty)), "提交");
        ExpectContent(button, "提交", true);
        Invoke(undo);
        ExpectHistory(true, true);
        ExpectContent(button, "另存", true);
        Invoke(redo);
        ExpectHistory(true, false);
        ExpectContent(button, "提交", true);
        Invoke(undo);
        ExpectHistory(true, true);
        ExpectContent(button, "另存", true);

        // The effective value matches the last edit, but the application now owns a live binding.
        var application = new ContentControl { Content = "另存" };
        var binding = button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = application });
        if (undo.IsEnabled)
        {
            Invoke(undo);
        }

        ExpectHistory(false, false);
        application.Content = "已同步";
        Await(() => ReferenceEquals(binding, BindingOperations.GetBindingExpressionBase(button, ContentControl.ContentProperty))
                    && Equals(button.Content, "已同步"),
            "Undo must preserve a replacement binding with the same effective value and subsequent source updates.");
        button.Content = "另存";
        CommandManager.InvalidateRequerySuggested();
        ExpectHistory(false, false);

        widthEditor = SelectTextEditor(list, Row(button, FrameworkElement.WidthProperty));
        CommitText(widthEditor, "144");
        ExpectHistory(true, false);
        button.Width = 152;
        if (undo.IsEnabled)
        {
            Invoke(undo);
        }

        ExpectHistory(false, false);
        Await(() => button.Width == 152 && widthEditor.Text == "152",
            "The target application's new local Width must survive an attempted undo of an earlier edit.");
        Console.WriteLine("PASS: SaveButton.Content undo/redo preserves workbench selection, markers, localized controls and preview pixels; branches and application changes are guarded.");

        PropertyInformation Row(Button target, DependencyProperty property) =>
            list.Items.Cast<PropertyInformation>().Single(row => ReferenceEquals(row.Target, target) && row.DependencyProperty == property);

        void SelectTarget(Button target)
        {
            inspector.CurrentSelection = inspector.RootTreeItem?.FindNode(target)
                ?? throw new InvalidOperationException("Missing " + target.Name + " in the inspector's live tree.");
            Await(() => list.Items.Cast<PropertyInformation>().Any(row => ReferenceEquals(row.Target, target) && row.DependencyProperty == ContentControl.ContentProperty)
                        && list.Items.Cast<PropertyInformation>().Any(row => ReferenceEquals(row.Target, target) && row.DependencyProperty == FrameworkElement.WidthProperty)
                        && ReferenceEquals(preview.Target, target) && viewbox.Child is Rectangle { Fill: VisualBrush brush } && ReferenceEquals(brush.Visual, scene),
                "The properties and preview must show " + target.Name + " after tree selection.");
        }

        static void CheckReport(bool edited)
        {
            var format = typeof(EditedPropertiesHelper).GetMethod("FormatReport", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing edited-properties report formatter.");
            var report = (string?)format.Invoke(null, null) ?? string.Empty;
            if (report.Contains("Property: Content, New Value: 完成") != edited)
            {
                throw new InvalidOperationException("Copy property changes must reflect Undo and Redo even when the original element is not selected.");
            }
        }

        void ExpectLabels(string language, string undoCaption, string redoCaption)
        {
            InspectorLocalization.Default.SetLanguage(language);
            Await(() => Equals(undo.Content, undoCaption) && Equals(redo.Content, redoCaption)
                        && undo.IsVisible && redo.IsVisible
                        && UIElementAutomationPeer.CreatePeerForElement(undo).GetName() == InspectorLocalization.Default["PropertyInspector.UndoEditTip"]
                        && UIElementAutomationPeer.CreatePeerForElement(redo).GetName() == InspectorLocalization.Default["PropertyInspector.RedoEditTip"],
                "The visible and accessible property history controls must be localized in " + language);
        }

        void ExpectHistory(bool canUndo, bool canRedo) =>
            Await(() => undo.IsEnabled == canUndo && redo.IsEnabled == canRedo,
                $"Expected property history controls Undo={canUndo}, Redo={canRedo}.");

        void ExpectContent(Button target, string expected, bool edited)
        {
            var content = Row(target, ContentControl.ContentProperty);
            var editor = SelectTextEditor(list, content);
            Await(() => Equals(target.Content, expected) && Equals(content.Value, expected) && editor.Text == expected && MarkerMatches(content, edited)
                        && InspectorTableSmoke.Descendants(target).OfType<TextBlock>().Any(text => text.Text == expected),
                $"{target.Name}.Content must show {expected} on the target and in the property cell with Edited={edited}.");
        }

        bool MarkerMatches(PropertyInformation property, bool edited)
        {
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(property);
            var label = InspectorLocalization.Default["PropertyGrid.Edited"];
            var visible = InspectorTableSmoke.Descendants(row).OfType<TextBlock>().Any(text => text.Name == "EditedMarker" && text.IsVisible && text.Text == label);
            var status = UIElementAutomationPeer.CreatePeerForElement(row).GetItemStatus();
            return property.IsValueChangedByUser == edited && visible == edited && (edited ? status == label : string.IsNullOrEmpty(status));
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

    private static void Invoke(Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
}
