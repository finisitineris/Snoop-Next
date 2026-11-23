namespace Snoop.Infrastructure.Helpers;

using System;
using System.Windows;
using Snoop.Localization;
using Snoop.Windows;

public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (Exception exception)
        {
            var localization = InspectorLocalization.Default;
            ErrorDialog.ShowExceptionMessageBox(exception, localization["Clipboard.CopyFailedTitle"], localization["Clipboard.CopyFailedMessage"]);
        }
    }
}
