namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Data;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class WorkbenchInitialSelectionSmoke
{
    internal const string Argument = "--workbench-initial-selection-smoke";

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            Verify(app);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    internal static void Verify(Application app)
    {
        var originalMainWindow = app.MainWindow;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        var originalInstallPath = Environment.GetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR);
        var originalTransientSettingsPath = TransientSettingsData.Current?.WriteToFile();
        var saveButton = new Button { Name = "SaveButton", Content = "Save" };
        var target = new Window
        {
            Title = "Snoop initial selection fixture",
            Width = 280,
            Height = 180,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new Border { Child = new StackPanel { Children = { saveButton } } },
        };

        try
        {
            app.MainWindow = target;
            target.Show();
            var handle = new WindowInteropHelper(target).Handle;
            Require(handle != IntPtr.Zero, "The initial-selection fixture must have a visible HWND.");

            FocusManager.SetFocusedElement(target, saveButton);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(target), saveButton),
                "The nested SaveButton must have logical focus before attachment.");
            VerifyAttachment(app, handle, saveButton, "the focused SaveButton", capture: true);

            FocusManager.SetFocusedElement(target, null);
            Require(FocusManager.GetFocusedElement(target) is null,
                "The target Window must have no logical focus for the window fallback.");
            VerifyAttachment(app, handle, target, "the named Window when it has no logical focus");

            var foreignButton = new Button { Name = "OtherWindowFocus", Content = "Other action" };
            var sibling = new Window
            {
                Title = "Snoop sibling selection fixture",
                Width = 240,
                Height = 140,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = foreignButton,
            };
            try
            {
                sibling.Show();
                FocusManager.SetFocusedElement(target, foreignButton);
                Require(ReferenceEquals(FocusManager.GetFocusedElement(target), foreignButton),
                    "The named Window must hold the foreign focus reference for the regression.");
                VerifyAttachment(app, handle, target, "the named Window when stored focus belongs to a sibling Window");
            }
            finally
            {
                sibling.Close();
            }

            FocusManager.SetFocusedElement(target, saveButton);
            VerifyAttachment(app, IntPtr.Zero, app, "the inspection root when no target HWND was supplied");
        }
        finally
        {
            app.MainWindow = originalMainWindow;
            target.Close();
            if (originalTransientSettingsPath is not null)
            {
                try
                {
                    TransientSettingsData.LoadCurrent(originalTransientSettingsPath);
                }
                finally
                {
                    File.Delete(originalTransientSettingsPath);
                }
            }

            InspectorLocalization.Default.SetLanguage(originalLanguage);
            Environment.SetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR, originalInstallPath);
        }

        Console.WriteLine("PASS: managed WPF attachment selects the focused SaveButton and falls back safely for missing, foreign or untargeted focus.");
    }

    private static void VerifyAttachment(Application app, IntPtr handle, object expected, string description, bool capture = false)
    {
        var existingInspectors = app.Windows.OfType<SnoopUI>().ToArray();
        var settingsPath = new TransientSettingsData
        {
            TargetWindowHandle = handle.ToInt64(),
            MultipleAppDomainMode = MultipleAppDomainMode.NeverUse,
            MultipleDispatcherMode = MultipleDispatcherMode.NeverUse,
            ShowActivated = false,
            LanguageTag = InspectorLocalization.Default.LanguageTag,
        }.WriteToFile();

        try
        {
            Require(SnoopManager.StartSnoop(settingsPath) == 0, "The managed attachment entry point must open an inspector.");
            var inspector = app.Windows.OfType<SnoopUI>().Single(window => !existingInspectors.Contains(window));
            var selected = inspector.CurrentSelection;
            Require(inspector.IsVisible && selected is not null && ReferenceEquals(selected.Target, expected),
                $"The initial workbench selection must be {description}. Actual selection was '{selected}'.");

            var tree = (TreeView)inspector.FindName("Tree");
            var preview = (Previewer)inspector.FindName("PreviewArea");
            var properties = (PropertyInspector)inspector.FindName("PropertyInspector");
            Await(() => tree.IsVisible && preview.IsVisible && properties.IsVisible && selected!.IsSelected
                        && ReferenceEquals(tree.SelectedItem, selected) && ReferenceEquals(preview.Target, expected)
                        && ReferenceEquals(properties.RootTarget, expected),
                $"The tree, preview and properties must reflect {description} in the visible three-pane workbench.");

            if (capture)
            {
                LauncherAppearanceSmoke.CaptureIfRequested((FrameworkElement)inspector.FindName("WorkbenchChrome"),
                    "workbench-initial-selection");
            }
        }
        finally
        {
            foreach (var inspector in app.Windows.OfType<SnoopUI>().Where(window => !existingInspectors.Contains(window)).ToArray())
            {
                inspector.Close();
            }

            File.Delete(settingsPath);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
