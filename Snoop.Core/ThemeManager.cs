namespace Snoop.Core;

using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using JetBrains.Annotations;
using Microsoft.Win32;
using Snoop.Infrastructure;

public class ThemeManager
{
    public static readonly ThemeManager Current = new();

    private readonly ConditionalWeakTable<Dispatcher, ThemeResources> resourcesByDispatcher = new();
    private long themeChange;

    public void ApplyTheme(ThemeMode themeMode)
    {
        var change = Interlocked.Increment(ref this.themeChange);
        var finalThemeMode = ResolveThemeMode(themeMode);
        foreach (var visual in SnoopPartsRegistry.GetSnoopVisualTreeRoots())
        {
            if (visual is not Window window)
            {
                continue;
            }

            var dispatcher = window.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                ApplyCurrentTheme();
            }
            else if (!dispatcher.HasShutdownStarted)
            {
                dispatcher.InvokeAsync(ApplyCurrentTheme);
            }

            void ApplyCurrentTheme()
            {
                // A newer choice on this Dispatcher can overtake an older queued choice.
                if (Volatile.Read(ref this.themeChange) == change)
                {
                    this.ApplyResolvedTheme(finalThemeMode, window);
                }
            }
        }
    }

    public void ApplyTheme(ThemeMode themeMode, Visual visual)
    {
        if (visual is Window window)
        {
            this.ApplyResolvedTheme(ResolveThemeMode(themeMode), window);
        }
    }

    private void ApplyResolvedTheme(ThemeMode themeMode, Window window)
    {
        if (!window.IsInitialized || window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var resources = this.resourcesByDispatcher.GetValue(window.Dispatcher, _ => new ThemeResources());
        var resourceDictionary = themeMode is ThemeMode.Dark ? resources.Dark : resources.Light;
        var resourceDictionaryToRemove = themeMode is ThemeMode.Dark ? resources.Light : resources.Dark;
        var dictionaries = window.Resources.MergedDictionaries;

        dictionaries.Remove(resourceDictionary);
        dictionaries.Add(resourceDictionary);
        dictionaries.Remove(resourceDictionaryToRemove);
    }

    private static ThemeMode ResolveThemeMode(ThemeMode themeMode)
    {
        return themeMode is ThemeMode.Auto
            ? AppsUseLightTheme() ? ThemeMode.Light : ThemeMode.Dark
            : themeMode;
    }

    private sealed class ThemeResources
    {
        public ThemeResources()
        {
            this.Light = new ResourceDictionary
            {
                Source = new Uri("/Snoop.Core;component/Themes/Colors.Light.xaml", UriKind.Relative)
            };
            this.Dark = Invert(this.Light, new ResourceDictionary
            {
                Source = new Uri("/Snoop.Core;component/Themes/Colors.Dark.xaml", UriKind.Relative)
            });
        }

        public ResourceDictionary Light { get; }

        public ResourceDictionary Dark { get; }
    }

    [MustUseReturnValue]
    private static bool AppsUseLightTheme()
    {
        try
        {
            var registryValue = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);

            if (registryValue is null)
            {
                return true;
            }

            return Convert.ToBoolean(registryValue);
        }
        catch (Exception exception)
        {
            Trace.TraceError(exception.ToString());
        }

        return true;
    }

    private static ResourceDictionary Invert(ResourceDictionary original, ResourceDictionary target)
    {
#pragma warning disable CS8605
        foreach (DictionaryEntry entry in original)
#pragma warning restore CS8605
        {
            var key = entry.Key;

            if (target.Contains(key))
            {
                continue;
            }

            var value = entry.Value;

            if (value is Brush brush)
            {
                value = Invert(brush);
            }
            else if (entry.Value is Color color)
            {
                value = Invert(color);
            }

            target.Add(key, value);
        }

        return target;
    }

    private static Brush Invert(Brush original)
    {
        switch (original)
        {
            case SolidColorBrush solidColorBrush:
            {
                var brush = new SolidColorBrush(Invert(solidColorBrush.Color));
                brush.Freeze();
                return brush;
            }

            case LinearGradientBrush linearGradientBrush:
            {
                var brush = linearGradientBrush.Clone();

                foreach (var gradientStop in brush.GradientStops)
                {
                    gradientStop.Color = Invert(gradientStop.Color);
                }

                brush.Freeze();
                return brush;
            }

            default:
                return original;
        }
    }

    private static Color Invert(Color original)
    {
        System.Drawing.Color fromColor = System.Drawing.Color.FromArgb(original.A, original.R, original.G, original.B);

        var invertedColor = System.Drawing.Color.FromArgb(fromColor.ToArgb() ^ 0xffffff);

        if (invertedColor.R is > 110 and < 150
            && invertedColor.G is > 110 and < 150
            && invertedColor.B is > 110 and < 150)
        {
            var avg = (invertedColor.R + invertedColor.G + invertedColor.B) / 3;
            avg = avg > 128 ? 200 : 60;
            invertedColor = System.Drawing.Color.FromArgb(fromColor.A, avg, avg, avg);
        }

        return Color.FromArgb(invertedColor.A, invertedColor.R, invertedColor.G, invertedColor.B);
    }
}
