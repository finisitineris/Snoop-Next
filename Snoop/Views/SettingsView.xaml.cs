namespace Snoop.Views;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Snoop.Infrastructure;
using Snoop.Next.Localization;

public partial class SettingsView
{
    private static readonly HashSet<string> realSettingsProperties = new()
    {
        nameof(Settings.Default.Version),
        nameof(Settings.Default.MultipleAppDomainMode),
        nameof(Settings.Default.MultipleDispatcherMode),
        nameof(Settings.Default.SetOwnerWindow),
        nameof(Settings.Default.ShowActivated),
        nameof(Settings.Default.EnableDiagnostics),
        nameof(Settings.Default.GlobalHotKey),
        nameof(Settings.Default.ILSpyPath),
        nameof(Settings.Default.StartAppChooserMinimized),
    };

    public static readonly DependencyProperty PropertiesProperty = DependencyProperty.Register(nameof(Properties), typeof(ObservableCollection<PropertyInformation>), typeof(SettingsView), new PropertyMetadata(default(ObservableCollection<PropertyInformation>)));

    public SettingsView()
    {
        this.InitializeComponent();

        // This selector is a pending preference; no live preview until Save.
        this.LanguageSelector.SelectedValue = Settings.Default.LanguageTag;

        this.Properties = new ObservableCollection<PropertyInformation>(TypeDescriptor.GetProperties(Settings.Default)
            .OfType<PropertyDescriptor>()
            .Where(x => realSettingsProperties.Contains(x.Name))
            .Select(x => new PropertyInformation(Settings.Default, x, x.Name, x.DisplayName)));
    }

    public ObservableCollection<PropertyInformation>? Properties
    {
        get => (ObservableCollection<PropertyInformation>?)this.GetValue(PropertiesProperty);
        set => this.SetValue(PropertiesProperty, value);
    }

    private void SaveSettingsAndClose_OnClick(object sender, RoutedEventArgs e)
    {
        Settings.Default.LanguageTag = this.LanguageSelector.SelectedValue as string ?? LanguageTagPolicy.DefaultLanguageTag;
        Settings.Default.Save();

        // This code runs on the Snoop-owned Settings window Dispatcher.
        LocalizationManager.Default.SetLanguage(Settings.Default.LanguageTag);

        if (Window.GetWindow(this) is { } ownerWindow)
        {
            ownerWindow.DialogResult = true;
            ownerWindow.Close();
        }
    }

    private void DiscardSettings_OnClick(object sender, RoutedEventArgs e)
    {
        Settings.Default.Reload();
        this.LanguageSelector.SelectedValue = Settings.Default.LanguageTag;
    }

    private void ResetAllSettings_OnClick(object sender, RoutedEventArgs e)
    {
        Settings.Default.Reset();
        this.LanguageSelector.SelectedValue = Settings.Default.LanguageTag;
    }
}