using System.Windows;
using System.Windows.Interop;
using TawkType.Desktop.Services;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

/// <summary>What one of these dialogs should ask for, and what to do with the answer.</summary>
/// <param name="Commit">
/// Returns a sentence to show when the entry cannot be kept, or null once it has been saved. The
/// rules live in Core and this dialog only repeats what they said, so the box, the imported file and
/// this window cannot disagree about what a valid entry is.
/// </param>
public sealed record VocabularyEntryPrompt(
    string Title,
    string Blurb,
    string FirstLabel,
    string FirstHint,
    string FirstValue,
    string? SecondLabel,
    string? SecondHint,
    string SecondValue,
    string PrimaryLabel,
    Func<string, string, string?> Commit);

/// <summary>
/// Adding or editing a spelling or a replacement.
///
/// One window for both because they differ by one field, and because the separator they used to be
/// written with — <c>heard =&gt; typed</c> — is exactly the thing nobody should have to remember. The
/// shape of an entry is the dialog's job now.
///
/// Shown with <c>ShowDialog</c> and an owner, so <c>IsCancel</c> and <c>IsDefault</c> both work: gotcha
/// 24 is about the modeless Settings window, not this one. Note for anyone scripting it: an owned
/// dialog is a UIA descendant of its owner rather than a child of the root (gotcha 38).
/// </summary>
public partial class VocabularyEntryWindow : Window
{
    private readonly VocabularyEntryPrompt _prompt;

    public VocabularyEntryWindow(VocabularyEntryPrompt prompt)
    {
        InitializeComponent();
        _prompt = prompt;

        Title = prompt.Title;
        Heading.Text = prompt.Title;
        Blurb.Text = prompt.Blurb;

        FirstLabel.Text = prompt.FirstLabel;
        FirstHint.Text = prompt.FirstHint;
        FirstBox.Text = prompt.FirstValue;

        PrimaryButton.Content = prompt.PrimaryLabel;

        if (prompt.SecondLabel is null)
        {
            SecondLabel.Visibility = Visibility.Collapsed;
            SecondBox.Visibility = Visibility.Collapsed;
            SecondHint.Visibility = Visibility.Collapsed;
            FirstHint.Margin = new Thickness(0, 4, 0, 0);
        }
        else
        {
            SecondLabel.Text = prompt.SecondLabel;
            SecondHint.Text = prompt.SecondHint ?? string.Empty;
            SecondBox.Text = prompt.SecondValue;
        }
    }

    /// <summary>True when something was saved, so the caller knows whether to rebuild its list.</summary>
    public bool Saved { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

        // Editing an existing entry almost always means changing the second half, the way the Remember
        // window already assumes. Adding starts at the beginning.
        var editing = _prompt.FirstValue.Length > 0;
        var box = editing && SecondBox.Visibility == Visibility.Visible ? SecondBox : FirstBox;

        box.Focus();
        box.SelectAll();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var problem = _prompt.Commit(FirstBox.Text, SecondBox.Text);
        if (problem is not null)
        {
            Problem.Text = problem;
            Problem.Visibility = Visibility.Visible;

            // Put them back where the trouble is rather than leaving them to find it.
            var blank = FirstBox.Text.Trim().Length == 0;
            (blank ? FirstBox : SecondBox).Focus();
            return;
        }

        Saved = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
