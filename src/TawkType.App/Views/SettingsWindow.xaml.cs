using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TawkType.Desktop.Services;
using TawkType.Desktop.ViewModels;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Every page lives in the same scroll viewer, stacked and switched by visibility, so the offset
    /// is shared. Scrolling down one page and then picking another from the nav rail arrived part way
    /// down the new one — or past the end of it, on a page shorter than the last.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedPage))
        {
            PageScroll.ScrollToTop();
        }

        // Gotcha 52 one level down: one ListBox serves all three vocabulary lists, so switching tab
        // swaps its ItemsSource and it keeps the offset from the list before — the same bug as the
        // shared page scroller, in a smaller box.
        if (e.PropertyName == nameof(SettingsViewModel.ActiveList))
        {
            ScrollVocabularyToTop();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);
    }

    private void ScrollVocabularyToTop()
    {
        if (VocabularyPage.IsVisible
            && FindVisualChild<ScrollViewer>(VocabularyPage) is { } scroller)
        {
            scroller.ScrollToTop();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }

            if (FindVisualChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>
    /// Escape in the search box clears the search and goes no further.
    ///
    /// Without the handled flag it bubbles to <see cref="OnKeyDown"/>, which closes the window — and
    /// closing discards every unsaved edit on every page. Clearing a search is not a reason to throw
    /// away somebody's work, and nothing on screen would have warned them.
    /// </summary>
    private void OnVocabularySearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.ActiveList.Search = string.Empty;
        }
    }

    /// <summary>Enter opens the selected row for editing, Delete removes it. Arrow keys come free.</summary>
    private void OnVocabularyListKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not VocabularyRow row)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                row.Section.EditCommand.Execute(row);
                break;

            case Key.Delete:
                e.Handled = true;
                row.Section.RemoveCommand.Execute(row);
                break;
        }
    }

    /// <summary>
    /// Double-click edits; a single click only selects. With eighty rows, a click that throws a modal
    /// is hostile — and the pencil is still there for anyone who wants one click.
    /// </summary>
    private void OnVocabularyListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: VocabularyRow row })
        {
            row.Section.EditCommand.Execute(row);
        }
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
