namespace Snoop.Views;

using System.ComponentModel;
using System.Windows;
using Snoop.Localization;
using Snoop.Windows;

public sealed class HighlightSettingsWindow : SnoopBaseWindow
{
    private readonly HighlightSettingsView view = new();

    public HighlightSettingsWindow()
    {
        this.Content = this.view;
        this.SetBinding(TitleProperty, new InspectorLocExtension("HighlightSettings.Title").CreateBinding());
        this.Width = 500;
        this.Height = 354;
        this.MinWidth = 380;
        this.MinHeight = 280;
        this.WindowStyle = WindowStyle.SingleBorderWindow;
        this.ShowInTaskbar = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        this.view.CommitPendingFields();
        base.OnClosing(e);
    }
}
