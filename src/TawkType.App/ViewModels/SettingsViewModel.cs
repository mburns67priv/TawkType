using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TawkType.Core.Abstractions;
using TawkType.Core.History;
using TawkType.Core.Models;
using TawkType.Core.Settings;
using TawkType.Desktop.Services;
using TawkType.Transcription;
using TawkType.Windows.Audio;
using TawkType.Windows.Input;
using TawkType.Windows.Startup;

namespace TawkType.Desktop.ViewModels;

public enum SettingsPage
{
    General,
    Transcription,
    Activation,
    Modes,
    Appearance,
    Vocabulary,
    Cleanup,
    History,
}

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly ModelMaintenance _models;
    private readonly IApiKeyStore _apiKeys;
    private readonly IDictationHistory _history;
    private readonly UpdateService _updates;

    /// <summary>Commas or line breaks, so a list can be pasted in either shape.</summary>
    private static readonly char[] Separators = [',', (char)10, (char)13];

    /// <summary>Set by the password box as the user types. Null means "leave the stored key alone".</summary>
    private string? _pendingApiKey;

    private CancellationTokenSource? _statusTimer;
    private CancellationTokenSource? _download;

    [ObservableProperty]
    private TawkTypeSettings _draft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNavPage))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedBy))]
    private SettingsPage _selectedPage = SettingsPage.General;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinimumHoldError))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedBy))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _minimumHoldText;

    /// <summary>Minutes, because nobody thinks about a runaway recording in seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxRecordingError))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedBy))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _maxRecordingMinutesText;

    /// <summary>The mode being edited on the Modes page. Not the same as the one that is in charge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModeIsBuiltIn))]
    private DictationMode? _selectedMode;

    partial void OnSelectedModeChanged(DictationMode? value) => OnPropertyChanged(nameof(ModeSpellingsText));

    [ObservableProperty]
    private string _selectedInputDevice;

    [ObservableProperty]
    private string _modelStorageText;

    /// <summary>What the active engine needs, and whether it already has it.</summary>
    [ObservableProperty]
    private string _activeModelText = string.Empty;

    /// <summary>Which model the Download button would fetch. Defaults to the one the settings need.</summary>
    [ObservableProperty]
    private DownloadableModel? _selectedDownload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadIndeterminate))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadIndeterminate))]
    private double? _downloadProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CleanupTimeoutError))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedBy))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _cleanupTimeoutText;

    /// <summary>One "heard => typed" per line.</summary>
    [ObservableProperty]
    private string _replacementsText;

    /// <summary>One "trigger => text" per line; a literal backslash-n makes a line break in the text.</summary>
    [ObservableProperty]
    private string _snippetsText;

    [ObservableProperty]
    private string _vocabularyText;

    [ObservableProperty]
    private string _apiKeyStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryMaxEntriesError))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedBy))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
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

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    /// <summary>
    /// Seeded from the registry in the constructor, not left at its default. A checkbox that always
    /// opens unticked tells the user their app does not start with Windows even when it does — and
    /// ticking it to "fix" that writes a value that was already there.
    /// </summary>
    [ObservableProperty]
    private bool _startWithWindows;

    public SettingsViewModel(
        SettingsStore store,
        ModelMaintenance models,
        IApiKeyStore apiKeys,
        IDictationHistory history,
        UpdateService updates)
    {
        _store = store;
        _models = models;
        _apiKeys = apiKeys;
        _history = history;
        _updates = updates;

        _draft = store.Current.Clone();
        _minimumHoldText = _draft.MinimumHoldMs.ToString();
        _maxRecordingMinutesText = NumberField.RecordingLimit.Format(_draft.MaxRecordingSeconds / 60d);

        foreach (var mode in _draft.Modes)
        {
            Modes.Add(mode);
        }

        _selectedMode = Modes.FirstOrDefault(m => m.Name == _draft.ActiveMode) ?? Modes.FirstOrDefault();
        InputDevices = new[] { "(system default)" }.Concat(WaveInAudioCapture.ListInputDevices()).ToArray();
        _selectedInputDevice = _draft.InputDeviceName ?? InputDevices[0];
        _modelStorageText = models.Describe();
        _cleanupTimeoutText = _draft.Cleanup.TimeoutMs.ToString();
        _vocabularyText = string.Join(Environment.NewLine, _draft.Vocabulary.Spellings);
        _replacementsText = VocabularyFormat.Format(_draft.Vocabulary.Replacements);
        _snippetsText = VocabularyFormat.Format(_draft.Vocabulary.Snippets);
        _historyMaxEntriesText = _draft.History.MaxEntries.ToString();
        _apiKeyStatus = DescribeApiKey();

        // The field, not the property: assigning the property here would fire OnStartWithWindowsChanged
        // and write the registry back on every open.
        _startWithWindows = WindowsStartup.IsEnabled();

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

    public IReadOnlyList<TranscriptionEngine> Engines { get; } = Enum.GetValues<TranscriptionEngine>();

    /// <summary>
    /// The engine, proxied so the fields that only some engines read can grey themselves out. Binding
    /// the combo straight to the draft would set it without telling anyone.
    /// </summary>
    public TranscriptionEngine SelectedEngine
    {
        get => Draft.Engine;
        set
        {
            if (Draft.Engine == value)
            {
                return;
            }

            Draft.Engine = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageApplies));
            OnPropertyChanged(nameof(LanguageHint));
            OnPropertyChanged(nameof(WhisperModelApplies));
            OnPropertyChanged(nameof(WhisperModelHint));
            RefreshModels(); // the catalogue marks what is in use, and that just changed
        }
    }

    /// <summary>
    /// What the language picker shows: detect, then the five most spoken, a separator, then the rest
    /// A to Z. Mixed types because a WPF ComboBox takes a Separator as an item and draws it as one.
    /// </summary>
    public IReadOnlyList<object> LanguageOptions { get; } = LanguagePicker.Options();

    /// <summary>
    /// The chosen language, as an item of <see cref="LanguageOptions"/>. Writing it back as a code
    /// keeps settings.json readable and keeps hand-edited files working.
    /// </summary>
    public Language? SelectedLanguage
    {
        get => Languages.Find(Draft.Language);
        set
        {
            if (value is null || value.Code == Draft.Language)
            {
                return;
            }

            Draft.Language = value.Code;
            OnPropertyChanged();
        }
    }

    /// <summary>Parakeet works the language out itself, so the setting does nothing under it.</summary>
    public bool LanguageApplies => Draft.Engine != TranscriptionEngine.Parakeet;

    public string LanguageHint => LanguageApplies
        ? "An ISO 639-1 code such as en, fr, de — or auto. Under Auto it also decides which engine runs."
        : "Parakeet detects the language itself, so this is only used by Whisper and by Auto.";

    /// <summary>Which Whisper size to load. Nothing to choose while Whisper cannot run.</summary>
    public bool WhisperModelApplies => Draft.Engine != TranscriptionEngine.Parakeet;

    public string WhisperModelHint => WhisperModelApplies
        ? "LargeV3Turbo is a 1.6 GB download."
        : "Only used when Whisper is the active engine.";

    public IReadOnlyList<string> WhisperModels { get; } = ModelManager.ModelNames;

    public IReadOnlyList<TextInjectionMode> InjectionModes { get; } = Enum.GetValues<TextInjectionMode>();

    public IReadOnlyList<string> InputDevices { get; }

    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    public IReadOnlyList<CleanupStyle> CleanupStyles { get; } = Enum.GetValues<CleanupStyle>();

    /// <summary>Suggestions only; the combo is editable so any model id can be typed.</summary>
    public IReadOnlyList<string> CleanupModels { get; } = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"];

    public ObservableCollection<ModelListItem> InstalledModels { get; } = new();

    /// <summary>Everything that can be downloaded, for the picker beside the Download button.</summary>
    public ObservableCollection<DownloadableModel> DownloadableModels { get; } = new();

    public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new();

    public bool HistoryIsEmpty => HistoryEntries.Count == 0;

    // Formatted for the stat tiles. DictationStats stays free of presentation concerns.
    public string StatCount => Stats.Count.ToString("N0");

    public string StatSpeech => DictationStats.FormatDuration(Stats.SpeechDuration);

    public string StatWordsPerMinute => Stats.WordsPerMinute.ToString("N0");

    public string StatWords => Stats.Words.ToString("N0");

    public string StatCharacters => Stats.Characters.ToString("N0");

    public string StatTimeSaved => DictationStats.FormatDuration(Stats.TimeSaved);

    // Each box checks itself as you type: the message appears under the box and Save goes quiet until
    // it is gone. Nothing is clamped or corrected behind your back — what you typed stays there to fix.
    public string? MinimumHoldError => NumberField.MinimumHold.Parse(MinimumHoldText).Error;

    public string? MaxRecordingError => NumberField.RecordingLimit.Parse(MaxRecordingMinutesText).Error;

    public string? CleanupTimeoutError => NumberField.CleanupTimeout.Parse(CleanupTimeoutText).Error;

    public string? HistoryMaxEntriesError => NumberField.HistoryEntries.Parse(HistoryMaxEntriesText).Error;

    public bool CanSave => BadValuePage is null;

    /// <summary>The first page holding a value Save will not accept, or null when there is none.</summary>
    private SettingsPage? BadValuePage
        => MinimumHoldError is not null || MaxRecordingError is not null ? SettingsPage.Activation
            : CleanupTimeoutError is not null ? SettingsPage.Cleanup
            : HistoryMaxEntriesError is not null ? SettingsPage.History
            : null;

    /// <summary>
    /// Shown beside a greyed-out Save. Without it, a bad value left on one page disables Save while you
    /// are looking at another, with nothing on screen to say why.
    /// </summary>
    public string? SaveBlockedBy
        => BadValuePage is { } page && page != SelectedPage
            ? $"Check the value on the {NavPage.All.First(nav => nav.Page == page).Title} page"
            : null;

    /// <summary>
    /// The selected mode's extra spellings, as one per line. A text box for the same reason the main
    /// vocabulary is one: these lists are written in bursts and pasted from somewhere.
    /// </summary>
    public string ModeSpellingsText
    {
        get => SelectedMode is null ? string.Empty : string.Join(Environment.NewLine, SelectedMode.Vocabulary.Spellings);
        set
        {
            if (SelectedMode is null)
            {
                return;
            }

            SelectedMode.Vocabulary.Spellings = (value ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            OnPropertyChanged();
        }
    }

    /// <summary>Built-in modes can be adjusted but not renamed or deleted, so their names stay meaningful.</summary>
    public bool SelectedModeIsBuiltIn => SelectedMode?.IsBuiltIn ?? false;

    public ObservableCollection<DictationMode> Modes { get; } = new();

    /// <summary>The mode that will be used for the next dictation.</summary>
    public DictationMode? ActiveMode
    {
        get => Modes.FirstOrDefault(m => m.Name == Draft.ActiveMode) ?? Modes.FirstOrDefault();
        set
        {
            if (value is not null)
            {
                Draft.ActiveMode = value.Name;
                OnPropertyChanged();
            }
        }
    }

    public string SettingsPath => _store.Path;

    public string HistoryPath => _history.Path;

    public string Version => _updates.CurrentVersion;

    public bool CanRestartForUpdate => _updates.State == UpdateState.ReadyToRestart;

    public void Dispose() => _history.Changed -= OnHistoryChanged;

    /// <summary>Called by the password box, which cannot be data-bound.</summary>
    public void SetPendingApiKey(string apiKey) => _pendingApiKey = apiKey;

    [RelayCommand]
    private void Navigate(SettingsPage page) => SelectedPage = page;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // CanSave has already checked all four, so these are only unwrapping what it validated.
        Draft.MinimumHoldMs = (int)NumberField.MinimumHold.Parse(MinimumHoldText).Value;
        Draft.MaxRecordingSeconds = (int)Math.Round(NumberField.RecordingLimit.Parse(MaxRecordingMinutesText).Value * 60);
        Draft.Cleanup.TimeoutMs = (int)NumberField.CleanupTimeout.Parse(CleanupTimeoutText).Value;
        Draft.History.MaxEntries = (int)NumberField.HistoryEntries.Parse(HistoryMaxEntriesText).Value;

        Draft.InputDeviceName = SelectedInputDevice == InputDevices[0] ? null : SelectedInputDevice;
        Draft.Language = string.IsNullOrWhiteSpace(Draft.Language) ? "en" : Draft.Language.Trim();

        Draft.Cleanup.Model = string.IsNullOrWhiteSpace(Draft.Cleanup.Model)
            ? new CleanupSettings().Model
            : Draft.Cleanup.Model.Trim();

        ApplyVocabularyEdits();

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
                + "next time TawkType starts." + Environment.NewLine + Environment.NewLine + _history.Path,
                "TawkType",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>A download whose size we do not know yet still has to look like it is doing something.</summary>
    public bool IsDownloadIndeterminate => IsDownloading && DownloadProgress is null;

    /// <summary>
    /// Fetches the model for whichever engine the current settings use. This is the only thing in
    /// TawkType that downloads one — the transcribers refuse to, so that holding the hotkey never starts
    /// a surprise several-hundred-megabyte transfer.
    /// </summary>
    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        var target = SelectedDownload;
        if (IsDownloading || target is null || target.IsDownloaded)
        {
            return;
        }

        _download = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = null;

        try
        {
            var progress = new Progress<ModelProgress>(report => DownloadProgress = report.Fraction);
            await _models.DownloadAsync(target.Key, progress, _download.Token);
            Flash(target.Label + " downloaded.");
        }
        catch (OperationCanceledException)
        {
            Flash("Download cancelled.");
        }
        catch (Exception ex)
        {
            Flash("Download failed: " + ex.Message);
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = null;
            _download?.Dispose();
            _download = null;
            RefreshModels();
            ModelDownloaded?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void CancelDownload() => _download?.Cancel();

    /// <summary>Raised after a download attempt so the app can re-check and warm up.</summary>
    public event EventHandler? ModelDownloaded;

    /// <summary>
    /// The whole vocabulary as one JSON file. Worth having: these lists are the part of TawkType that is
    /// genuinely the user's own work, and they should be able to keep a copy, move it to another
    /// machine, or share it with someone without retyping it.
    /// </summary>
    [RelayCommand]
    private void ExportVocabulary()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "tawktype-vocabulary.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ApplyVocabularyEdits();
            File.WriteAllText(dialog.FileName, VocabularyFile.Write(Draft.Vocabulary));
            Flash("Vocabulary exported.");
        }
        catch (Exception ex)
        {
            Flash("Could not export: " + ex.Message);
        }
    }

    [RelayCommand]
    private void ImportVocabulary()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var imported = VocabularyFile.Read(File.ReadAllText(dialog.FileName));
            if (imported is null)
            {
                Flash("That file is not a TawkType vocabulary.");
                return;
            }

            // Into the boxes rather than straight into settings: the user still has to press Save, and
            // can see what arrived before they do.
            Draft.Vocabulary = imported;
            VocabularyText = string.Join(Environment.NewLine, imported.Spellings);
            ReplacementsText = VocabularyFormat.Format(imported.Replacements);
            SnippetsText = VocabularyFormat.Format(imported.Snippets);
            Flash("Vocabulary imported. Save to keep it.");
        }
        catch (Exception ex)
        {
            Flash("Could not import: " + ex.Message);
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

    [RelayCommand]
    private async Task CheckForUpdatesAsync() => await _updates.CheckAsync();

    [RelayCommand]
    private void RestartForUpdate() => _updates.RestartAndUpdate();

    partial void OnStartWithWindowsChanged(bool value)
    {
        // Point the Run key at the launcher stub Velopack maintains, not this build's exe, so the entry
        // survives an update replacing the versioned folder underneath it.
        var target = Environment.ProcessPath;
        if (target is null || !WindowsStartup.Set(value, target))
        {
            Flash("Could not change the Windows startup setting.");
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

    /// <summary>Rebuilds the download picker, keeping the user's choice if it is still on the list.</summary>
    private void RefreshCatalogue()
    {
        var wanted = SelectedDownload?.Key ?? _models.ActiveModelKey;

        DownloadableModels.Clear();
        foreach (var model in _models.Catalogue())
        {
            DownloadableModels.Add(model);
        }

        SelectedDownload = DownloadableModels.FirstOrDefault(m => m.Key == wanted)
            ?? DownloadableModels.FirstOrDefault();
    }

    private void RefreshModels()
    {
        InstalledModels.Clear();
        foreach (var model in _models.List())
        {
            InstalledModels.Add(new ModelListItem(model));
        }

        RefreshCatalogue();

        ModelStorageText = _models.Describe();
        ActiveModelText = _models.IsActiveModelDownloaded
            ? $"Your settings use {_models.ActiveModelLabel}, and it is downloaded."
            : $"Your settings use {_models.ActiveModelLabel}, which is not downloaded. "
              + "Dictation does nothing until one is.";
    }

    /// <summary>Turns the three text boxes into the draft's lists. Shared by Save and by Export.</summary>
    private void ApplyVocabularyEdits()
    {
        Draft.Vocabulary.Spellings = VocabularyText
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        Draft.Vocabulary.Replacements = VocabularyFormat.ParseReplacements(ReplacementsText);
        Draft.Vocabulary.Snippets = VocabularyFormat.ParseSnippets(SnippetsText);

        // The prompt keeps its own copy, so an older build still sees the words.
        Draft.Cleanup.Vocabulary = Draft.Vocabulary.Spellings;
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
