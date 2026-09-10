using System.Windows;
using Murmur.Desktop.ViewModels;

namespace Murmur.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
    }
}
