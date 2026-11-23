namespace MultipleDispatchers
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Threading;

    public partial class MainWindow
    {
        public MainWindow()
        {
            this.InitializeComponent();
        }

        internal static void ShowOnNewDispatcher(bool useFixture)
        {
            var thread = new Thread(() =>
            {
                var window = new MainWindow();
                if (useFixture)
                {
                    window.ConfigureFixture(isPrimary: false);
                    window.Closed += (_, _) => window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }

                window.Show();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        internal void ConfigureFixture(bool isPrimary)
        {
            var thread = Thread.CurrentThread;
            thread.CurrentCulture = CultureInfo.GetCultureInfo(isPrimary ? "fr-FR" : "ja-JP");
            thread.CurrentUICulture = CultureInfo.GetCultureInfo(isPrimary ? "de-DE" : "en-GB");

            using var process = Process.GetCurrentProcess();
            var domain = AppDomain.CurrentDomain;
            var role = isPrimary ? "Primary" : "Secondary";
            var identity = $"{role} | PID {process.Id} | AppDomain {domain.Id} | STA thread {thread.ManagedThreadId}";
            this.Name = role + "DispatcherWindow";
            this.Title = "Snoop dispatcher fixture | " + identity;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            var offset = isPrimary ? 24 : 120;
            this.Left = SystemParameters.WorkArea.Left + offset;
            this.Top = SystemParameters.WorkArea.Top + offset;

            var readout = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            var refresh = new Button { Content = "Refresh target state", HorizontalAlignment = HorizontalAlignment.Left };
            refresh.Click += (_, _) => RefreshState();
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(readout);
            panel.Children.Add(refresh);
            this.Content = panel;
            RefreshState();

            void RefreshState()
            {
                readout.Text = $"{identity}\nAppDomain name: {domain.FriendlyName}\n"
                               + $"Runtime: {Environment.Version} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})\n"
                               + $"CurrentCulture: {thread.CurrentCulture.Name}\n"
                               + $"CurrentUICulture: {thread.CurrentUICulture.Name}\n"
                               + $"DefaultThreadCurrentCulture: {CultureInfo.DefaultThreadCurrentCulture?.Name ?? "(unset)"}\n"
                               + $"DefaultThreadCurrentUICulture: {CultureInfo.DefaultThreadCurrentUICulture?.Name ?? "(unset)"}";
                if (isPrimary)
                {
                    // The application resource dictionary is shared by both Dispatchers.
                    var resources = Application.Current.Resources;
                    var marker = resources[App.FixtureResourceKey] ?? "(missing)";
                    readout.Text += $"\nApplication resource: {marker}\n"
                                    + $"Resource entries: {resources.Count}\n"
                                    + $"Merged dictionaries: {resources.MergedDictionaries.Count}";
                }
            }
        }

        private void DispatcherLaunchButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOnNewDispatcher(useFixture: false);
        }

        private void NoDispatcherLaunchButton_Click(object sender, RoutedEventArgs e)
        {
            new MainWindow().Show();
        }
    }
}
