namespace MultipleDispatchers
{
    using System;
    using System.Windows;

    public partial class App
    {
        internal const string FixtureResourceKey = "SnoopFixture.DispatcherResource";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var useFixture = Array.IndexOf(e.Args, "--multi-dispatcher") >= 0;
            var primary = new MainWindow();
            if (useFixture)
            {
                this.MainWindow = primary;
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                this.Resources[FixtureResourceKey] = "Shared dispatcher resource marker";
                primary.ConfigureFixture(isPrimary: true);
            }

            primary.Show();
            if (useFixture)
            {
                MultipleDispatchers.MainWindow.ShowOnNewDispatcher(useFixture: true);
            }
        }
    }
}
