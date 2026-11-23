namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Localization;
using Snoop.Windows;

internal static class InspectorDispatcherSmoke
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string BackgroundKey = "Snoop.Brushes.Default.Background";

    public static void Verify(Application app, SnoopUI inspector, MenuItem primaryMenu)
    {
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var primaryUiCulture = Thread.CurrentThread.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var resources = app.Resources;
        var markerKey = new object();
        var marker = new object();
        resources.Add(markerKey, marker);
        var resourceCount = resources.Count;
        var mergedDictionaryCount = resources.MergedDictionaries.Count;

        var started = new TaskCompletionSource<Dispatcher>();
        var worker = new Thread(() =>
        {
            started.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Snoop inspector binding smoke" };
        worker.SetApartmentState(ApartmentState.STA);

        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            localization.SetLanguage("en-US");
            worker.Start();

            if (!started.Task.Wait(Timeout))
            {
                throw new TimeoutException("The second inspector Dispatcher did not start.");
            }

            var secondary = started.Task.Result;
            var secondaryMenu = secondary.Invoke(() =>
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
                var menu = new MenuItem();
                menu.SetBinding(MenuItem.HeaderProperty, new InspectorLocExtension("Menu.File").CreateBinding());
                return menu;
            }, DispatcherPriority.Normal, CancellationToken.None, Timeout);

            VerifyState("File");
            localization.SetLanguage("zh-CN");
            VerifyState("文件");
            localization.SetLanguage("en-US");
            VerifyState("File");
            VerifyThemes(app, inspector, secondary);

            void VerifyState(string expectedHeader)
            {
                primaryMenu.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Require(expectedHeader.Equals(primaryMenu.Header), "The SnoopUI File menu did not refresh to " + expectedHeader);
                Require(Thread.CurrentThread.CurrentCulture.Name == "fr-FR"
                        && Equals(Thread.CurrentThread.CurrentUICulture, primaryUiCulture),
                    "Inspector localization changed the primary thread's culture.");

                // WPF queues the cross-Dispatcher binding update ahead of ApplicationIdle.
                secondary.Invoke(() =>
                {
                    Require(expectedHeader.Equals(secondaryMenu.Header), "The second Dispatcher File menu did not refresh to " + expectedHeader);
                    Require(Thread.CurrentThread.CurrentCulture.Name == "ja-JP"
                            && Thread.CurrentThread.CurrentUICulture.Name == "en-GB",
                        "Inspector localization changed the second thread's culture.");
                }, DispatcherPriority.ApplicationIdle, CancellationToken.None, Timeout);

                Require(Equals(CultureInfo.DefaultThreadCurrentCulture, defaultCulture)
                        && Equals(CultureInfo.DefaultThreadCurrentUICulture, defaultUiCulture),
                    "Inspector localization changed a default thread culture.");
                Require(ReferenceEquals(app.Resources, resources) && resources.Count == resourceCount
                        && resources.MergedDictionaries.Count == mergedDictionaryCount
                        && ReferenceEquals(resources[markerKey], marker),
                    "Inspector localization changed application resources.");
            }
        }
        finally
        {
            resources.Remove(markerKey);
            Thread.CurrentThread.CurrentCulture = originalCulture;
            localization.SetLanguage(originalLanguage);

            var secondary = Dispatcher.FromThread(worker);
            if (secondary != null)
            {
                secondary.BeginInvokeShutdown(DispatcherPriority.Send);
                if (!worker.Join(Timeout))
                {
                    throw new TimeoutException("The second inspector Dispatcher did not stop.");
                }
            }
        }

        Console.WriteLine("PASS: inspector File bindings refreshed on two STA Dispatchers with cultures and application resources intact");
    }

    private static void VerifyThemes(Application app, SnoopUI inspector, Dispatcher secondaryDispatcher)
    {
        var manager = ThemeManager.Current;
        var originalTheme = Snoop.Core.Settings.Default.ThemeMode;
        var resources = app.Resources;
        var resourceCount = resources.Count;
        var dictionaryCount = resources.MergedDictionaries.Count;
        var targetBrush = Brushes.Magenta;
        var target = new Window();
        target.Resources[BackgroundKey] = targetBrush;
        var secondaryWindow = secondaryDispatcher.Invoke(() =>
        {
            var window = new SnoopBaseWindow();
            window.BeginInit();
            var border = new Border();
            border.SetResourceReference(Border.BackgroundProperty, BackgroundKey);
            window.Content = border;
            window.EndInit();
            return window;
        }, DispatcherPriority.Normal, CancellationToken.None, Timeout);

        try
        {
            var dark = Color.FromRgb(46, 46, 46);
            manager.ApplyTheme(ThemeMode.Dark);
            var primaryDark = VerifyTheme(dark);
            var secondaryDark = secondaryDispatcher.Invoke(() => ((Border)secondaryWindow.Content).Background,
                DispatcherPriority.ApplicationIdle, CancellationToken.None, Timeout);
            Require(!ReferenceEquals(primaryDark, secondaryDark), "Inspectors on different Dispatchers shared a theme brush.");

            manager.ApplyTheme(ThemeMode.Light);
            VerifyTheme(Colors.White);

            // Queue a theme change for the second Dispatcher while it is busy.
            // Its newer selection must still win when the older queued operation runs.
            using var secondaryEntered = new ManualResetEventSlim();
            using var primaryUpdated = new ManualResetEventSlim();
            var update = secondaryDispatcher.InvokeAsync(() =>
            {
                secondaryEntered.Set();
                Require(primaryUpdated.Wait(Timeout), "The primary theme update did not finish.");
                manager.ApplyTheme(ThemeMode.Light);
            }, DispatcherPriority.Send);

            try
            {
                Require(secondaryEntered.Wait(Timeout), "The second theme update did not start.");
                manager.ApplyTheme(ThemeMode.Dark);
            }
            finally
            {
                primaryUpdated.Set();
            }

            Require(update.Task.Wait(Timeout), "The second theme update did not finish.");
            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            VerifyTheme(Colors.White);

            manager.ApplyTheme(ThemeMode.Auto);
            var systemColor = ((SolidColorBrush)inspector.FindResource(BackgroundKey)).Color;
            Require(systemColor == Colors.White || systemColor == dark, "System theme did not resolve to a known palette.");
            VerifyTheme(systemColor);

            Require(ReferenceEquals(app.Resources, resources) && resources.Count == resourceCount
                    && resources.MergedDictionaries.Count == dictionaryCount
                    && ReferenceEquals(target.FindResource(BackgroundKey), targetBrush),
                "Applying an inspector theme changed the target's resources.");

            SolidColorBrush VerifyTheme(Color color)
            {
                var brush = (SolidColorBrush)inspector.FindResource(BackgroundKey);
                Require(brush.Color == color, "The primary inspector did not apply the selected theme.");
                secondaryDispatcher.Invoke(() =>
                {
                    Require(((SolidColorBrush)((Border)secondaryWindow.Content).Background).Color == color,
                        "The second inspector did not refresh its dynamic theme resource.");
                }, DispatcherPriority.ApplicationIdle, CancellationToken.None, Timeout);
                return brush;
            }
        }
        finally
        {
            secondaryDispatcher.Invoke(secondaryWindow.Close, DispatcherPriority.Normal, CancellationToken.None, Timeout);
            target.Close();
            manager.ApplyTheme(originalTheme);
        }

        Console.WriteLine("PASS: inspector themes refresh on two STA Dispatchers without changing target resources");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
