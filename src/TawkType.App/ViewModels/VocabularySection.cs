using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TawkType.Core.Settings;
using TawkType.Desktop.Views;

namespace TawkType.Desktop.ViewModels;

/// <summary>How the list is shown. Never how it is stored — see <see cref="VocabularySection.Sort"/>.</summary>
public enum VocabularySort
{
    AsEntered,
    Ascending,
    Descending,
}

/// <summary>
/// One entry as the list shows it.
///
/// <see cref="ToString"/> is overridden because a screen reader and any automation script read that,
/// not the template — gotcha 42, which has already happened twice. The Edit and Remove buttons carry
/// their own names for the same reason: eight buttons all announcing "Remove" is not a list anybody
/// can use without looking at it.
/// </summary>
public sealed class VocabularyRow(
    VocabularySection section,
    int index,
    string primary,
    string? secondary,
    string? tooltip)
{
    /// <summary>
    /// The list this row belongs to, so Edit and Remove can be bound straight to it.
    ///
    /// The row buttons used to reach the section by walking up to the enclosing `ContentControl` and
    /// taking its `DataContext` — which is not the section. Setting `Content` does not set
    /// `DataContext`: the template's context becomes the content, the control's stays whatever it
    /// inherited. So the binding silently resolved against the settings view model, found no command,
    /// and both buttons did nothing at all while looking perfectly normal.
    /// </summary>
    public VocabularySection Section { get; } = section;

    /// <summary>Position in the underlying list. Rows are rebuilt after every change, so it is never stale.</summary>
    public int Index { get; } = index;

    public string Primary { get; } = primary;

    public string? Secondary { get; } = secondary;

    public string? Tooltip { get; } = tooltip;

    public bool HasSecondary => Secondary is not null;

    public string EditName => $"Edit {Primary}";

    public string RemoveName => $"Remove {Primary}";

    public override string ToString() => Secondary is null ? Primary : $"{Primary} — {Secondary}";
}

/// <summary>
/// One of the three vocabulary lists, in either of the two ways it can be edited.
///
/// The structured list is the real thing and the text box is a view onto it, not the other way round.
/// Entering text mode formats from the list; leaving it parses back, and refuses to leave while a line
/// is not an entry, because a text box that is a projection of a list cannot afford to drop a line in
/// silence — that is a rule the user wrote, saved, and never sees again.
///
/// Both paths go through <see cref="VocabularyRules"/>, which is the point: the dialogs used to be the
/// only careful way in.
/// </summary>
public abstract partial class VocabularySection : ObservableObject
{
    private readonly Action<string?> _changed;

    protected VocabularySection(string singular, string plural, string hint, Action<string?> changed)
    {
        Singular = singular;
        Plural = plural;
        Hint = hint;
        _changed = changed;
    }

    public string Singular { get; }

    public string Plural { get; }

    /// <summary>The heading. Capitalised; <see cref="Plural"/> is for the middle of a sentence.</summary>
    public string Title => char.ToUpperInvariant(Plural[0]) + Plural[1..];

    /// <summary>The singular, capitalised, for the start of a sentence about one entry.</summary>
    public string SingularTitle => char.ToUpperInvariant(Singular[0]) + Singular[1..];

    /// <summary>What this list is for, in one sentence. The syntax is not in it — that is the dialog's job.</summary>
    public string Hint { get; }

    public string AddLabel => $"Add a {Singular}";

    public ObservableCollection<VocabularyRow> Rows { get; } = [];

    /// <summary>What the empty list says. Why the thing exists, not that there is nothing in it.</summary>
    public abstract string EmptyText { get; }

    /// <summary>The syntax hint, shown only in text mode — the one place it is true and the one place it is needed.</summary>
    public abstract string TextHint { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isTextMode;

    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>The line that is not an entry, or null. Blocks Save as well as leaving text mode.</summary>
    [ObservableProperty]
    private string? _problem;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>True when this is the list on screen. The others are not rendered at all.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Filters the rows shown. The stored list is untouched — which is why text mode is refused while
    /// this is set: a text box showing only the matching lines, committed, would replace the whole
    /// list with them and delete everything that did not match, silently.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(CanEditAsText))]
    [NotifyPropertyChangedFor(nameof(TextModeBlockedReason))]
    [NotifyPropertyChangedFor(nameof(EmptySearchText))]
    private string _search = string.Empty;

    /// <summary>How the rows are ordered on screen. A view, never written to the vocabulary.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    [NotifyPropertyChangedFor(nameof(IsSorted))]
    private VocabularySort _sort;

    /// <summary>How many entries the list actually holds, whatever the search is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabLabel))]
    private int _total;

    public bool IsSearching => Search.Length > 0;

    public bool IsSorted => Sort != VocabularySort.AsEntered;

    /// <summary>Blocked while searching, because committing a filtered box would delete the rest.</summary>
    public bool CanEditAsText => !IsSearching;

    public string? TextModeBlockedReason
        => IsSearching ? "Clear the search first — text mode shows the whole list." : null;

    public string TabLabel => IsSearching ? $"{Title}  {Rows.Count}/{Total}" : $"{Title}  {Total}";

    public string EmptySearchText => $"No {Plural} match “{Search}”.";

    public string SortLabel => Sort switch
    {
        VocabularySort.Ascending => "A to Z",
        VocabularySort.Descending => "Z to A",
        _ => "As entered",
    };

    /// <summary>What the two columns are called. The dialogs' words, so one idea has one vocabulary.</summary>
    public abstract string PrimaryHeader { get; }

    public abstract string? SecondaryHeader { get; }

    /// <summary>
    /// The header band and every row share these, which is what lines them up. Star widths at equal
    /// ratios align exactly; SharedSizeGroup does not work on star columns, so do not reach for it.
    /// </summary>
    public GridLength PrimaryWidth => new(1, GridUnitType.Star);

    public GridLength SecondaryWidth
        => SecondaryHeader is null ? new GridLength(0) : new GridLength(1.4, GridUnitType.Star);

    public string ToggleLabel => IsTextMode ? "Done editing text" : "Edit as text";

    /// <summary>Rebuilds the rows and the count from the draft. Called after anything changes the list.</summary>
    public void Refresh()
    {
        // Built first, so every row carries its position in the stored list. Filtering and sorting are
        // applied to the rows afterwards and never to the source: Index has to keep pointing at the
        // real entry or Edit and Remove would act on whatever happened to be in that slot.
        var all = BuildRows().ToList();
        Total = all.Count;

        IEnumerable<VocabularyRow> shown = all;

        if (IsSearching)
        {
            shown = shown.Where(row => Matches(row, Search));
        }

        shown = Sort switch
        {
            VocabularySort.Ascending => shown.OrderBy(row => row.Primary, StringComparer.CurrentCultureIgnoreCase),
            VocabularySort.Descending => shown.OrderByDescending(row => row.Primary, StringComparer.CurrentCultureIgnoreCase),
            _ => shown,
        };

        Rows.Clear();
        foreach (var row in shown)
        {
            Rows.Add(row);
        }

        IsEmpty = Total == 0;
        NoMatches = Total > 0 && Rows.Count == 0;

        CountText = Total switch
        {
            0 => $"no {Plural} yet",
            1 => $"1 {Singular}",
            _ => $"{Total} {Plural}",
        };

        OnPropertyChanged(nameof(TabLabel));
    }

    /// <summary>True when the list holds something but the search has hidden all of it.</summary>
    [ObservableProperty]
    private bool _noMatches;

    private static bool Matches(VocabularyRow row, string search)
        => row.Primary.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || (row.Secondary?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnSortChanged(VocabularySort value) => Refresh();

    /// <summary>As entered, then A to Z, then back. Never touches the stored order.</summary>
    [RelayCommand]
    private void CycleSort()
        => Sort = Sort switch
        {
            VocabularySort.AsEntered => VocabularySort.Ascending,
            VocabularySort.Ascending => VocabularySort.Descending,
            _ => VocabularySort.AsEntered,
        };

    [RelayCommand]
    private void ClearSearch() => Search = string.Empty;

    [RelayCommand]
    private void ToggleTextMode()
    {
        if (!IsTextMode)
        {
            if (IsSearching)
            {
                return;
            }

            Text = FormatText();
            Problem = null;
            IsTextMode = true;
            return;
        }

        if (!Commit())
        {
            return;
        }

        IsTextMode = false;
        Refresh();
    }

    /// <summary>
    /// Parses the text box back into the list. False when a line is not an entry, in which case the
    /// list is untouched and <see cref="Problem"/> names the line.
    /// </summary>
    public bool Commit()
    {
        if (!IsTextMode)
        {
            return true;
        }

        var rejected = TryCommit(Text);
        if (rejected.Length == 0)
        {
            Problem = null;
            Refresh();
            return true;
        }


        // The format is on the hint directly above this, so the message names the line and stops.
        Problem = rejected.Length == 1
            ? $"Line {rejected[0]} is not a {Singular}. Fix it, or take the line out."
            : $"{rejected.Length} lines are not {Plural} — the first is line {rejected[0]}. Fix them, or take them out.";

        return false;
    }

    [RelayCommand]
    private void Add(Window? owner)
    {
        if (!ShowAddDialog(owner))
        {
            return;
        }

        // Otherwise they add an entry and watch nothing happen, because it does not match the search.
        var wasSearching = IsSearching;
        Search = string.Empty;

        Refresh();
        Announce(Done("added", wasSearching ? $"Search cleared so you can see the new {Singular}." : null));
    }

    [RelayCommand]
    private void Edit(VocabularyRow? row)
    {
        if (row is not null && ShowEditDialog(row, Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault()))
        {
            Refresh();
            Announce(Done("updated", null));
        }
    }

    /// <summary>
    /// Removing one entry does not ask, matching the history window: it is small, obviously scoped, and
    /// nothing here is on disk until Save, which Cancel still discards wholesale.
    /// </summary>
    [RelayCommand]
    private void Remove(VocabularyRow? row)
    {
        if (row is null)
        {
            return;
        }

        RemoveAt(row.Index);
        Refresh();
        Announce(Done("removed", null));
    }

    /// <summary>
    /// Drops the text box without parsing it. For Import, which has just replaced the list the box was
    /// a view of: parsing it afterwards would put the replaced vocabulary straight back.
    /// </summary>
    public void LeaveTextMode()
    {
        IsTextMode = false;
        Problem = null;
    }

    /// <summary>Tells the owner something changed, with a sentence to flash when there is one to say.</summary>
    protected void Announce(string? note) => _changed(note);

    /// <summary>
    /// What to say after a dialog or a Remove. Every one of them ends in the same five words.
    ///
    /// The dialog's own button says the change is made and the row updates the moment it closes, which
    /// is true of the draft and not of the disk — the Settings window's Save is still what writes it,
    /// and Cancel still throws it away. Import already said so; these three said nothing at all, which
    /// is how "it says Saved but it is not really" happens.
    /// </summary>
    private string Done(string what, string? aside)
        => aside is null
            ? $"{SingularTitle} {what}. Save to keep it."
            : $"{SingularTitle} {what}. {aside} Save to keep it.";

    protected abstract IEnumerable<VocabularyRow> BuildRows();

    protected abstract string FormatText();

    /// <summary>Parses and commits, or returns the 1-based lines that were not entries.</summary>
    protected abstract int[] TryCommit(string text);

    protected abstract bool ShowAddDialog(Window? owner);

    protected abstract bool ShowEditDialog(VocabularyRow row, Window? owner);

    protected abstract void RemoveAt(int index);

    protected static bool Show(Window dialog, Window? owner)
    {
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog switch
        {
            VocabularyEntryWindow entry => entry.Saved,
            SnippetWindow snippet => snippet.Saved,
            _ => false,
        };
    }
}
