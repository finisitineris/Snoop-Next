namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Windows;

internal static class ScreenshotPreviewSmoke
{
    public static void Verify()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SnoopCapture-" + Guid.NewGuid().ToString("N")));
        const string filename = "SnoopScreenshot_CaptureTarget_96.png";
        var destination = Path.Combine(directory.FullName, filename);
        var lastDirectory = typeof(ScreenshotDialog).GetField("lastSaveDirectory", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Cannot isolate the screenshot destination.");
        var savedDirectory = lastDirectory.GetValue(null);
        var target = new Border { Name = "CaptureTarget", Width = 64, Height = 32, Background = Brushes.Coral };
        var preview = new Previewer();
        var content = new StackPanel();
        content.Children.Add(target);
        content.Children.Add(preview);
        var owner = new SnoopBaseWindow { Width = 460, Height = 320, Content = content, ShowInTaskbar = false };
        try
        {
            lastDirectory.SetValue(null, directory.FullName);
            owner.Show();
            preview.Target = target;
            owner.UpdateLayout();
            owner.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var camera = FindButton(preview, Previewer.ScreenshotCommand);
            Open(dialog =>
            {
                VerifyPreview(dialog);
                Click(dialog, ScreenshotDialog.CancelCommand);
            });
            ExpectEmpty();

            Open(dialog =>
            {
                VerifyPreview(dialog);
                ChooseFile(dialog, null);
                Require(dialog.IsVisible, "Cancelling Save As must leave the screenshot preview open.");
                ExpectEmpty();
                ChooseFile(dialog, filename);
                Require(!dialog.IsVisible, "Confirming a PNG destination must close the screenshot preview.");
            });

            var files = directory.GetFiles();
            var actual = string.Join(", ", files.Select(file => file.Name));
            Require(files.Length == 1 && string.Equals(files[0].Name, filename, StringComparison.OrdinalIgnoreCase) && File.Exists(destination),
                $"Expected only '{destination}' after Save As. Entries in '{directory.FullName}': [{actual}]. The picker returned directory '{lastDirectory.GetValue(null)}'.");
            using var stream = File.OpenRead(destination);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Single();
            var pixel = new byte[4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(32, 16, 1, 1), pixel, 4, 0);
            Require(frame.PixelWidth == 64 && frame.PixelHeight == 32 && pixel.SequenceEqual(new[] { Colors.Coral.B, Colors.Coral.G, Colors.Coral.R, (byte)255 }),
                "The saved 96 DPI PNG must contain the selected visual's dimensions and center pixel.");
            Console.WriteLine("PASS: native screenshot preview, preview cancellation and Save As cancellation create no files; explicit Save As writes the selected visual to PNG.");

            void Open(Action<ScreenshotDialog> verify)
            {
                // Drive the preview after the screenshot command has entered its modal dispatcher.
                var action = owner.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ScreenshotDialog>().Single(window => window.IsVisible);
                    try
                    {
                        Require(ReferenceEquals(dialog.Owner, owner), "The screenshot preview must belong to the window containing the camera button.");
                        verify(dialog);
                    }
                    finally
                    {
                        if (dialog.IsVisible)
                        {
                            dialog.Close();
                        }
                    }
                }, DispatcherPriority.ApplicationIdle);
                Previewer.ScreenshotCommand.Execute(null, camera);
                action.Task.GetAwaiter().GetResult();
            }

            void VerifyPreview(ScreenshotDialog dialog)
            {
                dialog.UpdateLayout();
                var photo = (ContentControl)dialog.FindName("PhotoFrame");
                Require(photo.IsVisible && InspectorTableSmoke.Descendants(photo).OfType<Border>()
                        .Any(border => border.Background is VisualBrush brush && ReferenceEquals(brush.Visual, target)),
                    "Opening the camera action must display the selected visual in the preview.");
                ((ComboBox)dialog.FindName("dpiBox")).SelectedIndex = 0;
                ExpectEmpty();
            }

            void ExpectEmpty() => Require(!directory.EnumerateFileSystemInfos().Any(), "Screenshot preview and cancel must leave the destination empty.");
        }
        finally
        {
            owner.Close();
            lastDirectory.SetValue(null, savedDirectory);
            directory.Delete(recursive: true);
        }
    }

    private static void ChooseFile(ScreenshotDialog preview, string? expectedFilename)
    {
        var owner = new WindowInteropHelper(preview).Handle;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        // A native Save As dialog pumps Win32 messages. Drive only the popup owned by
        // this preview from an MTA client and dismiss it if any assertion fails.
        using var timeout = deadline.Token.Register(() => ClosePicker());
        var client = Task.Run(() =>
        {
            try
            {
                var buttonId = expectedFilename is null ? 2 : 1; // IDCANCEL / IDOK
                IntPtr picker;
                IntPtr button;
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    picker = GetWindow(owner, 6); // GW_ENABLEDPOPUP
                    button = picker == IntPtr.Zero || picker == owner ? IntPtr.Zero : GetDlgItem(picker, buttonId);
                    if (button != IntPtr.Zero)
                    {
                        break;
                    }

                    Thread.Sleep(25);
                }

                if (expectedFilename is not null)
                {
                    var native = AutomationElement.FromHandle(picker);
                    // Accept the default name shown by the shell in the isolated save directory.
                    var stem = Path.GetFileNameWithoutExtension(expectedFilename);
                    while (true)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        var edits = native.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                        if (edits.Cast<AutomationElement>().Any(edit => edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                            && pattern is ValuePattern value && (value.Current.Value == expectedFilename || value.Current.Value == stem)))
                        {
                            break;
                        }

                        Thread.Sleep(25);
                    }
                }

                // Post the button's notification so the client does not wait for its own modal dialog to close.
                Require(PostMessage(picker, 0x0111, new IntPtr(buttonId), button), "Could not activate the native file dialog button."); // WM_COMMAND
            }
            catch
            {
                ClosePicker();
                throw;
            }
        });

        Click(preview, ScreenshotDialog.SaveCommand);
        client.GetAwaiter().GetResult();
        deadline.Token.ThrowIfCancellationRequested();

        void ClosePicker()
        {
            var popup = GetWindow(owner, 6);
            if (popup != IntPtr.Zero && popup != owner)
            {
                PostMessage(popup, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }
    }

    private static Button FindButton(FrameworkElement view, RoutedCommand command) =>
        InspectorTableSmoke.Descendants(view).OfType<Button>().Single(button => button.Command == command && button.IsVisible && button.IsEnabled);

    private static void Click(FrameworkElement view, RoutedCommand command) => command.Execute(null, FindButton(view, command));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetDlgItem(IntPtr window, int id);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
