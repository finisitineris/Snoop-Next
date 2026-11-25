namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

internal static class PropertyOriginSmoke
{
    public static void Verify()
    {
        var language = InspectorLocalization.Default.LanguageTag;
        var target = new Button
        {
            Name = "SaveButton",
            Content = "Save changes",
            DataContext = new CommandContext(ApplicationCommands.Save),
            Style = new Style(typeof(Button)) { Setters = { new Setter(FrameworkElement.MinWidthProperty, 120d) } },
        };
        target.Resources.Add("ActionWidth", 160d);
        target.SetResourceReference(FrameworkElement.WidthProperty, "ActionWidth");
        var brush = new SolidColorBrush(Colors.SteelBlue);
        brush.Freeze();
        target.Resources.Add("ActionBrush", brush);
        target.Background = brush;
        var command = target.SetBinding(Button.CommandProperty, new Binding(nameof(CommandContext.SaveCommand)));
        var broken = target.SetBinding(Button.CommandParameterProperty, new Binding("MissingParameter") { FallbackValue = "fallback" });
        target.SetBinding(FrameworkElement.TagProperty, new PriorityBinding
        {
            Bindings = { new Binding(nameof(CommandContext.SaveCommand)) },
        });

        var inspector = new PropertyInspector { RootTarget = target };
        var window = new SnoopBaseWindow { Content = inspector, Width = 740, Height = 570, Title = "SaveButton" };
        try
        {
            InspectorLocalization.Default.SetLanguage("en-US");
            window.Show();
            Drain();
            var grid = (PropertyGrid2)inspector.FindName("PropertyGrid");
            var list = (ListView)grid.FindName("ListView");
            var panel = (Border)grid.FindName("OriginDetailsPanel");
            var details = (TextBox)grid.FindName("OriginDetailsText");
            var close = (Button)grid.FindName("CloseOriginDetailsButton");
            PropertyInformation Row(DependencyProperty property) => grid.Properties.Single(row => row.DependencyProperty == property);

            var style = Row(FrameworkElement.MinWidthProperty);
            var literal = Row(ContentControl.ContentProperty);
            var resource = Row(FrameworkElement.WidthProperty);
            var bound = Row(Button.CommandProperty);
            var failed = Row(Button.CommandParameterProperty);
            var priority = Row(FrameworkElement.TagProperty);
            Require(style.ValueSourceText == "Style" && HasField(style.PropertyOriginDetails, "EffectiveValue", "120")
                    && literal.ValueSourceText == "Local" && HasField(literal.PropertyOriginDetails, "LocalValue", "Save changes"),
                "The grid must distinguish a style setter from a local literal and display their real values.");
            Require(Row(Control.BackgroundProperty).ValueSourceText == "Local",
                "A directly assigned brush must retain its local origin even when the dictionary contains the same instance.");
            Require(resource.ValueSource.IsExpression && resource.ValueSourceText.Contains("Expression")
                    && !resource.ValueSourceText.Contains("Binding") && HasField(resource.PropertyOriginDetails, "EffectiveValue", "160"),
                "The DynamicResource must retain its WPF expression origin without a binding badge.");
            Require(bound.ValueSourceText.Contains("Binding") && command.Status == BindingStatus.Active
                    && broken.Status == BindingStatus.PathError && HasField(failed.PropertyOriginDetails, "Status", "PathError")
                    && HasField(failed.PropertyOriginDetails, "Path", "MissingParameter"),
                "Healthy and failing bindings must report their real WPF paths and statuses.");
            Require(HasField(priority.PropertyOriginDetails, "Binding", nameof(PriorityBinding))
                    && HasField(priority.PropertyOriginDetails, "ActiveChild", "1")
                    && HasField(priority.PropertyOriginDetails, "Path", nameof(CommandContext.SaveCommand)),
                "The priority binding must expose its child path.");

            list.SetCurrentValue(ListView.SelectedItemProperty, bound);
            Drain();
            Require(!panel.IsVisible, "Selecting Command must leave origin details closed until requested.");
            void OpenSourceCell()
            {
                list.ScrollIntoView(bound);
                list.UpdateLayout();
                var row = list.ItemContainerGenerator.ContainerFromItem(bound) as ListViewItem
                    ?? throw new InvalidOperationException("The Command property row was not realized.");
                var button = InspectorTableSmoke.Descendants(row).OfType<Button>().Single(control => control.Name == "ShowPropertyOriginButton");
                button.BringIntoView();
                Require(button.Focusable && button.IsTabStop && button.IsEnabled && button.IsVisible,
                    "The Command source cell must expose an enabled keyboard-accessible button.");
                PressEnter(button);
                Require(panel.IsVisible && details.IsReadOnly && ReferenceEquals(list.SelectedItem, bound)
                        && ReferenceEquals(grid.Selection, bound) && ReferenceEquals(inspector.Target, target),
                    "Opening origin details must preserve the Command row and inspected target.");
            }

            OpenSourceCell();
            Require(HasField(details.Text, "Path", nameof(CommandContext.SaveCommand))
                    && HasField(details.Text, "Source", typeof(CommandContext).FullName!) && HasField(details.Text, "Status", "Active"),
                "The visible detail pane must show the binding path, resolved source and runtime status.");
            InspectorFlyoutSmoke.Capture(inspector, "property-origin-en-US");
            var english = details.Text;
            inspector.Width = 354; // The workbench's default property pane width.
            window.SizeToContent = SizeToContent.Width;
            InspectorLocalization.Default.SetLanguage("zh-CN");
            Drain();
            Require(panel.IsVisible && panel.ActualWidth > 0 && panel.ActualWidth <= 354 && details.Text != english
                    && HasField(details.Text, "Path", nameof(CommandContext.SaveCommand))
                    && HasField(details.Text, "Source", typeof(CommandContext).FullName!) && HasField(details.Text, "Status", "Active")
                    && ReferenceEquals(grid.Selection, bound),
                "Switching to Chinese must translate the open detail pane and preserve its technical values and row.");
            Require(IsTextVisible(details, nameof(CommandContext.SaveCommand)) && IsTextVisible(details, "Active"),
                "The binding path and status must be readable without scrolling at the default property pane width.");
            InspectorFlyoutSmoke.Capture(inspector, "property-origin-zh-CN");
            target.DataContext = new CommandContext(ApplicationCommands.Open);
            Drain();
            Require(ReferenceEquals(target.Command, ApplicationCommands.Open)
                    && ReferenceEquals(command, BindingOperations.GetBindingExpressionBase(target, Button.CommandProperty))
                    && ReferenceEquals(broken, BindingOperations.GetBindingExpressionBase(target, Button.CommandParameterProperty)),
                "Inspecting origins must preserve the target bindings and permit subsequent data-context updates.");

            PressEnter(close);
            Require(!panel.IsVisible && ReferenceEquals(grid.Selection, bound), "Closing details must retain the inspected row.");
            // Replacing a binding with one that returns the same value may produce no effective-value change notification.
            target.SetBinding(Button.CommandProperty, new Binding(nameof(CommandContext.AlternateCommand)));
            Drain();
            OpenSourceCell();
            Require(HasField(details.Text, "Path", nameof(CommandContext.AlternateCommand)),
                "Reopening the same property must read its current binding expression.");
            list.SetCurrentValue(ListView.SelectedItemProperty, literal);
            Drain();
            Require(!panel.IsVisible, "Selecting a different property must dismiss the previous property's details.");
            OpenSourceCell();
            inspector.RootTarget = new TextBlock();
            Drain();
            Require(!panel.IsVisible && ReferenceEquals(inspector.RootTarget, grid.Target),
                "Changing the inspected target must dismiss the previous target's details.");
        }
        finally
        {
            window.Close();
            InspectorLocalization.Default.SetLanguage(language);
        }

        Console.WriteLine("PASS: value origins distinguish style, local, resource and live bindings; keyboard inspection preserves the selected property and target.");
    }

    private static bool HasField(string text, string key, string value)
    {
        var line = string.Format(CultureInfo.InvariantCulture, InspectorLocalization.Default["PropertyOrigin." + key], value);
        return text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Contains(line);
    }

    private static bool IsTextVisible(TextBox box, string text)
    {
        var index = box.Text.IndexOf(text, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var bounds = box.GetRectFromCharacterIndex(index + text.Length - 1);
        return !bounds.IsEmpty && bounds.Height > 0 && bounds.Top >= 0 && bounds.Bottom <= box.ActualHeight;
    }

    private static void PressEnter(Button button)
    {
        button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
        Drain();
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CommandContext
    {
        public CommandContext(ICommand saveCommand) => this.SaveCommand = saveCommand;

        public ICommand SaveCommand { get; }

        public ICommand AlternateCommand => this.SaveCommand;
    }
}
