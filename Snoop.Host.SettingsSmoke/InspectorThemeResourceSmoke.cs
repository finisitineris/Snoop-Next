namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Snoop.Core;
using Snoop.Windows;

internal static class InspectorThemeResourceSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var palette = new[]
        {
            new { Key = "Text", Light = "#273447", Dark = "#E8EDF6" },
            new { Key = "Panel", Light = "#FFFFFF", Dark = "#293548" },
            new { Key = "Border", Light = "#DCE5F0", Dark = "#3B4A60" },
            new { Key = "Accent", Light = "#3979D5", Dark = "#78ADFF" },
            new { Key = "Background", Light = "#E7EDF7", Dark = "#141C28" },
            new { Key = "Error", Light = "#CE5151", Dark = "#FF9494" },
        };
        var originalTheme = Snoop.Core.Settings.Default.ThemeMode;
        ThemeManager.Current.ApplyTheme(ThemeMode.Light, inspector);
        Border? focusProbeHost = null;
        try
        {
            VerifyPalette(ThemeMode.Light);

            var chrome = Find<DockPanel>("WorkbenchChrome");
            var status = Find<StatusBar>("WorkbenchStatusBar");
            var focusStyle = (Style)inspector.FindResource("SnoopNext.WorkbenchFocusVisual");
            foreach (var name in new[] { "TreeTypeComboBox", "ThemeComboBox", "RefreshTreeButton", "GridSplitter", "DiagnosticsSplitter" })
            {
                if (!ReferenceEquals(Find<Control>(name).FocusVisualStyle, focusStyle))
                {
                    throw new InvalidOperationException("Workbench focus style not applied to " + name);
                }
            }

            // Mount the actual adorner template in the inspector's resource scope.
            // WPF displays the real adorner only after keyboard navigation.
            var focusProbe = new Control { Style = focusStyle };
            focusProbeHost = new Border { Child = focusProbe };
            chrome.Children.Add(focusProbeHost);
            focusProbe.ApplyTemplate();
            var outline = (Rectangle)focusProbe.Template.FindName("FocusOutline", focusProbe);
            var underlay = (Rectangle)focusProbe.Template.FindName("HighContrastUnderlay", focusProbe);

            VerifyAppearance(ThemeMode.Light);
            VerifyAppearance(ThemeMode.Dark);
            VerifyAppearance(ThemeMode.Light);

            void VerifyAppearance(ThemeMode theme)
            {
                ThemeManager.Current.ApplyTheme(theme, inspector);
                VerifyPalette(theme);
                RequireBrush(chrome.Background, BrushFor("Background").Color, "workbench background");
                RequireBrush(status.Background, BrushFor("Panel").Color, "status panel");
                RequireBrush(status.Foreground, BrushFor("Text").Color, "status text");
                RequireBrush(status.BorderBrush, BrushFor("Border").Color, "status separator");
                var highContrast = SystemParameters.HighContrast;
                var focusColor = highContrast ? SystemColors.WindowTextColor : BrushFor("Accent").Color;
                RequireBrush(outline.Stroke, focusColor, "keyboard focus outline");
                if (outline.StrokeThickness != (highContrast ? 1 : 2)
                    || underlay.Visibility != (highContrast ? Visibility.Visible : Visibility.Collapsed))
                {
                    throw new InvalidOperationException("Keyboard focus outline does not match the system contrast mode.");
                }
            }

            void VerifyPalette(ThemeMode theme)
            {
                foreach (var color in palette)
                {
                    var brush = BrushFor(color.Key);
                    RequireBrush(brush, (Color)ColorConverter.ConvertFromString(theme == ThemeMode.Dark ? color.Dark : color.Light), color.Key);
                    if (!brush.IsFrozen)
                    {
                        throw new InvalidOperationException("Workbench palette brush is mutable: " + color.Key);
                    }
                }
            }
        }
        finally
        {
            if (focusProbeHost?.Parent is DockPanel parent)
            {
                parent.Children.Remove(focusProbeHost);
            }

            ThemeManager.Current.ApplyTheme(originalTheme, inspector);
        }

        Console.WriteLine("PASS: workbench theme palette, status bar and keyboard focus template refresh in Light and Dark");

        T Find<T>(string name)
            where T : class
        {
            return inspector.FindName(name) as T ?? throw new InvalidOperationException("Missing workbench element " + name);
        }

        SolidColorBrush BrushFor(string key)
        {
            return inspector.TryFindResource("SnoopNext.Brush." + key) as SolidColorBrush
                ?? throw new InvalidOperationException("Missing workbench palette brush " + key);
        }
    }

    private static void RequireBrush(Brush? brush, Color expected, string context)
    {
        if (brush is not SolidColorBrush solid || solid.Color != expected)
        {
            throw new InvalidOperationException("Unexpected color for " + context + ". Expected " + expected + ", got " + brush);
        }
    }
}
