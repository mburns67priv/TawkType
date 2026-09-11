using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.History;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Desktop.ViewModels;

/// <summary>
/// One row in the history list.
///
/// <see cref="Draft"/> is what the text box is bound to, kept apart from the record so an edit can be
/// abandoned. Nothing is written until the user says so: a log they are reading through should not
/// change under them because they clicked in it.
/// </summary>
public sealed partial class HistoryEntry(DictationRecord record) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdited))]
    private string _draft = record.FinalText.Trim();

    [ObservableProperty]
    private bool _isBusy;

    public DictationRecord Record { get; private set; } = record;

    public string TimeText => Record.At.ToString("HH:mm");

    public string DayText => Record.At.LocalDateTime.Date == DateTime.Today
        ? "Today"
        : Record.At.ToString("d MMM");

    public string FinalText => Record.FinalText.Trim();

    public string RawText => Record.RawText.Trim();

    public bool ShowRaw => Record.WasCleaned && RawText.Length > 0;

    /// <summary>Drives Save and Revert, so they are only there when there is something to do.</summary>
    public bool IsEdited => !string.Equals(Draft, FinalText, StringComparison.Ordinal);

    public string MetaText =>
        $"{Record.Engine} · {Record.AudioSeconds:F1}s audio · {Record.TranscriptionMs} ms";

    /// <summary>Takes a saved record as the new truth, so the row stops reading as edited.</summary>
    public void Accept(DictationRecord saved)
    {
        Record = saved;
        Draft = saved.FinalText.Trim();
        OnPropertyChanged(nameof(FinalText));
        OnPropertyChanged(nameof(ShowRaw));
        OnPropertyChanged(nameof(IsEdited));
    }

    public void Revert() => Draft = FinalText;
}

/// <summary>
/// Drives the history window. It is meant to sit on screen all day, so it refreshes itself from
/// <see cref="IDictationHistory"/> as dictations land rather than being rebuilt on each open.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IDictationHistory _history;
    private readonly SettingsStore _settings;
    private readonly ITextCleaner _cleaner;
    private CancellationTokenSource? _statusTimer;

    [ObservableProperty]
    private HistoryEntry? _selected;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _alwaysOnTop;

    /// <summary>
    /// Filters the list as it is typed. Searching is the only way a log of a few hundred entries is
    /// usable at all, and the one search anyone runs is "what did I say about…".
    /// </summary>
    [ObservableProperty]
    private string _search = string.Empty;

    partial void OnSearchChanged(string value) => Refresh();

    public HistoryViewModel(IDictationHistory history, SettingsStore settings, ITextCleaner cleaner)
    {
        _history = history;
        _settings = settings;
        _cleaner = cleaner;
        _alwaysOnTop = settings.Current.History.AlwaysOnTop;

        Refresh();
        _history.Changed += OnHistoryChanged;
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public bool IsEmpty => Entries.Count == 0;

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search);

    /// <summary>
    /// "Nothing dictated yet" is wrong and slightly alarming when the log is full and the search
    /// simply found nothing.
    /// </summary>
    public string EmptyText => IsFiltered
        ? "No dictation matches that."
        : "Nothing dictated yet. Hold your push-to-talk key and speak.";

    public void Dispose() => _history.Changed -= OnHistoryChanged;

    /// <summary>Persists the window's placement so it reopens where the user left it.</summary>
    public void SavePlacement(double left, double top, double width, double height)
    {
        var next = _settings.Current.Clone();
        next.History.WindowLeft = left;
        next.History.WindowTop = top;
        next.History.WindowWidth = width;
        next.History.WindowHeight = height;
        next.History.AlwaysOnTop = AlwaysOnTop;
        _settings.Save(next);
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        var next = _settings.Current.Clone();
        next.History.AlwaysOnTop = value;
        _settings.Save(next);
    }

    [RelayCommand]
    private void CopyLast()
    {
        if (Entries.FirstOrDefault() is { } entry)
        {
            Copy(entry);
        }
        else
        {
            Flash("Nothing dictated yet.");
        }
    }

    [RelayCommand]
    private void Copy(HistoryEntry? entry)
    {
        entry ??= Selected ?? Entries.FirstOrDefault();
        if (entry is null)
        {
            return;
        }

        PutOnClipboard(entry.Draft);
    }

    /// <summary>Copies what the recogniser heard, before any cleanup — the half a correction starts from.</summary>
    [RelayCommand]
    private void CopyHeard(HistoryEntry? entry)
    {
        entry ??= Selected;
        if (entry is null || entry.RawText.Length == 0)
        {
            return;
        }

        PutOnClipboard(entry.RawText);
    }

    [RelayCommand]
    private void SaveEdit(HistoryEntry? entry)
    {
        entry ??= Selected;
        if (entry is null || !entry.IsEdited)
        {
            return;
        }

        // Only what was typed. What was heard is the evidence a correction is learned from, and an
        // edit that overwrote it would destroy the pair the vocabulary needs.
        var edited = entry.Record with { FinalText = entry.Draft };

        if (_history.Replace(edited))
        {
            entry.Accept(edited);
            Flash("Saved.");
        }
        else
        {
            Flash("Could not save that edit — the history file could not be written.");
        }
    }

    [RelayCommand]
    private void RevertEdit(HistoryEntry? entry) => (entry ?? Selected)?.Revert();

    [RelayCommand]
    private void Delete(HistoryEntry? entry)
    {
        entry ??= Selected;
        if (entry is null)
        {
            return;
        }

        // No confirmation for one entry: it is a small, single, obviously-scoped action, and the
        // dialog would be in the way of the tidying-up this exists for. Clear, which takes everything
        // at once, still asks.
        if (_history.Remove(entry.Record.Id))
        {
            Entries.Remove(entry);
            OnPropertyChanged(nameof(IsEmpty));
            Flash("Deleted.");
        }
        else
        {
            Flash("Could not delete that entry — the history file could not be written.");
        }
    }

    /// <summary>
    /// Runs cleanup over the raw transcript again, under whatever the settings say now. Reprocesses
    /// the text, never the audio: keeping recordings around to re-transcribe would mean a permanent
    /// archive of everything ever said, which is not a thing to introduce by default.
    /// </summary>
    [RelayCommand]
    private async Task CleanAgainAsync(HistoryEntry? entry)
    {
        entry ??= Selected;
        if (entry is null || entry.RawText.Length == 0 || entry.IsBusy)
        {
            return;
        }

        entry.IsBusy = true;
        Flash("Cleaning…");

        try
        {
            var cleaned = (await _cleaner.CleanAsync(entry.Record.RawText).ConfigureAwait(true)).Trim();

            if (cleaned.Length == 0 || string.Equals(cleaned, entry.Draft, StringComparison.Ordinal))
            {
                Flash("No change.");
                return;
            }

            // Into the draft, not onto the disk. The point of running it again is to see what it would
            // say; accepting it is a separate decision, and Revert is right there.
            entry.Draft = cleaned;
            Flash("Cleaned — Save to keep it.");
        }
        catch (Exception ex)
        {
            Flash("Could not clean that: " + ex.Message);
        }
        finally
        {
            entry.IsBusy = false;
        }
    }

    /// <summary>
    /// Teaches the vocabulary a correction, from the window rather than from Settings. Returns the
    /// reason it was refused, or null when it was taken.
    ///
    /// This is the other half of the vocabulary feature: the moment anyone knows a replacement is
    /// wanted is the moment they are looking at the words that came out wrong, not later, in a
    /// settings page, from memory.
    /// </summary>
    public string? LearnReplacement(string? heard, string? typed)
    {
        var next = _settings.Current.Clone();
        var result = VocabularyEdit.Learn(next.Vocabulary.Replacements, heard, typed);

        if (!result.Ok)
        {
            return result.Problem;
        }

        next.Vocabulary.Replacements = result.Replacements;
        _settings.Save(next);
        Flash(result.Replaced ? "Replacement updated." : "Replacement saved.");
        return null;
    }

    private void PutOnClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            Flash("Copied to the clipboard.");
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; nothing here is worth an error dialog.
            Flash("Could not copy: " + ex.Message);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Delete all {Entries.Count} entries from the dictation history?\n\n{_history.Path}",
            "TawkType",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (!_history.Clear())
        {
            // The list would otherwise empty itself and the log would be back on the next launch.
            MessageBox.Show(
                "TawkType could not delete the history file. It is still on disk and will come back the "
                + "next time Talk2Me starts." + Environment.NewLine + Environment.NewLine + _history.Path,
                "TawkType",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var selectedId = Selected?.Record.Id;

        // Half-finished edits are carried across the rebuild. The list refreshes whenever a dictation
        // lands, which must not throw away what the user is in the middle of typing into a past one.
        var drafts = Entries
            .Where(entry => entry.IsEdited)
            .ToDictionary(entry => entry.Record.Id, entry => entry.Draft);

        Entries.Clear();
        foreach (var record in HistoryQuery.Filter(_history.Recent, Search))
        {
            var entry = new HistoryEntry(record);
            if (drafts.TryGetValue(record.Id, out var draft))
            {
                entry.Draft = draft;
            }

            Entries.Add(entry);
        }

        Selected = Entries.FirstOrDefault(e => e.Record.Id == selectedId);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(EmptyText));
    }

    private async void Flash(string message)
    {
        _statusTimer?.Cancel();
        var cts = _statusTimer = new CancellationTokenSource();
        Status = message;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), cts.Token);
            Status = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
