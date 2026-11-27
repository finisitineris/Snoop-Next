namespace Snoop.Views;

using System;
using System.ComponentModel;
using System.Windows;
using Snoop.Infrastructure;
using Snoop.Next.Localization;

public partial class AttachFailureDialog
{
    public AttachFailureDialog(string titleKey, string target, Exception? exception)
    {
        this.InitializeComponent();

        var text = LocalizationManager.Default;
        this.Title = text[titleKey];
        this.FailureCaption.Text = text.Format("Chooser.AttachFailureDetail", target);
        var guidanceKey = exception switch
        {
            Win32Exception { NativeErrorCode: 5 } => "Chooser.AttachAccessDenied",
            Win32Exception { NativeErrorCode: 1223 } => "Chooser.AttachElevationCanceled",
            _ => "Chooser.AttachFailureGuidance"
        };
        this.FailureGuidance.Text = text[guidanceKey];
        if (exception is not null)
        {
            this.FailureDetails.Text = LogHelper.WriteError(exception);
        }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = true;
    }
}
