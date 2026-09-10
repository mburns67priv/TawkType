using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.History;
using Talk2Me.Core.Settings;
using Talk2Me.Desktop.Services;
using Talk2Me.Transcription;
using Talk2Me.Windows.Audio;
using Talk2Me.Windows.Input;

namespace Talk2Me.Desktop.ViewModels;

public enum SettingsPage
{
    General,
    Transcription,
    Activation,
    Appearance,
    Cleanup,
    History,
}

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly ModelMaintenance _models;
    private readonly IApiKeyStore _apiKeys;
    private readonly IDictationHistory _history;

    /// <summary>Set by the password box as the user types. Null means "leave the stored key alone".</summary>
    private string? _pendingApiKey;

    private CancellationTokenSource? _statusTimer;

    [ObservableProperty]
    private Talk2MeSettings _draft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNavPage))]
    private SettingsPage _selectedPage = SettingsPage.General;

    [ObservableProperty]
    private string _minimumHoldText;

    [ObservableProperty]
    private string _selectedInputDevice;

    [ObservableProperty]
    private string _modelStorageText;

    [ObservableProperty]
    private string _cleanupTimeoutText;

    [ObservableProperty]
    private string _vocabularyText;

    [ObservableProperty]
    private string _apiKeyStatus;

    [ObservableProperty]
    private string _historyMaxEntriesText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatCount))]
    [NotifyPropertyChangedFor(nameof(StatSpeech))]
    [NotifyPropertyChangedFor(nameof(StatWordsPerMinute))]
    [NotifyPropertyChangedFor(nameof(StatWords))]
    [NotifyPropertyChangedFor(nameof(StatCharacters))]
    [NotifyPropertyChangedFor(nameof(StatTimeSaved))]
    private DictationStats _stats = DictationStats.Empty;

    [ObservableProperty]
    private HistoryEntry? _selectedHistoryEntry;

    [ObservableProperty]
    private string _status = string.Empty;

    public SettingsViewModel(
        SettingsStore store,
        ModelMaintenance models,
        IApiKeyStore apiKeys,
        IDictationHistory history)
    {
        _store = store;
        _models = models;
        _apiKeys = apiKeys;
        _history = history;

        _draft = store.Current.Clone();
        _minimumHoldText = _draft.MinimumHoldMs.ToString();
        InputDevices = new[] { "(system default)" }.Concat(WaveInAudioCapture.ListInputDevices()).ToArray();
        _selectedInputDevice = _draft.InputDeviceName ?? InputDevices[0];
        _modelStorageText = models.Describe();
        _cleanupTimeoutText = _draft.Cleanup.TimeoutMs.ToString();
        _vocabularyText = string.Join(", ", _draft.Cleanup.Vocabulary);
        _historyMaxEntriesText = _draft.History.MaxEntries.ToString();
        _apiKeyStatus = DescribeApiKey();

        RefreshModels();
        RefreshHistory();

        _history.Changed += OnHistoryChanged;
    }

    public event EventHandler? Saved;

    public IReadOnlyList<NavPage> Pages { get; } = NavPage.All;

    public IReadOnlyList<NavPage> HomeCards { get; } = NavPage.HomeCards;

    /// <summary>Two-way bound to the nav rail's selection; the cards go through NavigateCommand.</summary>
    public NavPage SelectedNavPage
    {
        get => Pages.First(page => page.Page == SelectedPage);
        set => SelectedPage = value?.Page ?? SettingsPage.General;
    }

    public IReadOnlyList<string> Hotkeys { get; } = VirtualKeys.Names;

    public IReadOnlyList<TranscriptionEngine> Engines { get; } = Enum.GetValues<TranscriptionEngine>();

    public IReadOnlyList<string> WhisperModels { get; } = ModelManager.ModelNames;

    public IReadOnlyList<TextInjectionMode> InjectionModes { get; } = Enum.GetValues<TextInjectionMode>();

    public IReadOnlyList<string> InputDevices { get; }

    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    public IReadOnlyList<CleanupStyle> CleanupStyles { get; } = Enum.GetValues<CleanupStyle>();

    /// <summary>Suggestions only; the combo is editable so any model id can be typed.</summary>
    public IReadOnlyList<string> CleanupModels { get; } = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"];

    public ObservableCollection<ModelListItem> InstalledModels { get; } = new();

    public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new();

    public bool HistoryIsEmpty => HistoryEntries.Count == 0;

    // Formatted for the stat tiles. DictationStats stays free of presentation concerns.
    public string StatCount => Stats.Count.ToString("N0");

    public string StatSpeech => DictationStats.FormatDuration(Stats.SpeechDuration);

    public string StatWordsPerMinute => Stats.WordsPerMinute.ToString("N0");

    public string StatWords => Stats.Words.ToString("N0");

    public string StatCharacters => Stats.Characters.ToString("N0");

    public string StatTimeSaved => DictationStats.FormatDuration(Stats.TimeSaved);

    public string SettingsPath => _store.Path;

    public string HistoryPath => _history.Path;

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public void Dispose() => _history.Changed -= OnHistoryChanged;

    /// <summary>Called by the password box, which cannot be data-bound.</summary>
    public void SetPendingApiKey(string apiKey) => _pendingApiKey = apiKey;

    [RelayCommand]
    private void Navigate(SettingsPage page) => SelectedPage = page;

    [RelayCommand]
    private void Save()
    {
        if (int.TryParse(MinimumHoldText, out var hold) && hold >= 0)
        {
            Draft.MinimumHoldMs = hold;
        }

        if (int.TryParse(CleanupTimeoutText, out var timeout) && timeout >= 250)
        {
            Draft.Cleanup.TimeoutMs = timeout;
        }

        if (int.TryParse(HistoryMaxEntriesText, out var maxEntries) && maxEntries >= 1)
        {
            Draft.History.MaxEntries = maxEntries;
        }

        Draft.InputDeviceName = SelectedInputDevice == InputDevices[0] ? null : SelectedInputDevice;
        Draft.Language = string.IsNullOrWhiteSpace(Draft.Language) ? "en" : Draft.Language.Trim();

        Draft.Cleanup.Model = string.IsNullOrWhiteSpace(Draft.Cleanup.Model)
            ? new CleanupSettings().Model
            : Draft.Cleanup.Model.Trim();

        Draft.Cleanup.Vocabulary = VocabularyText
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (_pendingApiKey is not null)
        {
            _apiKeys.Write(_pendingApiKey);
            _pendingApiKey = null;
            ApiKeyStatus = DescribeApiKey();
        }

        _store.Save(Draft);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        _pendingApiKey = null;
        _apiKeys.Write(null);
        ApiKeyStatus = DescribeApiKey();
    }

    [RelayCommand]
    private void CopyHistoryEntry(HistoryEntry? entry)
    {
        entry ??= SelectedHistoryEntry;
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
            // Another process can hold the clipboard open; not worth an error dialog.
            Flash("Could not copy: " + ex.Message);
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (HistoryEntries.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Delete all {HistoryEntries.Count} entries from the dictation history?\n\n{_history.Path}",
            "Talk2Me",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _history.Clear();
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedModelsAsync(Window? owner)
    {
        var selected = InstalledModels.Where(item => item.IsSelected).Select(item => item.Model).ToArray();

        if (await _models.DeleteAsync(selected, owner))
        {
            RefreshModels();
        }
    }

    [RelayCommand]
    private async Task DeleteAllModelsAsync(Window? owner)
    {
        if (await _models.DeleteAllWithConfirmationAsync(owner))
        {
            RefreshModels();
        }
    }

    /// <summary>Opens a credit or licence link in the default browser.</summary>
    [RelayCommand]
    private static void OpenLink(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    [RelayCommand]
    private static void OpenDataFolder()
    {
        Directory.CreateDirectory(SettingsStore.AppDataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", SettingsStore.AppDataDirectory) { UseShellExecute = true });
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(RefreshHistory);

    private void RefreshHistory()
    {
        var selectedId = SelectedHistoryEntry?.Record.Id;

        HistoryEntries.Clear();
        foreach (var record in _history.Recent)
        {
            HistoryEntries.Add(new HistoryEntry(record));
        }

        SelectedHistoryEntry = HistoryEntries.FirstOrDefault(entry => entry.Record.Id == selectedId);
        Stats = DictationStats.From(_history.Recent);
        OnPropertyChanged(nameof(HistoryIsEmpty));
    }

    private void RefreshModels()
    {
        InstalledModels.Clear();
        foreach (var model in _models.List())
        {
            InstalledModels.Add(new ModelListItem(model));
        }

        ModelStorageText = _models.Describe();
    }

    private string DescribeApiKey()
    {
        if (_apiKeys.HasKey)
        {
            return "A key is stored for your Windows account, encrypted with DPAPI.";
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
            ? "No key stored. Paste one above, or set ANTHROPIC_API_KEY."
            : "Using the ANTHROPIC_API_KEY environment variable.";
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
