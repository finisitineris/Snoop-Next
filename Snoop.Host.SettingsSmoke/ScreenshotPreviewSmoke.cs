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
using Snoop.Infrastructure;
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
        var target = new Border
        {
            Name = "CaptureTarget",
            Width = 64,
            Height = 32,
            Background = Brushes.Coral,
            RenderTransform = new ScaleTransform(1.5, 1.25),
            Child = new Border { Width = 32, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.SteelBlue },
        };
        var evidence = Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR");
        var evidenceDirectory = string.IsNullOrEmpty(evidence) ? null : Path.GetFullPath(evidence);
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
            if (evidenceDirectory is not null)
            {
                Directory.CreateDirectory(evidenceDirectory);
                File.Copy(destination, Path.Combine(evidenceDirectory, "screenshot-confirmed-96dpi.png"), overwrite: true);
            }

            using var stream = File.OpenRead(destination);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Single();
            var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var left = Sample(bitmap, 0.25);
            var seam = Sample(bitmap, 0.5);
            var right = Sample(bitmap, 0.75);
            var offset = VisualTreeHelper.GetOffset(target);
            var details = $"PNG {frame.PixelWidth}x{frame.PixelHeight} at {frame.DpiX:F2}x{frame.DpiY:F2} DPI, target {target.RenderSize}, offset {offset}, left/seam/right {left}/{seam}/{right}.";
            Console.WriteLine(details);
            Require(offset.X > target.ActualWidth && frame.PixelWidth == 64 && frame.PixelHeight == 32
                    && Math.Abs(frame.DpiX - 96) < 0.1 && Math.Abs(frame.DpiY - 96) < 0.1
                    && left == Colors.Coral && seam == Colors.SteelBlue && right == Colors.SteelBlue,
                "The saved PNG must contain both halves of the offset visual at 96 DPI. " + details);
            VerifyTransformedDrawing();
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

    private static void VerifyTransformedDrawing()
    {
        // Non-UI visuals can draw at a nonzero local origin and have a root transform.
        var drawing = new DrawingVisual { Offset = new Vector(18, 23), Transform = new MatrixTransform(0, 2, -1, 0, 0, 0) };
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.Coral, null, new Rect(31, 7, 6, 8));
            context.DrawRectangle(Brushes.SteelBlue, null, new Rect(37, 7, 6, 8));
        }

        var image = VisualCaptureUtil.RenderVisualWithHighQuality(drawing, 96, 96);
        var top = Sample(image, 0.5, 0.25);
        var bottom = Sample(image, 0.5, 0.75);
        Require(image.PixelWidth == 12 && image.PixelHeight == 8 && top == Colors.Coral && bottom == Colors.SteelBlue,
            $"A rotated and scaled drawing must retain its local image dimensions and display the rotated color bands, got {image.PixelWidth}x{image.PixelHeight} {top}/{bottom}.");
    }

    private static Color Sample(BitmapSource image, double fractionX, double fractionY = 0.5)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect((int)(image.PixelWidth * fractionX), (int)(image.PixelHeight * fractionY), 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
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
