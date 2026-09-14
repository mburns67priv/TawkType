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
    private readonly AppDataMaintenance _appData;
    private readonly IApiKeyStore _apiKeys;
    private readonly ILlmClient _llm;
    private readonly ThemeManager _theme;
    private readonly SoundCues _sounds;

    /// <summary>Whether Save ever ran. A window closed without it has to put the theme back.</summary>

    /// <summary>
    /// The faintness to go back to when dimming is switched on again, so somebody who set their own
    /// value and then tried solid does not lose it to the default.
    /// </summary>
    private double _dimmedOpacity;
    private readonly IDictationHistory _history;
    private readonly UpdateService _updates;


    /// <summary>Set by the password box as the user types. Null means "leave the stored key alone".</summary>
    private string? _pendingApiKey;

    /// <summary>
    /// Whether anything on the Vocabulary page has changed the draft since this window opened.
    ///
    /// Drives two things: the note that says the change is not on disk yet, and what happens when the
    /// file changes underneath — an untouched vocabulary can simply take the newer one, an edited one
    /// cannot, because adopting it would throw the user's edits away.
    /// </summary>
    private bool _vocabularyTouched;

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

    /// <summary>What is in the data folder, so the delete button is not an unlabelled cliff edge.</summary>
    [ObservableProperty]
    private string _appDataText;

    /// <summary>What the log is using, so "Clear the log now" says what it would get back.</summary>
    [ObservableProperty]
    private string _logText;

    /// <summary>
    /// Every sound the user's Windows scheme has, plus silence. Read when the window opens rather
    /// than at launch: somebody setting these up may have just changed their scheme in the Sound
    /// control panel, and a list captured once would not show it.
    /// </summary>
    public IReadOnlyList<SoundOption> SoundOptions { get; }

    public SoundOption SelectedStartSound
    {
        get => OptionFor(Draft.Sounds.Start);
        set => SetCue(value, cue => Draft.Sounds.Start = cue, nameof(SelectedStartSound));
    }

    public SoundOption SelectedStopSound
    {
        get => OptionFor(Draft.Sounds.Stop);
        set => SetCue(value, cue => Draft.Sounds.Stop = cue, nameof(SelectedStopSound));
    }

    public SoundOption SelectedErrorSound
    {
        get => OptionFor(Draft.Sounds.Error);
        set => SetCue(value, cue => Draft.Sounds.Error = cue, nameof(SelectedErrorSound));
    }

    /// <summary>
    /// The slider. Proxied rather than bound straight at the draft so the word beside it keeps up,
    /// and so dragging it plays nothing — a preview on every tick of a drag is a machine gun.
    /// </summary>
    public int SoundVolume
    {
        get => Draft.Sounds.Volume;
        set
        {
            var clamped = SoundCue.ClampVolume(value);
            if (clamped == Draft.Sounds.Volume)
            {
                return;
            }

            Draft.Sounds.Volume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SoundVolumeText));
        }
    }

    public string SoundVolumeText => SoundCue.DescribeVolume(SoundVolume);

    /// <summary>
    /// Plays a cue at the volume currently on the slider, whatever the master switch says.
    ///
    /// Auditioning is the whole point of the picker: you cannot choose between "Notification" and
    /// "Device Connect" from their names. It ignores <c>PlaySounds</c> deliberately — somebody who
    /// has not ticked that box yet is exactly who is trying to decide whether to.
    /// </summary>
    [RelayCommand]
    private void PreviewSound(string? which)
    {
        var cue = which switch
        {
            "start" => Draft.Sounds.Start,
            "stop" => Draft.Sounds.Stop,
            "error" => Draft.Sounds.Error,
            _ => null,
        };

        if (SoundCue.IsSilent(cue))
        {
            Flash("That one is silent.");
            return;
        }

        _sounds.Preview(cue, SoundVolume);
    }

    /// <summary>The option matching a stored cue, inventing a placeholder when the scheme has lost it.</summary>
    private SoundOption OptionFor(string? cue)
    {
        var name = SoundCue.Normalise(cue);

        if (SoundCue.IsSilent(name))
        {
            return SoundOption.Silence;
        }

        return SoundOptions.FirstOrDefault(option =>
                   string.Equals(option.EventName, name, StringComparison.OrdinalIgnoreCase))
               ?? SoundOption.Missing(name);
    }

    private void SetCue(SoundOption? option, Action<string> write, string property)
    {
        write(SoundCue.Normalise(option?.EventName));
        OnPropertyChanged(property);
    }

    /// <summary>
    /// The three vocabulary lists. Each owns its rows, its text box and the rules for getting in, so
    /// the page is three copies of one thing rather than three arrangements of the same idea.
    /// </summary>
    public SpellingSection Spellings { get; }

    public ReplacementSection Replacements { get; }

    public SnippetSection Snippets { get; }

    private IEnumerable<VocabularySection> Vocabularies => VocabularyLists;

    /// <summary>The three, in the order the tabs show them.</summary>
    public VocabularySection[] VocabularyLists { get; }

    /// <summary>
    /// The one list on screen. Three stacked lists were fine at three entries each and unreadable at
    /// eighty: the sections ended up miles apart, the footer below the horizon, and finding an entry
    /// meant scrolling past the other two.
    /// </summary>
    [ObservableProperty]
    private VocabularySection _activeList = null!;

    [RelayCommand]
    private void SelectVocabularyList(VocabularySection? section)
    {
        if (section is not null)
        {
            ActiveList = section;
        }
    }

    partial void OnActiveListChanged(VocabularySection? oldValue, VocabularySection newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        newValue.IsActive = true;
    }

    /// <summary>
    /// What the AI cleanup page says about the vocabulary, which it sends with every rewrite. It used
    /// to carry a second editable box for the same spellings, comma separated — a way into the list
    /// that none of the rules applied to, and a second place for it to disagree with itself.
    /// </summary>
    public string VocabularySummary
    {
        get
        {
            var words = Draft.Vocabulary.Spellings.Length;
            var rules = Draft.Vocabulary.Replacements.Length;

            return words == 0 && rules == 0
                ? "Nothing yet. Anything you add on the Vocabulary page is sent with the rewrite, so Claude knows the words you have taught TawkType."
                : $"{words} spelling{(words == 1 ? string.Empty : "s")} and {rules} replacement{(rules == 1 ? string.Empty : "s")}, from the Vocabulary page, are sent with every rewrite.";
        }
    }

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
    /// <summary>One "trigger => text" per line; a literal backslash-n makes a line break in the text.</summary>
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
    [NotifyPropertyChangedFor(nameof(UnsavedNote))]
    private string _status = string.Empty;

    /// <summary>
    /// Set when the vocabulary on disk changed while this window held an edited copy of it — which
    /// happens when Remember… in the history window teaches a replacement. Unlike the flash it stays
    /// on screen, because it is a warning about what the Save button is about to do.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnsavedNote))]
    private string? _vocabularyConflict;

    /// <summary>
    /// The whole of the warning, for the tooltip. The footer line is short because it shares a bar
    /// with two buttons and a 720px window leaves it a few dozen characters — short enough to read at
    /// a glance, with the rest a hover away.
    /// </summary>
    public string VocabularyConflictDetail =>
        "While this window has been open, a replacement was saved from the history window's Remember… "
        + "button. This window is still holding the vocabulary as it was when it opened, so Save will "
        + "write that older list back and the new replacement will be lost. Cancel and reopen Settings "
        + "to keep it.";

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    /// <summary>
    /// The quiet reminder that a vocabulary change is still only in the draft.
    ///
    /// Nothing on this page reaches disk until Save, and the Add and Edit dialogs used to say nothing
    /// about that at all: the row changed the moment the dialog closed, so the change looked done. The
    /// flash says it entry by entry; this stays put afterwards, until Save or Cancel settles it.
    ///
    /// Null while a flash is up — that flash already ends in "Save to keep it" — and null while a
    /// conflict is showing, which is the more serious version of the same sentence.
    /// </summary>
    public string? UnsavedNote
        => VocabularyConflict is not null || Status.Length > 0 || !_vocabularyTouched
            ? null
            : "Vocabulary changed. Save to keep it.";

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
        AppDataMaintenance appData,
        IApiKeyStore apiKeys,
        ILlmClient llm,
        IDictationHistory history,
        ThemeManager theme,
        UpdateService updates,
        SoundCues sounds)
    {
        _store = store;
        _models = models;
        _appData = appData;
        _apiKeys = apiKeys;
        _llm = llm;
        _theme = theme;
        _history = history;
        _updates = updates;
        _sounds = sounds;

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
        _appDataText = DescribeAppData(appData);
        _logText = DescribeLogs();
        _cleanupTimeoutText = _draft.Cleanup.TimeoutMs.ToString();

        // Silence first, then whatever the scheme has, by label. A scheme that cannot be read leaves
        // just the one entry, which is still a usable picker: it says "no sound" and means it.
        SoundOptions = new[] { SoundOption.Silence }
            .Concat(WindowsSoundScheme.List().Select(SoundOption.From))
            .ToArray();
        Spellings = new SpellingSection(() => Draft.Vocabulary, OnVocabularyChanged);
        Replacements = new ReplacementSection(() => Draft.Vocabulary, OnVocabularyChanged);
        Snippets = new SnippetSection(() => Draft.Vocabulary, OnVocabularyChanged);

        VocabularyLists = [Spellings, Replacements, Snippets];

        // Rows and counts come from Refresh, so without this the page opens empty for somebody who
        // already has a vocabulary — the lists would fill in only once something else changed them.
        foreach (var section in VocabularyLists)
        {
            section.Refresh();
        }

        ActiveList = Spellings;
        _historyMaxEntriesText = _draft.History.MaxEntries.ToString();
        _apiKeyStatus = DescribeApiKey();
        _updateStatus = updates.Describe();

        // The field, not the property: assigning the property here would fire OnStartWithWindowsChanged
        // and write the registry back on every open.
        _startWithWindows = WindowsStartup.IsEnabled();
        _dimmedOpacity = _draft.Overlay.RestingOpacity;

        RefreshModels();
        RefreshHistory();

        _history.Changed += OnHistoryChanged;
        _updates.Changed += OnUpdatesChanged;
        _store.Changed += OnStoreChanged;
    }

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

    public IReadOnlyList<OverlayPositionChoice> OverlayPositions { get; } = OverlayPositionChoice.All;

    /// <summary>The pill's corner, as a named choice rather than as the enum's identifier.</summary>
    public OverlayPositionChoice SelectedOverlayPosition
    {
        get => OverlayPositionChoice.For(Draft.Overlay.Position);
        set
        {
            if (value is null || value.Position == Draft.Overlay.Position)
            {
                return;
            }

            Draft.Overlay.Position = value.Position;
            OnPropertyChanged();
        }
    }

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

    /// <summary>
    /// Feature research §9. Built from the draft rather than from what is saved, so the two switches
    /// below change what it says as they are clicked — a panel that only told the truth after Save
    /// would be describing a machine the user is no longer looking at.
    /// </summary>
    public PrivacyState Privacy => PrivacyState.From(Draft, _llm.IsConfigured);

    /// <summary>
    /// The independent controls §9 asks for, and the reason they are properties rather than direct
    /// bindings to the draft: the same two switches appear on the History and AI cleanup pages, and
    /// all of them have to move together.
    /// </summary>
    public bool KeepHistory
    {
        get => Draft.History.Enabled;
        set
        {
            if (value == Draft.History.Enabled)
            {
                return;
            }

            Draft.History.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Privacy));
        }
    }

    public bool UseClaude
    {
        get => Draft.Cleanup.UseLlm;
        set
        {
            if (value == Draft.Cleanup.UseLlm)
            {
                return;
            }

            Draft.Cleanup.UseLlm = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Privacy));
        }
    }

    /// <summary>
    /// The theme, applied the moment it is picked rather than on Save. Nobody can choose between
    /// light and dark from two words in a list; the whole question is what it looks like.
    ///
    /// Closing without saving puts it back — see <see cref="Dispose"/>.
    /// </summary>
    public AppTheme SelectedTheme
    {
        get => Draft.Appearance.Theme;
        set
        {
            if (value == Draft.Appearance.Theme)
            {
                return;
            }

            Draft.Appearance.Theme = value;
            _theme.Preview(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Proxied so the dimming switch below can grey out when the pill hides itself entirely.</summary>
    public bool KeepPillOnScreen
    {
        get => Draft.Overlay.AlwaysVisible;
        set
        {
            if (value == Draft.Overlay.AlwaysVisible)
            {
                return;
            }

            Draft.Overlay.AlwaysVisible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the pill fades while it is resting. Off keeps it at full strength, which is what
    /// somebody wants when a faint pill disappears into a busy desktop or a bright wallpaper.
    ///
    /// It is a checkbox over a number: the stored setting is still an opacity, so a value set by hand
    /// in settings.json survives being switched off and back on.
    /// </summary>
    public bool DimWhenResting
    {
        get => Draft.Overlay.RestingOpacity < 1;
        set
        {
            if (value == DimWhenResting)
            {
                return;
            }

            if (value)
            {
                Draft.Overlay.RestingOpacity = _dimmedOpacity is > 0 and < 1
                    ? _dimmedOpacity
                    : new OverlaySettings().RestingOpacity;
            }
            else
            {
                _dimmedOpacity = Draft.Overlay.RestingOpacity;
                Draft.Overlay.RestingOpacity = 1;
            }

            OnPropertyChanged();
        }
    }

    public string SettingsPath => _store.Path;

    public string HistoryPath => _history.Path;

    public string Version => _updates.CurrentVersion;

    public bool CanRestartForUpdate => _updates.State == UpdateState.ReadyToRestart;

    public void Dispose()
    {
        _history.Changed -= OnHistoryChanged;
        _updates.Changed -= OnUpdatesChanged;
        _store.Changed -= OnStoreChanged;

        // Any theme previewed but not saved goes back to whatever is on disk, which is what Apply()
        // reads. Unconditional, and it has to be: this used to be skipped once a save had happened,
        // which was safe only while saving also closed the window. Now that it does not, previewing a
        // second theme after saving and then closing would have left that preview in force.
        _theme.Apply();
    }

    /// <summary>
    /// The update service reports every state change, and until this existed nobody was listening:
    /// the status line was bound to a property nothing ever assigned, and "Restart and update" was
    /// bound to one that never raised a change — so a downloaded update had no way to be applied from
    /// here at all. Marshalled, because the check runs on a background thread.
    /// </summary>
    private void OnUpdatesChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            UpdateStatus = _updates.Describe();
            OnPropertyChanged(nameof(CanRestartForUpdate));
        });

    /// <summary>Called by the password box, which cannot be data-bound.</summary>
    public void SetPendingApiKey(string apiKey) => _pendingApiKey = apiKey;

    [RelayCommand]
    private void Navigate(SettingsPage page) => SelectedPage = page;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // A section still in text mode is flushed here, so somebody who typed into the box and pressed
        // Save gets what they typed rather than having to press Done editing text first. A line that is
        // not an entry stops the save instead of being dropped.
        if (!ApplyVocabularyEdits())
        {
            Flash("One of the vocabulary boxes has a line that is not an entry.");
            return;
        }

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

        if (_pendingApiKey is not null)
        {
            _apiKeys.Write(_pendingApiKey);
            _pendingApiKey = null;
            ApiKeyStatus = DescribeApiKey();
        }

        _store.Save(Draft);

        // The draft and the disk agree again, so both warnings go: the quiet one that said the
        // vocabulary was only in the draft, and the conflict, which has now happened.
        _vocabularyTouched = false;
        VocabularyConflict = null;

        // The window stays open. Settings are changed in handfuls — a mode, then the key that cycles
        // them, then the thing on the next page that the first two made you think of — and closing
        // after each one made that three trips. The flash is what says it worked, since there is no
        // longer a window disappearing to say it.
        Flash("Saved.");
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

            // Into the draft rather than straight to disk: the user still has to press Save, and can
            // see what arrived before they do.
            //
            // Through the same rules as everything else, because a file is the third way into these
            // lists and used to be the one nobody checked. And any section still showing its text box
            // is taken out of it first: leaving one open would mean the next Save parsed a box holding
            // the vocabulary that has just been replaced, and put it back.
            var review = VocabularyRules.Clean(imported);
            Draft.Vocabulary = review.Vocabulary;

            foreach (var section in Vocabularies)
            {
                section.LeaveTextMode();
                section.Refresh();
            }

            OnVocabularyChanged(null);
            Flash(review.Changed
                ? "Vocabulary imported. " + string.Join(" ", review.Notes) + " Save to keep it."
                : "Vocabulary imported. Save to keep it.");
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

    /// <summary>
    /// Everything TawkType keeps, gone: models, history, settings, the key. Here rather than only at
    /// uninstall time because Velopack's hooks may not show UI, so this is the only place the question
    /// can actually be asked — and because someone may want a clean slate without uninstalling.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAppDataAsync(Window? owner)
    {
        if (await _appData.DeleteWithConfirmationAsync(owner))
        {
            AppDataText = DescribeAppData(_appData);
            LogText = DescribeLogs();
            RefreshModels();
            Flash("Deleted. TawkType is back to how it started.");
        }
    }

    /// <summary>
    /// Clears the log files. No confirmation: the log holds no work of the user's, only a record that
    /// TawkType ran, and it is capped and overwritten anyway — asking would be a dialog in front of
    /// tidying up. Deleting the history asks, because that is the user's own words.
    /// </summary>
    [RelayCommand]
    private void PurgeLogs()
    {
        var (bytes, failures) = _appData.PurgeLogs();
        LogText = DescribeLogs();

        Flash(failures > 0
            ? $"Cleared {ModelStorage.FormatSize(bytes)}; {failures} file(s) were in use."
            : bytes > 0
                ? $"Log cleared — {ModelStorage.FormatSize(bytes)} freed."
                : "There was nothing in the log.");
    }

    private static string DescribeLogs()
    {
        var bytes = AppDataMaintenance.LogBytes;
        return bytes == 0
            ? "The log is empty."
            : $"{ModelStorage.FormatSize(bytes)} in {AppDataMaintenance.LogsDirectory}";
    }

    private static string DescribeAppData(AppDataMaintenance appData)
    {
        var contents = appData.Describe();
        return contents.IsEmpty
            ? "Nothing is stored yet."
            : $"{contents.SizeText} in {AppDataMaintenance.DataDirectory}";
    }

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
    /// <summary>
    /// Flushes any section still showing its text box into the draft. False when one of them holds a
    /// line that is not an entry, which is the whole reason this returns anything: Save has to refuse
    /// rather than quietly drop the line, and the section is already saying which one it is.
    /// </summary>
    private bool ApplyVocabularyEdits()
    {
        var ok = true;

        foreach (var section in Vocabularies)
        {
            ok &= section.Commit();
        }

        return ok;
    }

    /// <summary>A section changed the draft: refresh what depends on it, and say anything it asked to say.</summary>
    private void OnVocabularyChanged(string? note)
    {
        _vocabularyTouched = true;

        OnPropertyChanged(nameof(VocabularySummary));
        OnPropertyChanged(nameof(UnsavedNote));
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SaveBlockedBy));

        if (note is not null)
        {
            Flash(note);
        }
    }

    /// <summary>
    /// settings.json changed while this window is open. Almost always this window's own Save, or the
    /// history window remembering where it was dragged to — neither of which touches the vocabulary,
    /// which is why only the vocabulary is compared.
    ///
    /// The one that matters is Remember… in the history window: it writes a replacement straight to
    /// disk, while this window is holding a clone taken when it opened. Before this, that replacement
    /// was invisible here and the next Save quietly wrote the older list back over it.
    ///
    /// An untouched vocabulary simply takes the newer one. An edited one cannot — adopting it would
    /// throw away what the user has been doing on this page — so it says what happened instead, and
    /// keeps saying it, because the Save button is now a destructive one.
    /// </summary>
    private void OnStoreChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (VocabularyRules.Same(Draft.Vocabulary, _store.Current.Vocabulary))
            {
                return;
            }

            // A section in text mode is holding text the user typed and has not committed. Nothing has
            // marked the draft yet, but replacing the lists under an open box would still lose it.
            var busy = _vocabularyTouched || Vocabularies.Any(section => section.IsTextMode);

            if (busy)
            {
                VocabularyConflict = "A correction was saved elsewhere — Save will replace it.";
                return;
            }

            Draft.Vocabulary = _store.Current.Vocabulary.Clone();

            foreach (var section in Vocabularies)
            {
                section.Refresh();
            }

            OnPropertyChanged(nameof(VocabularySummary));
            Flash("A correction was saved from the history window. It is in the list below.");
        });


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
