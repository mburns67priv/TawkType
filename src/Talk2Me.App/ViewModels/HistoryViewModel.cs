using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Desktop.ViewModels;

/// <summary>One row in the history list.</summary>
public sealed class HistoryEntry(DictationRecord record)
{
    public DictationRecord Record { get; } = record;

    public string TimeText => Record.At.ToString("HH:mm");

    public string DayText => Record.At.LocalDateTime.Date == DateTime.Today
        ? "Today"
        : Record.At.ToString("d MMM");

    public string FinalText => Record.FinalText.Trim();

    public string RawText => Record.RawText.Trim();

    public bool ShowRaw => Record.WasCleaned && RawText.Length > 0;

    public string MetaText =>
        $"{Record.Engine} · {Record.AudioSeconds:F1}s audio · {Record.TranscriptionMs} ms";
}

/// <summary>
/// Drives the history window. It is meant to sit on screen all day, so it refreshes itself from
/// <see cref="IDictationHistory"/> as dictations land rather than being rebuilt on each open.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IDictationHistory _history;
    private readonly SettingsStore _settings;
    private CancellationTokenSource? _statusTimer;

    [ObservableProperty]
    private HistoryEntry? _selected;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _alwaysOnTop;

    public HistoryViewModel(IDictationHistory history, SettingsStore settings)
    {
        _history = history;
        _settings = settings;
        _alwaysOnTop = settings.Current.History.AlwaysOnTop;

        Refresh();
        _history.Changed += OnHistoryChanged;
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public bool IsEmpty => Entries.Count == 0;

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

        try
        {
            Clipboard.SetText(entry.FinalText);
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
            "Talk2Me",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _history.Clear();
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var selectedId = Selected?.Record.Id;

        Entries.Clear();
        foreach (var record in _history.Recent)
        {
            Entries.Add(new HistoryEntry(record));
        }

        Selected = Entries.FirstOrDefault(e => e.Record.Id == selectedId);
        OnPropertyChanged(nameof(IsEmpty));
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
