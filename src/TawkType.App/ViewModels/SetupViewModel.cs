using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Onboarding;
using TawkType.Core.Pipeline;
using TawkType.Core.Settings;
using TawkType.Desktop.Services;
using TawkType.Transcription;
using TawkType.Windows.Audio;
using TawkType.Windows.Startup;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// First run: microphone, model, key, and a dictation that actually lands.
///
/// Two things make this different from a wizard that collects settings. Each answer is saved the
/// moment it is given rather than at the end, because the later steps have to use it — the meter
/// listens to the device just chosen, and the practice dictation is driven by the key just recorded,
/// through the same engine everything else uses. And nothing is taken on trust: the microphone gate
/// wants a level, the model gate wants an engine that loaded, and the last gate wants words back from
/// a real dictation. "Ready" here means it worked, not that the user agreed it would.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject, IDisposable
{
    /// <summary>The first entry of the device list, and what "no preference" is stored as.</summary>
    public const string SystemDefaultDevice = "(system default)";

    private readonly SettingsStore _store;
    private readonly ModelMaintenance _models;
    private readonly ITranscriber _transcriber;
    private readonly IAudioCapture _audio;
    private readonly IDictationHistory _history;
    private readonly DictationEngine _engine;
    private readonly ILogger<SetupViewModel> _logger;

    /// <summary>Set only when this really is the first run, so re-opening setup later cannot un-finish it.</summary>
    private readonly bool _isFirstRun;

    private CancellationTokenSource? _modelWork;
    private bool _metering;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyPropertyChangedFor(nameof(Blocker))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(ContinueText))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private SetupStep _step = SetupStep.Welcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyPropertyChangedFor(nameof(Blocker))]
    [NotifyPropertyChangedFor(nameof(ReadyLines))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private SetupState _state = new();

    [ObservableProperty]
    private string _selectedInputDevice;

    /// <summary>Live input, 0..1, for the meter.</summary>
    [ObservableProperty]
    private double _level;

    /// <summary>The loudest moment since this device was chosen. The verdict is made on this, not the last reading.</summary>
    [ObservableProperty]
    private double _peakLevel;

    [ObservableProperty]
    private string _microphoneMessage = "Say something — a sentence is plenty.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModelProgressIndeterminate))]
    private bool _isWorkingOnModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModelProgressIndeterminate))]
    private double? _modelProgress;

    [ObservableProperty]
    private string _modelSummary = string.Empty;

    [ObservableProperty]
    private string _modelStatus = string.Empty;

    [ObservableProperty]
    private string _hotkeyAdvice = string.Empty;

    /// <summary>Anything that is not "nothing known stands in its way", so the line can colour itself.</summary>
    [ObservableProperty]
    private bool _hotkeyAdviceIsWarning;

    /// <summary>What is in the practice box. Written by the injector, like any other text field.</summary>
    [ObservableProperty]
    private string _practiceText = string.Empty;

    [ObservableProperty]
    private string _practiceStatus = string.Empty;

    [ObservableProperty]
    private string _practiceDetail = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    public SetupViewModel(
        SettingsStore store,
        ModelMaintenance models,
        ITranscriber transcriber,
        IAudioCapture audio,
        IDictationHistory history,
        DictationEngine engine,
        ILogger<SetupViewModel> logger)
    {
        _store = store;
        _models = models;
        _transcriber = transcriber;
        _audio = audio;
        _history = history;
        _engine = engine;
        _logger = logger;

        _isFirstRun = store.Current.NeedsSetup;
        Draft = store.Current.Clone();

        InputDevices = new[] { SystemDefaultDevice }.Concat(WaveInAudioCapture.ListInputDevices()).ToArray();

        // The fields rather than the properties: the setters save settings and restart the meter, and
        // neither is wanted while the window is still being built.
        _selectedInputDevice = Draft.InputDeviceName ?? SystemDefaultDevice;
        _startWithWindows = WindowsStartup.IsEnabled();

        RefreshModel();
        RefreshHotkeyAdvice();

        _engine.Completed += OnDictationCompleted;
        _engine.Failed += OnDictationFailed;
    }

    /// <summary>Setup is over — finished or skipped. The window closes on it.</summary>
    public event EventHandler? Finished;

    /// <summary>A model arrived and loaded, so the rest of the app can stop saying there is none.</summary>
    public event EventHandler? ModelInstalled;

    /// <summary>
    /// Saved after every answer rather than at the end. The steps that follow use these settings for
    /// real — the meter opens this device, the engine loads this language's model, the hook binds this
    /// key — so a draft held back until Finish would mean practising against the old ones.
    /// </summary>
    public TawkTypeSettings Draft { get; }

    public IReadOnlyList<string> InputDevices { get; }

    public IReadOnlyList<object> LanguageOptions { get; } = LanguagePicker.Options();

    public int StepNumber => SetupPlan.Number(Step);

    public int StepCount => SetupPlan.Count;

    public double Progress => (double)StepNumber / StepCount;

    public bool CanContinue => SetupPlan.Check(Step, State).CanContinue;

    public string? Blocker => SetupPlan.Check(Step, State).Blocker;

    public bool CanGoBack => SetupPlan.Back(Step) is not null;

    public bool IsLastStep => Step == SetupStep.Done;

    public string ContinueText => IsLastStep ? "Start dictating" : "Continue";

    public bool IsModelProgressIndeterminate => IsWorkingOnModel && ModelProgress is null;

    public string HistoryPath => _history.Path;

    /// <summary>The key as it will read on the bar, for the step that asks the user to hold it.</summary>
    public string HoldKey => Core.Input.Hotkey.ParseOrDefault(Draft.Hotkey).ToString();

    /// <summary>The chosen language, as an item of <see cref="LanguageOptions"/>.</summary>
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
            Commit();
            OnPropertyChanged();
            RefreshModel();
        }
    }

    /// <summary>Two-way with the recorder, so a combination held down lands here.</summary>
    public string Hotkey
    {
        get => Draft.Hotkey;
        set
        {
            if (value == Draft.Hotkey)
            {
                return;
            }

            Draft.Hotkey = value;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HoldKey));
            OnPropertyChanged(nameof(ReadyLines));
            RefreshHotkeyAdvice();
        }
    }

    public bool SuppressHotkey
    {
        get => Draft.SuppressHotkey;
        set
        {
            if (value == Draft.SuppressHotkey)
            {
                return;
            }

            Draft.SuppressHotkey = value;
            Commit();
            OnPropertyChanged();
            RefreshHotkeyAdvice();
        }
    }

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
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadyLines));
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
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadyLines));
        }
    }

    /// <summary>
    /// What is true at the end, written from what happened rather than from what was chosen. Every
    /// line about the machine is something this window watched work.
    /// </summary>
    public IReadOnlyList<string> ReadyLines =>
    [
        State.MicrophoneHeard
            ? "TawkType heard your microphone."
            : "Your microphone was never heard, so nothing will be transcribed.",
        State.ModelReady
            ? $"{_models.ActiveModelLabel} is downloaded and loaded."
            : "No speech model is loaded, so dictation will do nothing.",
        State.DictationSucceeded
            ? $"A dictation worked. Hold {HoldKey} anywhere and speak."
            : $"No dictation has worked yet. Hold {HoldKey} and speak to try one.",
        Draft.History.Enabled
            ? "What you dictate is kept on this machine in plain text, where you can search and edit it."
            : "Nothing you dictate is written to disk.",
        Draft.Cleanup.UseLlm
            ? "Claude rewriting is on: finished transcripts are sent to Anthropic."
            : "Nothing leaves this machine.",
    ];

    /// <summary>
    /// Called when the window goes away or comes back. A hidden window holding the microphone open is
    /// not something anyone would guess was happening, so the meter follows what is on screen.
    /// </summary>
    public void SetOnScreen(bool onScreen)
    {
        if (onScreen && Step == SetupStep.Microphone)
        {
            StartMeter();
        }
        else if (!onScreen)
        {
            StopMeter();
        }
    }

    public void Dispose()
    {
        _engine.Completed -= OnDictationCompleted;
        _engine.Failed -= OnDictationFailed;
        _modelWork?.Cancel();
        StopMeter();
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (IsLastStep)
        {
            Finish(skipped: false);
            return;
        }

        if (SetupPlan.Next(Step) is { } next)
        {
            Step = next;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (SetupPlan.Back(Step) is { } previous)
        {
            Step = previous;
        }
    }

    /// <summary>
    /// The way out for someone who cannot finish today — no microphone yet, no bandwidth for the
    /// model. It marks setup done rather than leaving it to reappear at every launch; the tray menu
    /// is how they come back to it.
    /// </summary>
    [RelayCommand]
    private void Skip() => Finish(skipped: true);

    [RelayCommand]
    private void ClearPractice()
    {
        PracticeText = string.Empty;
        PracticeStatus = string.Empty;
        PracticeDetail = string.Empty;
    }

    /// <summary>
    /// Fetches the model the chosen language needs, then loads it. One command for both because they
    /// are one question to the user, and because a download that will not load is not a finished step.
    /// </summary>
    [RelayCommand]
    private async Task GetModelAsync()
    {
        if (IsWorkingOnModel)
        {
            return;
        }

        _modelWork?.Dispose();
        _modelWork = new CancellationTokenSource();
        var token = _modelWork.Token;

        IsWorkingOnModel = true;
        ModelProgress = null;

        try
        {
            if (!_models.IsActiveModelDownloaded)
            {
                ModelStatus = "Downloading " + _models.ActiveModelLabel + "…";
                await _models.DownloadAsync(
                    _models.ActiveModelKey,
                    new Progress<ModelProgress>(report => ModelProgress = report.Fraction),
                    token);

                State = State with { ModelDownloaded = true };
            }

            ModelProgress = null;
            ModelStatus = "Loading " + _models.ActiveModelLabel + "…";

            // The half a download does not prove. Doing it here is also why the practice dictation two
            // steps away is not the one that waits for a runtime to start for the first time.
            await _transcriber.WarmUpAsync(
                new Progress<ModelProgress>(report => ModelProgress = report.Fraction),
                token);

            State = State with { ModelDownloaded = true, ModelLoaded = true };
            ModelStatus = _models.ActiveModelLabel + " is ready.";
            ModelInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ModelStatus = "Stopped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "First run could not prepare the model");
            ModelStatus = "That did not work: " + (ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            IsWorkingOnModel = false;
            ModelProgress = null;
            RefreshModel();
        }
    }

    [RelayCommand]
    private void CancelModel() => _modelWork?.Cancel();

    partial void OnStepChanged(SetupStep value)
    {
        if (value == SetupStep.Microphone)
        {
            StartMeter();
        }
        else
        {
            StopMeter();
        }

        switch (value)
        {
            case SetupStep.Model:
                RefreshModel();

                // Already downloaded, by an earlier attempt or by hand: load it without being asked, so
                // the step opens on its real state rather than on a button whose effect is a guess.
                if (_models.IsActiveModelDownloaded && !State.ModelLoaded && !IsWorkingOnModel)
                {
                    _ = GetModelAsync();
                }

                break;

            case SetupStep.Practice:
                PracticeStatus = string.Empty;
                PracticeDetail = string.Empty;
                break;
        }
    }

    partial void OnSelectedInputDeviceChanged(string value)
    {
        StopMeter();

        Draft.InputDeviceName = value == SystemDefaultDevice ? null : value;
        Commit();

        // A device nobody has spoken into yet has not been heard, whatever the last one managed.
        PeakLevel = 0;
        Level = 0;
        State = State with { MicrophoneHeard = false };
        MicrophoneMessage = "Say something — a sentence is plenty.";

        if (Step == SetupStep.Microphone)
        {
            StartMeter();
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        var target = Environment.ProcessPath;
        if (target is null || !WindowsStartup.Set(value, target))
        {
            _logger.LogWarning("Could not change the Windows startup setting from first run");
        }
    }

    /// <summary>Opens the shared capture purely to watch the level. The clip it collects is thrown away.</summary>
    private void StartMeter()
    {
        if (_metering)
        {
            return;
        }

        try
        {
            _audio.LevelChanged += OnLevel;
            _audio.Start();
            _metering = true;
        }
        catch (Exception ex)
        {
            _audio.LevelChanged -= OnLevel;
            _logger.LogWarning(ex, "Could not open the microphone for the first-run meter");
            MicrophoneMessage = "Windows would not open that microphone: " + ex.Message;
        }
    }

    /// <summary>
    /// Gives the microphone back. If a dictation started while this step was open, the engine owns the
    /// capture now and stopping it here would take the user's recording away mid-sentence — so the
    /// meter lets go of the events and leaves the device alone.
    /// </summary>
    private void StopMeter()
    {
        if (!_metering)
        {
            return;
        }

        _metering = false;
        _audio.LevelChanged -= OnLevel;
        Level = 0;

        try
        {
            if (_engine.State == DictationState.Idle && _audio.IsCapturing)
            {
                _audio.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop the first-run microphone meter");
        }
    }

    private void OnLevel(object? sender, float level)
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Level = level;
            PeakLevel = Math.Max(PeakLevel, level);

            var reading = MicrophoneCheck.For((float)PeakLevel);
            MicrophoneMessage = reading.Message;

            if (reading.Heard)
            {
                State = State with { MicrophoneHeard = true };
            }
        });

    /// <summary>
    /// The practice dictation, arriving the way every other one does. The words are typed into the box
    /// by the injector rather than put there by this window, so what the user sees is the real path —
    /// and a delivery that failed shows up here instead of being counted as a success.
    /// </summary>
    private void OnDictationCompleted(object? sender, DictationCompleted completed)
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Step != SetupStep.Practice || string.IsNullOrWhiteSpace(completed.CleanText))
            {
                return;
            }

            State = State with { DictationSucceeded = true };

            switch (completed.Delivery)
            {
                case DictationDelivery.Typed:
                    PracticeStatus = "That worked.";
                    PracticeDetail = $"{completed.CleanText.Length} characters from "
                        + $"{completed.AudioDuration.TotalSeconds:F1} seconds of speech, transcribed in "
                        + $"{completed.TranscriptionTime.TotalMilliseconds:F0} ms.";
                    break;

                case DictationDelivery.CopiedToClipboard:
                    PracticeStatus = "Heard you, but it went to the clipboard.";
                    PracticeDetail = completed.Reason + ". Press Ctrl + V in the box to see it.";
                    break;

                default:
                    PracticeStatus = "Heard you, but it could not be typed.";
                    PracticeDetail = completed.Reason;
                    break;
            }
        });

    private void OnDictationFailed(object? sender, Exception error)
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Step != SetupStep.Practice)
            {
                return;
            }

            PracticeStatus = "That did not work.";
            PracticeDetail = error.InnerException?.Message ?? error.Message;
        });

    private void RefreshModel()
    {
        var engine = EngineSelection.Resolve(Draft.Engine, Draft.Language);
        var downloaded = _models.IsActiveModelDownloaded;

        State = State with
        {
            ModelDownloaded = downloaded,

            // A model that has been deleted has not been loaded, whatever happened earlier in the flow.
            ModelLoaded = State.ModelLoaded && downloaded,
        };

        var entry = _models.Catalogue().FirstOrDefault(model => model.Key == _models.ActiveModelKey);
        var size = entry is null || entry.IsDownloaded
            ? string.Empty
            : $" It is a {ModelStorage.FormatSize(entry.Bytes)} download, kept on this machine.";

        ModelSummary = engine == TranscriptionEngine.Parakeet
            ? $"{LanguageName} is transcribed by {_models.ActiveModelLabel}, which runs on the processor "
              + "and works the language out for itself." + size
            : $"{LanguageName} needs {_models.ActiveModelLabel}, which uses the graphics card where there "
              + "is one and the processor otherwise." + size;

        if (!IsWorkingOnModel)
        {
            ModelStatus = downloaded
                ? State.ModelLoaded
                    ? _models.ActiveModelLabel + " is ready."
                    : "Downloaded, but not loaded yet."
                : "Not downloaded. Nothing can be transcribed until it is.";
        }
    }

    private string LanguageName => SelectedLanguage?.Name ?? Draft.Language;

    private void RefreshHotkeyAdvice()
    {
        var advice = HotkeyCheck.For(Draft.Hotkey, Draft.ToggleHotkey, Draft.ModeHotkey, Draft.SuppressHotkey);

        HotkeyAdvice = advice.Message;
        HotkeyAdviceIsWarning = advice.Verdict != HotkeyVerdict.Fine;
        State = State with { HotkeyUsable = advice.Usable };
    }

    private void Finish(bool skipped)
    {
        Draft.SetupCompleted = true;
        Commit();

        _logger.LogInformation(
            "First run {Outcome} at step {Step}; ready = {Ready}",
            skipped ? "skipped" : "finished",
            Step,
            State.IsReady);

        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the answers given so far. Marked unfinished while the flow is running, so quitting
    /// halfway brings it back next launch rather than leaving a half-configured app with no way in.
    /// </summary>
    private void Commit()
    {
        if (_isFirstRun && Draft.SetupCompleted != true)
        {
            Draft.SetupCompleted = false;
        }

        try
        {
            _store.Save(Draft);
        }
        catch (Exception ex)
        {
            // Not worth stopping the flow over; the user can still finish, and Finish saves again.
            _logger.LogWarning(ex, "Could not save settings from first run");
        }
    }
}
