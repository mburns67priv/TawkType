using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TawkType.Core.Settings;
using TawkType.Desktop.Views;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// One entry as the list shows it.
///
/// <see cref="ToString"/> is overridden because a screen reader and any automation script read that,
/// not the template — gotcha 42, which has already happened twice. The Edit and Remove buttons carry
/// their own names for the same reason: eight buttons all announcing "Remove" is not a list anybody
/// can use without looking at it.
/// </summary>
public sealed class VocabularyRow(int index, string primary, string? secondary, string? tooltip)
{
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

    public string ToggleLabel => IsTextMode ? "Done editing text" : "Edit as text";

    /// <summary>Rebuilds the rows and the count from the draft. Called after anything changes the list.</summary>
    public void Refresh()
    {
        Rows.Clear();
        foreach (var row in BuildRows())
        {
            Rows.Add(row);
        }

        IsEmpty = Rows.Count == 0;
        CountText = Rows.Count switch
        {
            0 => $"no {Plural} yet",
            1 => $"1 {Singular}",
            _ => $"{Rows.Count} {Plural}",
        };
    }

    [RelayCommand]
    private void ToggleTextMode()
    {
        if (!IsTextMode)
        {
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
        if (ShowAddDialog(owner))
        {
            Refresh();
            Announce(null);
        }
    }

    [RelayCommand]
    private void Edit(VocabularyRow? row)
    {
        if (row is not null && ShowEditDialog(row, Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault()))
        {
            Refresh();
            Announce(null);
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
        Announce(null);
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
