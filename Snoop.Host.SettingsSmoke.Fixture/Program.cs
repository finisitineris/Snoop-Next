namespace Snoop.Host.SettingsSmoke.Fixture;

using System;
using System.Windows;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var window = new Window { Title = args[0], Width = 240, Height = 100, ShowActivated = false };
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => window.Close();
        timeout.Start();
        window.ShowDialog();
        timeout.Stop();
    }
}
