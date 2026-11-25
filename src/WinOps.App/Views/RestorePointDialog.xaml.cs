using System.Windows;

namespace WinOps.App.Views;

public partial class RestorePointDialog : Window
{
    public RestorePointDialog()
    {
        InitializeComponent();
    }

    public string Description => DescriptionBox.Text.Trim();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            MessageBox.Show("Enter a description before creating the restore point.", "WinOps Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
