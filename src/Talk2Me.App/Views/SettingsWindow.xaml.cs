using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Talk2Me.Desktop.Services;
using Talk2Me.Desktop.ViewModels;
using Talk2Me.Windows.Shell;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// Settings. Everything the pages edit lives on a cloned <see cref="SettingsViewModel.Draft"/>, so
/// closing without saving discards the lot — there is nothing to roll back.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Escape closes too. A modeless window gets none of the dialog key handling for free, so this is
    /// wired by hand rather than left to the Cancel button's IsCancel.
    ///
    /// Bubbling, not tunnelling: a control that wants Escape for itself — an open combo box dropdown —
    /// handles it first and the window never sees it.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    // PasswordBox deliberately has no bindable Password property, so the value is pushed by hand.
    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
        => ((SettingsViewModel)DataContext).SetPendingApiKey(((PasswordBox)sender).Password);
}
