namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class PropertyEditFeedbackSmoke
{
    public static void Verify()
    {
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        var source = new Border { Width = 300 };
        var target = new Button { Name = "SaveButton", Content = "Save", Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var binding = target.SetBinding(FrameworkElement.MaxWidthProperty,
            new Binding(nameof(FrameworkElement.Width)) { Source = source, Mode = BindingMode.OneWay });
        var inspector = new PropertyInspector { Width = 354, FilterString = "Width", ShowDefaults = true };
        var panel = new DockPanel();
        panel.SetResourceReference(Panel.BackgroundProperty, "SnoopNext.Brush.Panel");
        DockPanel.SetDock(target, Dock.Top);
        panel.Children.Add(target);
        panel.Children.Add(inspector);
        var window = new SnoopBaseWindow { Content = panel, SizeToContent = SizeToContent.Width, Height = 560, Title = "SaveButton" };
        try
        {
            localization.SetLanguage("en-US");
            window.Show();
            Drain();
            inspector.RootTarget = target;
            Drain();
            var grid = (PropertyGrid2)inspector.FindName("PropertyGrid");
            var list = (ListView)grid.FindName("ListView");
            var feedback = (TextBlock)grid.FindName("PropertyEditFeedback");
            PropertyInformation Row(DependencyProperty property) => grid.Properties.Single(row => row.DependencyProperty == property);
            var width = Row(FrameworkElement.WidthProperty);
            var actualWidth = Row(FrameworkElement.ActualWidthProperty);
            var maxWidth = Row(FrameworkElement.MaxWidthProperty);

            void ExpectFeedback(PropertyInformation selected, string resource)
            {
                Drain();
                var message = localization[resource];
                var rowPeer = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren()
                    .Single(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem && peer.GetAutomationId() == selected.Name);
                Require(ReferenceEquals(inspector.RootTarget, selected.Target) && ReferenceEquals(grid.Target, selected.Target)
                        && ReferenceEquals(grid.Selection, selected)
                        && ReferenceEquals(list.SelectedItem, selected), "Property feedback must preserve the selected target and row.");
                Require(feedback.IsVisible && !string.IsNullOrWhiteSpace(message) && !message.StartsWith("[Missing:", StringComparison.Ordinal)
                        && feedback.Text == message && rowPeer.GetHelpText() == message,
                    "The selected row and visible feedback must explain " + resource + " in " + localization.LanguageTag);
                panel.UpdateLayout();
                var bounds = feedback.TransformToAncestor(panel).TransformBounds(new Rect(feedback.RenderSize));
                Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
                        && bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1,
                    "The full feedback text must fit in the default-width Properties pane.");
            }

            var editor = SelectTextEditor(list, width);
            CommitText(editor, "-1");
            ExpectFeedback(width, "ValueEditor.RejectedValue");
            Require(editor.Text == "-1" && target.Width == 120 && Equals(width.Value, 120d) && !width.IsValueChangedByUser,
                "A rejected negative Width must preserve the value and leave edit history empty.");
            LauncherAppearanceSmoke.CaptureIfRequested(panel, "property-invalid-width-en-US");
            var englishRejection = feedback.Text;
            localization.SetLanguage("zh-CN");
            ExpectFeedback(width, "ValueEditor.RejectedValue");
            Require(feedback.Text != englishRejection, "An open edit error must change languages with the inspector.");
            CommitText(editor, "not-a-width");
            ExpectFeedback(width, "ValueEditor.InvalidFormat");
            Require(editor.Text == "not-a-width" && target.Width == 120 && Equals(width.Value, 120d) && !width.IsValueChangedByUser,
                "An unparseable Width must preserve the value and leave edit history empty.");
            CommitText(editor, "120");
            Require(!feedback.IsVisible && !width.IsValueChangedByUser,
                "Reentering the current Width must clear feedback without creating an edit.");
            CommitText(editor, "-1");
            ExpectFeedback(width, "ValueEditor.RejectedValue");

            editor = SelectTextEditor(list, maxWidth);
            Require(!feedback.IsVisible, "A different writable row must not inherit the previous error.");
            CommitText(editor, "-1");
            ExpectFeedback(maxWidth, "ValueEditor.RejectedValue");
            source.Width = 310;
            Drain();
            Require(ReferenceEquals(binding, BindingOperations.GetBindingExpressionBase(target, FrameworkElement.MaxWidthProperty))
                    && target.MaxWidth == 310 && Equals(maxWidth.Value, 310d) && !maxWidth.IsValueChangedByUser,
                "Rejecting a bound value must retain the original live binding and its edit history.");

            var readOnlyEditor = SelectTextEditor(list, actualWidth);
            ExpectFeedback(actualWidth, "PropertyGrid.ReadOnlyHint");
            var editorPeer = UIElementAutomationPeer.CreatePeerForElement(readOnlyEditor);
            var provider = (IValueProvider)editorPeer.GetPattern(PatternInterface.Value);
            var layoutWidth = target.ActualWidth;
            Require(provider.IsReadOnly && editorPeer.GetHelpText() == feedback.Text
                    && Equals(actualWidth.Value, layoutWidth) && provider.Value == actualWidth.StringValue,
                "The read-only editor must expose the rendered button width through UI Automation.");
            try
            {
                provider.SetValue("90");
                throw new InvalidOperationException("The read-only ActualWidth editor accepted input through UI Automation.");
            }
            catch (ElementNotEnabledException)
            {
                // WPF's read-only TextBox rejects automation input.
            }

            Require(target.ActualWidth == layoutWidth && target.Width == 120 && !actualWidth.IsValueChangedByUser,
                "An attempted read-only edit must preserve the target layout and edit history.");
            LauncherAppearanceSmoke.CaptureIfRequested(panel, "property-readonly-zh-CN");
            var chineseExplanation = feedback.Text;
            localization.SetLanguage("en-US");
            ExpectFeedback(actualWidth, "PropertyGrid.ReadOnlyHint");
            Require(feedback.Text != chineseExplanation, "The selected read-only explanation must translate in place.");
            editor = SelectTextEditor(list, width);
            Require(!feedback.IsVisible, "Revisiting Width must not restore its dismissed validation error.");
            CommitText(editor, "-1");
            ExpectFeedback(width, "ValueEditor.RejectedValue");
            CommitText(editor, "144");
            Require(target.Width == 144 && Equals(width.Value, 144d) && width.IsValueChangedByUser && !feedback.IsVisible,
                "A valid correction must commit to the target, clear feedback and mark the edited row.");
            readOnlyEditor = SelectTextEditor(list, actualWidth);
            ExpectFeedback(actualWidth, "PropertyGrid.ReadOnlyHint");
            Require(target.ActualWidth == 144 && Equals(actualWidth.Value, 144d) && readOnlyEditor.Text == "144",
                "The same ActualWidth row must reflect the new layout after a Width correction.");
            editor = SelectTextEditor(list, width);
            CommitText(editor, "-1");
            ExpectFeedback(width, "ValueEditor.RejectedValue");
            Require(target.Width == 144 && width.IsValueChangedByUser, "A later rejection must preserve the successful edit.");

            var constrained = new ConstrainedWidth();
            // The inspector classifies dependency properties owned by a Snoop namespace as uncommon.
            inspector.ShowUncommonProperties = true;
            inspector.RootTarget = constrained;
            Await(() => list.Items.OfType<PropertyInformation>().Any(row => row.DependencyProperty == ConstrainedWidth.WidthLimitProperty)
                        && list.Items.OfType<PropertyInformation>().Any(row => ReferenceEquals(row.Target, constrained) && row.Name == nameof(ConstrainedWidth.Width)),
                "The property table must load both WidthLimit and Width for the constrained target.");
            var limit = Row(ConstrainedWidth.WidthLimitProperty);
            SelectTextEditor(list, limit);
            ExpectFeedback(limit, "PropertyGrid.ReadOnlyHint");
            Require(limit.IsLocallySet && !PropertyGrid2.ClearCommand.CanExecute(limit, grid),
                "Reset must be disabled for a read-only dependency property with a local value.");
            limit.Clear();
            Require(constrained.WidthLimit == 20 && !limit.IsValueChangedByUser, "Reset must preserve the read-only local value.");
            var bounded = grid.Properties.Single(row => row.Name == nameof(ConstrainedWidth.Width));
            editor = SelectTextEditor(list, bounded);
            CommitText(editor, "-1");
            ExpectFeedback(bounded, "ValueEditor.RejectedValue");
            Require(editor.Text == "-1" && constrained.Width == 20 && Equals(bounded.Value, 20) && !bounded.IsValueChangedByUser,
                "A throwing CLR setter must leave the value and edit history intact and keep the rejected input for correction.");
            CommitText(editor, "25");
            Require(!feedback.IsVisible && constrained.Width == 20 && Equals(bounded.Value, 20) && !bounded.IsValueChangedByUser,
                "Coercing a valid correction to the current value must dismiss the error without recording a change.");
        }
        finally
        {
            window.Close();
            localization.SetLanguage(originalLanguage);
        }

        Console.WriteLine("PASS: localized property feedback preserves edits and bindings, supports correction and exposes read-only values.");
    }

    private sealed class ConstrainedWidth : DependencyObject
    {
        private static readonly DependencyPropertyKey WidthLimitPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(WidthLimit), typeof(int), typeof(ConstrainedWidth), new PropertyMetadata(0));

        public static readonly DependencyProperty WidthLimitProperty = WidthLimitPropertyKey.DependencyProperty;

        private int width = 20;

        public ConstrainedWidth() => this.SetValue(WidthLimitPropertyKey, 20);

        public int WidthLimit => (int)this.GetValue(WidthLimitProperty);

        public int Width
        {
            get => this.width;
            set => this.width = value >= 0 ? Math.Min(value, this.WidthLimit) : throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
