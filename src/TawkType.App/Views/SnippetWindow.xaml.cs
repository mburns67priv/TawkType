using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TawkType.Core.Text;
using TawkType.Desktop.Services;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

/// <summary>
/// Adding or editing a snippet.
///
/// Its own window rather than a field on the other one, because the text runs to several lines and
/// that changes everything about the dialog: it has a real height, Enter belongs to the text area
/// rather than to a default button, and the trigger needs a preview saying what to say out loud.
///
/// This is where the <c>\n</c> escape died. The user presses Enter and gets a line break; the literal
/// two-character escape is a thing the text box format does and nobody has to type.
/// </summary>
public partial class SnippetWindow : Window
{
    private readonly Func<string, string, string?> _commit;

    public SnippetWindow(string title, string trigger, string text, string primaryLabel, Func<string, string, string?> commit)
    {
        InitializeComponent();
        _commit = commit;

        Title = title;
        Heading.Text = title;
        PrimaryButton.Content = primaryLabel;

        TriggerBox.Text = trigger;
        TextBoxField.Text = text;

        // Ctrl+Enter saves from anywhere in the dialog, since the primary button cannot be the default.
        InputBindings.Add(new KeyBinding(new SaveCommand(this), Key.Enter, ModifierKeys.Control));

        UpdatePreview();
    }

    /// <summary>True when something was saved, so the caller knows whether to rebuild its list.</summary>
    public bool Saved { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

        var box = TriggerBox.Text.Length > 0 ? TextBoxField : TriggerBox;
        box.Focus();
        box.SelectAll();
    }

    private void OnTriggerChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    /// <summary>
    /// Enter in the trigger moves to the text rather than saving. There is no default button to fire,
    /// but without this Enter would do nothing at all, which reads as a dead key.
    /// </summary>
    private void OnTriggerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            TextBoxField.Focus();
        }
    }

    private void UpdatePreview()
    {
        var trigger = TriggerBox.Text.Trim();

        SayPreview.Text = trigger.Length == 0
            ? $"You will say: {PhraseBook.SnippetPrefix} and then your trigger"
            : $"You will say: {PhraseBook.SnippetPrefix} {trigger}";
    }

    private void Save()
    {
        var problem = _commit(TriggerBox.Text, TextBoxField.Text);
        if (problem is not null)
        {
            Problem.Text = problem;
            Problem.Visibility = Visibility.Visible;
            (TriggerBox.Text.Trim().Length == 0 ? TriggerBox : TextBoxField).Focus();
            return;
        }

        Saved = true;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private sealed class SaveCommand(SnippetWindow window) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => window.Save();
    }
}
