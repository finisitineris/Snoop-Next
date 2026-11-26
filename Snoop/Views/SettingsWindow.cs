namespace Snoop.Views;

using System.Windows;
using Snoop.Next.Localization;
using Snoop.Windows;

public sealed class SettingsWindow : SnoopBaseWindow
{
    public SettingsWindow()
    {
        this.Title = LocalizationManager.Default["Chooser.Settings"];
        this.FontSize = 13;
        this.SetResourceReference(FontFamilyProperty, "SnoopNext.FontFamily.UI");
        this.Content = new SettingsView();
        this.Width = 640;
        this.Height = 620;
        this.MinWidth = 480;
        this.MinHeight = 360;
        this.ShowInTaskbar = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
