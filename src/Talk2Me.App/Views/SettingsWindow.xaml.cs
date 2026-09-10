using System.Windows;
using System.Windows.Controls;
using Talk2Me.Desktop.ViewModels;

namespace Talk2Me.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
    }

    // PasswordBox deliberately has no bindable Password property, so the value is pushed by hand.
    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
        => ((SettingsViewModel)DataContext).SetPendingApiKey(((PasswordBox)sender).Password);
}
