using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using TawkType.Core.Settings;
using TawkType.Core.Text;
using TawkType.Desktop.Services;
using TawkType.Desktop.ViewModels;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

/// <summary>
/// The always-available dictation log. Closing it hides it rather than tearing it down, so reopening
/// from the tray is instant and the view model keeps following new dictations either way.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;
    private readonly HistorySettings _placement;

    public HistoryWindow(HistoryViewModel viewModel, HistorySettings placement)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _placement = placement;

        Width = placement.WindowWidth;
        Height = placement.WindowHeight;

        // The real positioning happens in OnSourceInitialized: it needs the window handle to work out
        // which monitor the saved point is on, and whether that monitor is still connected.
        WindowStartupLocation = placement.WindowLeft is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.Manual;

        SetBinding(TopmostProperty, new Binding(nameof(HistoryViewModel.AlwaysOnTop)));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Opens the correction dialog for a row, seeded from whatever the user has selected.
    ///
    /// The selection is the reason this lives in code-behind: the natural gesture is to highlight the
    /// words that came out wrong and press the button, and a TextBox's selection is not something a
    /// binding can reach. Selected raw text becomes the phrase to listen for; selected final text
    /// becomes what to type instead.
    /// </summary>
    private void OnRememberClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryEntry entry })
        {
            return;
        }

        var row = FindRow(sender as DependencyObject);
        var heard = Selected(row, "HeardBox");
        var typed = Selected(row, "DraftBox");

        // With nothing selected, guess: the words that differ between what was heard and what is
        // there now. Offering both whole sentences would save a rule that only ever fires on that
        // exact sentence again, which is close to useless.
        var guess = heard is null || typed is null
            ? CorrectionGuess.Between(heard ?? entry.RawText, typed ?? entry.Draft)
            : new Correction(heard, typed);

        new RememberWindow(_viewModel, guess.Heard.Trim(), guess.Typed.Trim()) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// Takes up the offer made after an edit was saved: the same dialog Remember… opens, with the
    /// words that changed already in it.
    ///
    /// It goes through the dialog rather than saving directly because a replacement applies to every
    /// dictation from now on, and one click on a strip the user did not ask for is not enough consent
    /// for that. The strip clears itself when the dialog saves — see <c>LearnReplacement</c>.
    /// </summary>
    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Suggestion is not { } correction)
        {
            return;
        }

        new RememberWindow(_viewModel, correction.Heard, correction.Typed) { Owner = this }.ShowDialog();
    }

    /// <summary>The selected text in a named box inside this row, or null when nothing is selected.</summary>
    private static string? Selected(DependencyObject? row, string name)
    {
        if (row is null)
        {
            return null;
        }

        var box = FindDescendant<TextBox>(row, name);
        return string.IsNullOrWhiteSpace(box?.SelectedText) ? null : box!.SelectedText;
    }

    private static DependencyObject? FindRow(DependencyObject? from)
    {
        while (from is not null and not ListBoxItem)
        {
            from = VisualTreeHelper.GetParent(from);
        }

        return from;
    }

    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindDescendant<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Set on shutdown so the last close actually closes instead of hiding.</summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Escape goes back to the list, and then out of the window.
    ///
    /// Bubbling rather than tunnelling, so a control that wants Escape for itself gets it first — the
    /// text box in an expanded row is the one that matters, since somebody pressing Escape in the
    /// middle of an edit means the edit.
    ///
    /// The global Escape that cancels a dictation is a keyboard hook and is unrelated: it only fires
    /// while a dictation is actually running.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is HistoryViewModel viewModel)
        {
            e.Handled = true;

            if (viewModel.Selected is not null)
            {
                viewModel.CollapseCommand.Execute(null);
                return;
            }

            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

        if (_placement.WindowLeft is { } left && _placement.WindowTop is { } top
            && !RememberedPlacement.TryRestore(this, left, top))
        {
            CentreOnPrimary();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();

        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosing(e);
    }

    /// <summary>A monitor was unplugged, switched off, or rearranged. Rescue the window if stranded.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (!RememberedPlacement.EnsureOnScreen(this))
            {
                CentreOnPrimary();
            }
        });

    /// <summary>The primary monitor is the one place guaranteed to exist.</summary>
    private void CentreOnPrimary()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + ((area.Height - Height) / 2);
    }

    private void SavePlacement()
    {
        // Size stays in WPF units; the position is captured in physical pixels so it can be checked
        // against the real monitor rectangles next time, without any DPI conversion.
        var size = WindowState == WindowState.Normal
            ? new Size(Width, Height)
            : new Size(RestoreBounds.Width, RestoreBounds.Height);

        if (size.Width > 0 && size.Height > 0 && RememberedPlacement.Capture(this) is { } spot)
        {
            _viewModel.SavePlacement(spot.Left, spot.Top, size.Width, size.Height);
        }
    }
}
