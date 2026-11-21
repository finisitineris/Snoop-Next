#if NETFRAMEWORK
namespace HelloWorld
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;

    internal static class MultiAppDomainFixture
    {
        private const string ResourceKey = "SnoopFixture.TargetResource";

        public static void StartIfRequested(Window primaryWindow)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--multi-appdomain") < 0)
            {
                return;
            }

            ConfigureWindow(Application.Current, primaryWindow, "Primary", 24);

            var domain = AppDomain.CreateDomain("Snoop fixture secondary", null, AppDomain.CurrentDomain.SetupInformation);
            // The secondary dispatcher is process-bound. Closing the primary window ends the fixture.
            var thread = new Thread(() => domain.DoCallBack(RunSecondaryWindow))
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static void RunSecondaryWindow()
        {
            var application = new Application();
            var window = new Window();
            ConfigureWindow(application, window, "Secondary", 96);
            application.Run(window);
        }

        private static void ConfigureWindow(Application application, Window window, string role, double offset)
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            application.MainWindow = window;
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Resources[ResourceKey] = role + " resource marker";

            using var process = Process.GetCurrentProcess();
            var domain = AppDomain.CurrentDomain.FriendlyName;
            var identity = $"{role} | PID {process.Id} | {domain}";
            window.Title = "Snoop fixture | " + identity;
            window.Width = 520;
            window.Height = 220;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = SystemParameters.WorkArea.Left + offset;
            window.Top = SystemParameters.WorkArea.Top + offset;

            var readout = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            var refresh = new Button { Content = "Refresh target state", HorizontalAlignment = HorizontalAlignment.Left };
            refresh.Click += (_, _) => RefreshState();

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(readout);
            panel.Children.Add(refresh);
            window.Content = panel;
            RefreshState();

            void RefreshState()
            {
                var resources = application.Resources;
                var resource = resources[ResourceKey] ?? "(missing)";
                readout.Text = $"{identity}\nCurrentCulture: {Thread.CurrentThread.CurrentCulture.Name}\n"
                               + $"CurrentUICulture: {Thread.CurrentThread.CurrentUICulture.Name}\n"
                               + $"Application resource: {resource}\n"
                               + $"Resource entries: {resources.Count}\n"
                               + $"Merged dictionaries: {resources.MergedDictionaries.Count}";
            }
        }
    }
}
#endif
