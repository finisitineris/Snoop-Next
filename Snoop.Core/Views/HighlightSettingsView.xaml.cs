namespace Snoop.Views;

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snoop.Converters;
using Snoop.Infrastructure.SelectionHighlight;

public partial class HighlightSettingsView
{
    private readonly SelectionHighlightOptions options = SelectionHighlightOptions.Default;

    public HighlightSettingsView()
    {
        this.InitializeComponent();
        this.RefreshFields(null);
        PropertyChangedEventManager.AddHandler(this.options, this.OnOptionsChanged, string.Empty);
    }

    internal void CommitPendingFields()
    {
        this.Commit(this.FillEditor);
        this.Commit(this.BorderColorEditor);
        this.Commit(this.ThicknessEditor);
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.Dispatcher.CheckAccess())
        {
            this.RefreshFields(e.PropertyName);
        }
        else
        {
            this.Dispatcher.InvokeAsync(() => this.RefreshFields(e.PropertyName));
        }
    }

    private void RefreshFields(string? property)
    {
        if (property is null or nameof(SelectionHighlightOptions.Background))
        {
            this.FillEditor.Text = BrushText(this.options.Background);
            this.FillError.Visibility = Visibility.Collapsed;
        }

        if (property is null or nameof(SelectionHighlightOptions.BorderBrush))
        {
            this.BorderColorEditor.Text = BrushText(this.options.BorderBrush);
            this.BorderColorError.Visibility = Visibility.Collapsed;
        }

        if (property is null or nameof(SelectionHighlightOptions.BorderThickness))
        {
            this.ThicknessEditor.Text = this.options.BorderThickness.ToString("R", CultureInfo.InvariantCulture);
            this.ThicknessError.Visibility = Visibility.Collapsed;
        }
    }

    private static string BrushText(Brush? brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        // A WPF gradient, image or visual brush has no lossless editable string representation.
        return brush?.GetType().Name ?? string.Empty;
    }

    private void Field_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => this.Commit((TextBox)sender);

    private void Field_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            this.Commit((TextBox)sender);
            e.Handled = true;
        }
    }

    private void Commit(TextBox editor)
    {
        var text = editor.Text.Trim();
        if (editor == this.ThicknessEditor)
        {
            if (StringValueConverter.TryConvertFromString(typeof(double), text, out var value)
                && value is double number && number >= 0 && !double.IsInfinity(number))
            {
                this.options.BorderThickness = number;
                this.ThicknessError.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.ThicknessError.Visibility = Visibility.Visible;
            }

            return;
        }

        var isFill = editor == this.FillEditor;
        var original = isFill ? this.options.Background : this.options.BorderBrush;
        var error = isFill ? this.FillError : this.BorderColorError;
        if (text == BrushText(original))
        {
            error.Visibility = Visibility.Collapsed;
            return;
        }

        if (isFill && text.Length == 0)
        {
            this.options.Background = null;
        }
        else if (StringValueConverter.TryConvertFromString(typeof(Brush), text, out var parsed) && parsed is Brush brush)
        {
            // Keep the other properties of an existing solid brush (opacity and transforms).
            if (original is SolidColorBrush previous && brush is SolidColorBrush next)
            {
                var copy = previous.CloneCurrentValue();
                copy.Color = next.Color;
                brush = copy;
            }

            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            if (isFill)
            {
                this.options.Background = brush;
            }
            else
            {
                this.options.BorderBrush = brush;
            }
        }
        else
        {
            error.Visibility = Visibility.Visible;
            return;
        }

        error.Visibility = Visibility.Collapsed;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    private void ResetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        this.options.Reset();
        // Reset also discards invalid text when a setting already equals its default.
        this.RefreshFields(null);
    }
}
