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
    private KeyGestureEx? pendingShortcut;
    private string? shortcutError;
    private bool isRecording;
    private bool awaitingPrimaryKey;

    public SettingsView()
    {
        this.InitializeComponent();
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
        this.SetPendingShortcut(settings.GlobalHotKey);
        this.ILSpyPathTextBox.Text = settings.ILSpyPath ?? string.Empty;
        this.SaveError.Visibility = Visibility.Collapsed;
    }

    private void SaveSettingsAndClose_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.shortcutError is not null)
        {
            this.GlobalHotKeyEditor.Focus();
            this.StartRecording();
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
                draft.GlobalHotKey = this.pendingShortcut;
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

    private void RecordShortcut_OnClick(object sender, RoutedEventArgs e)
    {
        this.GlobalHotKeyEditor.Focus();
        this.StartRecording();
    }

    private void Recorder_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => this.StartRecording();

    private void StartRecording()
    {
        this.isRecording = true;
        this.awaitingPrimaryKey = false;
        this.UpdateShortcutDisplay();
    }

    private void Recorder_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!this.isRecording)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = e.KeyboardDevice.Modifiers;
        if (e.KeyboardDevice.IsKeyDown(Key.LWin) || e.KeyboardDevice.IsKeyDown(Key.RWin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            this.SetPendingShortcut(this.pendingShortcut);
            return;
        }

        e.Handled = true;
        if (e.IsRepeat)
        {
            return;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            this.SetPendingShortcut(this.pendingShortcut);
            return;
        }

        if (IsModifierKey(key))
        {
            this.awaitingPrimaryKey = true;
            return;
        }

        this.awaitingPrimaryKey = false;
        this.shortcutError = GetShortcutError(key, modifiers);
        if (this.shortcutError is null)
        {
            this.SetPendingShortcut(new KeyGestureEx(key, modifiers));
        }
        else
        {
            this.UpdateShortcutDisplay();
        }
    }

    private void Recorder_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!this.isRecording)
        {
            return;
        }

        e.Handled = true;
        if (this.awaitingPrimaryKey && e.KeyboardDevice.Modifiers == ModifierKeys.None
                                    && !e.KeyboardDevice.IsKeyDown(Key.LWin) && !e.KeyboardDevice.IsKeyDown(Key.RWin))
        {
            this.awaitingPrimaryKey = false;
            this.shortcutError = "Settings.ShortcutNeedsKey";
            this.UpdateShortcutDisplay();
        }
    }

    private void Recorder_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (this.isRecording)
        {
            this.isRecording = false;
            this.awaitingPrimaryKey = false;
            this.UpdateShortcutDisplay();
        }
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? GetShortcutError(Key key, ModifierKeys modifiers)
    {
        if (key is <= Key.None or > Key.OemClear or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return "Settings.ShortcutUnsupported";
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            return "Settings.ShortcutNeedsModifier";
        }

        var reserved = modifiers switch
        {
            ModifierKeys.Control => key is Key.A or Key.C or Key.F or Key.H or Key.N or Key.O or Key.P or Key.S or Key.T or Key.V
                                        or Key.W or Key.X or Key.Y or Key.Z or Key.Insert or Key.Back or Key.Delete or Key.Tab
                                        or Key.Escape or Key.Home or Key.End or Key.Left or Key.Right or Key.F4,
            ModifierKeys.Control | ModifierKeys.Shift => key is Key.Escape or Key.Tab or Key.Z or Key.S or Key.T or Key.N
                                                             or Key.V or Key.W or Key.Home or Key.End or Key.Left or Key.Right,
            ModifierKeys.Alt => key is Key.F4 or Key.Tab or Key.Escape or Key.Space or Key.Left or Key.Right or Key.Return,
            ModifierKeys.Alt | ModifierKeys.Shift => key is Key.Tab or Key.Escape,
            ModifierKeys.Control | ModifierKeys.Alt => key == Key.Delete,
            ModifierKeys.Windows | ModifierKeys.Shift => key is Key.S or Key.Left or Key.Right or Key.M,
            ModifierKeys.Windows | ModifierKeys.Control => key is Key.D or Key.F4 or Key.Left or Key.Right,
            _ => false
        };
        if (reserved || key is Key.PrintScreen or Key.Sleep or Key.Apps or >= Key.BrowserBack and <= Key.LaunchApplication2)
        {
            return "Settings.ShortcutReserved";
        }

        if (modifiers == ModifierKeys.Windows)
        {
            return "Settings.ShortcutWindows";
        }

        // The launcher listens for ordinary key-up events. Alt without Ctrl produces system key messages.
        return (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.Alt
            ? "Settings.ShortcutAlt"
            : null;
    }

    private void ResetShortcut_OnClick(object sender, RoutedEventArgs e) => this.SetPendingShortcut(Settings.DefaultGlobalHotKey);

    // The existing settings format uses an empty gesture for disabled and an omitted element for the default.
    private void DisableShortcut_OnClick(object sender, RoutedEventArgs e) => this.SetPendingShortcut(new KeyGestureEx(Key.None));

    private void SetPendingShortcut(KeyGestureEx? value)
    {
        this.pendingShortcut = value;
        this.isRecording = false;
        this.awaitingPrimaryKey = false;
        this.shortcutError = null;
        this.UpdateShortcutDisplay();
    }

    private void UpdateShortcutDisplay()
    {
        var text = LocalizationManager.Default;
        this.GlobalHotKeyEditor.Content = this.isRecording ? text["Settings.ShortcutListening"]
            : this.pendingShortcut is { Key: not Key.None } gesture ? gesture.ToString() : text["Settings.ShortcutDisabled"];
        this.ShortcutHint.Text = text[this.shortcutError ?? (this.isRecording ? "Settings.ShortcutRecordingHelp" : "Settings.ShortcutHelp")];
        this.ShortcutHint.SetResourceReference(TextBlock.ForegroundProperty, this.shortcutError is null ? "SnoopNext.Brush.TextSecondary" : "SnoopNext.Brush.Error");
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
