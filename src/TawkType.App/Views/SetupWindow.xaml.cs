using System.Windows;
using System.Windows.Interop;
using TawkType.Core.Onboarding;
using TawkType.Desktop.Services;
using TawkType.Desktop.ViewModels;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

/// <summary>
/// First run. Unlike Settings, every answer here is saved as it is given — the steps that follow
/// depend on it — so there is nothing to cancel and no Cancel button.
///
/// Escape is deliberately not wired to close. The hotkey recorder uses it to abandon a recording, a
/// dictation uses it to cancel, and a stray press should not throw away a setup halfway through.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        viewModel.Finished += (_, _) =>
        {
            AllowClose = true;
            Close();
        };

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The microphone step listens while it is open. A window hidden by its close button must not
        // go on holding the device.
        IsVisibleChanged += (_, args) => viewModel.SetOnScreen(args.NewValue is true);
    }

    /// <summary>
    /// Set when the flow ends. Until then the close button hides the window rather than destroying it,
    /// so a user who clicks it by mistake has not lost the step they were on.
    /// </summary>
    public bool AllowClose { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// The practice box has to hold the caret before the user reaches for the key: the words are typed
    /// into whatever has focus, and this window is the thing they are looking at.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupViewModel.Step) || _viewModel.Step != SetupStep.Practice)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            PracticeBox.Focus();
            PracticeBox.CaretIndex = PracticeBox.Text.Length;
        });
    }
}
