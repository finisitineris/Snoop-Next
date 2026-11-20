namespace Snoop.Next.Localization.Demo;

using System.Threading;
using System.Windows;
using System.Windows.Controls;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.UpdateCultureLabel();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo
            && combo.SelectedItem is ComboBoxItem item
            && item.Tag is string language)
        {
            LocalizationManager.Default.SetLanguage(language);
            this.UpdateCultureLabel();
        }
    }

    private void UpdateCultureLabel()
    {
        if (this.CultureLabel != null)
        {
            this.CultureLabel.Text =
                "Current thread UI Culture (unchanged): "
                + Thread.CurrentThread.CurrentUICulture.Name;
        }
    }
}
