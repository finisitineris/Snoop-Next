namespace Snoop.Views;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Snoop.Data;
using Snoop.Next.Localization;

public partial class SettingsView
{
    public SettingsView()
    {
        this.InitializeComponent();
        this.VersionValue.Text = Settings.Default.Version;
        this.ShowPreferences(Settings.Default);
    }

    private void ShowPreferences(Settings settings)
    {
        this.LanguageSelector.SelectedValue = settings.LanguageTag;
        this.MultipleAppDomainModeSelector.SelectedValue = settings.MultipleAppDomainMode;
        this.MultipleDispatcherModeSelector.SelectedValue = settings.MultipleDispatcherMode;
        this.SetOwnerWindowCheckBox.IsChecked = settings.SetOwnerWindow;
        this.ShowActivatedCheckBox.IsChecked = settings.ShowActivated;
        this.EnableDiagnosticsCheckBox.IsChecked = settings.EnableDiagnostics;
        this.StartAppChooserMinimizedCheckBox.IsChecked = settings.StartAppChooserMinimized;
        this.GlobalHotKeyEditor.Text = settings.GlobalHotKey?.ToString() ?? string.Empty;
        this.ILSpyPathTextBox.Text = settings.ILSpyPath ?? string.Empty;
        this.GlobalHotKeyError.Visibility = Visibility.Collapsed;
        this.SaveError.Visibility = Visibility.Collapsed;
    }

    private void SaveSettingsAndClose_OnClick(object sender, RoutedEventArgs e)
    {
        if (!this.TryReadShortcut(out var shortcut))
        {
            this.GlobalHotKeyError.Visibility = Visibility.Visible;
            this.GlobalHotKeyEditor.Focus();
            this.GlobalHotKeyEditor.SelectAll();
            return;
        }

        var settings = Settings.Default;
        try
        {
            settings.SavePreferences(draft =>
            {
                draft.LanguageTag = (string)this.LanguageSelector.SelectedValue;
                draft.MultipleAppDomainMode = (MultipleAppDomainMode)this.MultipleAppDomainModeSelector.SelectedValue;
                draft.MultipleDispatcherMode = (MultipleDispatcherMode)this.MultipleDispatcherModeSelector.SelectedValue;
                draft.SetOwnerWindow = this.SetOwnerWindowCheckBox.IsChecked == true;
                draft.ShowActivated = this.ShowActivatedCheckBox.IsChecked == true;
                draft.EnableDiagnostics = this.EnableDiagnosticsCheckBox.IsChecked == true;
                draft.StartAppChooserMinimized = this.StartAppChooserMinimizedCheckBox.IsChecked == true;
                draft.GlobalHotKey = shortcut;
                if (this.ILSpyPathTextBox.Text != (draft.ILSpyPath ?? string.Empty))
                {
                    draft.ILSpyPath = this.ILSpyPathTextBox.Text;
                }
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            this.SaveError.Visibility = Visibility.Visible;
            return;
        }

        LocalizationManager.Default.SetLanguage(settings.LanguageTag);
        if (Window.GetWindow(this) is { } window)
        {
            window.DialogResult = true;
        }
    }

    private bool TryReadShortcut(out KeyGestureEx? shortcut)
    {
        var text = this.GlobalHotKeyEditor.Text.Trim();
        if (text.Length == 0)
        {
            // The serialized empty gesture disables the shortcut after a restart.
            // An omitted GlobalHotKey element is a legacy file's default shortcut.
            shortcut = new KeyGestureEx(Key.None);
            return true;
        }

        shortcut = Settings.Default.GlobalHotKey;
        if (text == shortcut?.ToString())
        {
            return true;
        }

        try
        {
            shortcut = (KeyGestureEx?)KeyGestureExConverter.Default.ConvertFromInvariantString(text);
            return shortcut is { Key: > Key.None and <= Key.OemClear };
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private void GlobalHotKeyEditor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        this.GlobalHotKeyError.Visibility = Visibility.Collapsed;
    }

    private void DiscardSettings_OnClick(object sender, RoutedEventArgs e)
    {
        this.ShowPreferences(Settings.Default);
    }

    private void ResetAllSettings_OnClick(object sender, RoutedEventArgs e)
    {
        this.ShowPreferences(new Settings());
    }
}
