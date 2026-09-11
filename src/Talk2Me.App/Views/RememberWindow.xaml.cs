using System.Windows;
using System.Windows.Interop;
using Talk2Me.Desktop.Services;
using Talk2Me.Desktop.ViewModels;
using Talk2Me.Windows.Shell;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// Saves a vocabulary replacement from a dictation that came out wrong.
///
/// Two boxes and nothing else. It is opened with the phrase the user had selected already in the first
/// one, so the common case — highlight the wrong words, say what they should have been — is one field
/// of typing. A dialog rather than a silent save because a replacement applies to everything from now
/// on, and the user should see both halves of it before that starts.
/// </summary>
public partial class RememberWindow : Window
{
    private readonly HistoryViewModel _history;

    public RememberWindow(HistoryViewModel history, string heard, string typed)
    {
        InitializeComponent();
        _history = history;
        HeardBox.Text = heard;
        TypedBox.Text = typed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

        // The half that needs changing is almost always the second one.
        TypedBox.Focus();
        TypedBox.SelectAll();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_history.LearnReplacement(HeardBox.Text, TypedBox.Text) is { } problem)
        {
            Problem.Text = problem;
            Problem.Visibility = Visibility.Visible;
            return;
        }

        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
