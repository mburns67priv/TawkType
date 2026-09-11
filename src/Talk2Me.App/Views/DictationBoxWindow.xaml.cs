using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Talk2Me.Desktop.Services;
using Talk2Me.Desktop.ViewModels;
using Talk2Me.Windows.Shell;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// A scratchpad for a dictation that did not arrive. Closing it hides it, like the history window, so
/// the text is still there if the user shut it by reflex and then wanted it back.
/// </summary>
public partial class DictationBoxWindow : Window
{
    private readonly DictationBoxViewModel _viewModel;

    public DictationBoxWindow(DictationBoxViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _viewModel.Finished += OnFinished;
    }

    /// <summary>Set on shutdown so the last close actually closes instead of hiding.</summary>
    public bool AllowClose { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// The text reached its window, so this one gets out of the way. It hides rather than closing for
    /// the same reason as the Close button.
    /// </summary>
    private void OnFinished(object? sender, EventArgs e) => Hide();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
