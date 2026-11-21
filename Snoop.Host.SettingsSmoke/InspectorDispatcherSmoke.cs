namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Localization;

internal static class InspectorDispatcherSmoke
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static void Verify(Application app, MenuItem primaryMenu)
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
