using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;

namespace Talk2Me.Core.Pipeline;

/// <summary>
/// The push-to-talk state machine:
/// Idle --press--> Listening --release--> Transcribing --> Injecting --> Idle.
/// Presses while busy are ignored; failures surface through <see cref="Failed"/> and return to Idle.
/// </summary>
public sealed class DictationEngine : IDisposable
{
    private readonly IPushToTalkHotkey _hotkey;
    private readonly IAudioCapture _audio;
    private readonly ITranscriber _transcriber;
    private readonly ITextCleaner _cleaner;
    private readonly ITextInjector _injector;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<DictationEngine> _logger;
    private readonly object _gate = new();

    private DictationState _state = DictationState.Idle;
    private long _pressedAtTicks;
    private bool _started;

    public DictationEngine(
        IPushToTalkHotkey hotkey,
        IAudioCapture audio,
        ITranscriber transcriber,
        ITextCleaner cleaner,
        ITextInjector injector,
        ISettingsProvider settings,
        ILogger<DictationEngine> logger)
    {
        _hotkey = hotkey;
        _audio = audio;
        _transcriber = transcriber;
        _cleaner = cleaner;
        _injector = injector;
        _settings = settings;
        _logger = logger;
    }

    public DictationState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<DictationState>? StateChanged;

    public event EventHandler<DictationCompleted>? Completed;

    public event EventHandler<Exception>? Failed;

    /// <summary>Forwarded from the audio capture while listening; 0..1.</summary>
    public event EventHandler<float>? AudioLevelChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        _audio.LevelChanged += OnLevelChanged;
        _hotkey.Start();
        _logger.LogInformation("Dictation engine started; hotkey = {Hotkey}", _settings.Current.Hotkey);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _hotkey.Stop();
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _audio.LevelChanged -= OnLevelChanged;
    }

    public void Dispose() => Stop();

    /// <summary>Starts listening as if the hotkey were pressed. Used by the tray "test" action and tooling.</summary>
    public void BeginDictation() => OnPressed(this, EventArgs.Empty);

    /// <summary>Stops listening and runs the transcribe/clean/inject pipeline, as if the hotkey were released.</summary>
    public void EndDictation() => OnReleased(this, EventArgs.Empty);

    private void OnLevelChanged(object? sender, float level) => AudioLevelChanged?.Invoke(this, level);

    private void OnPressed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_state != DictationState.Idle)
            {
                _logger.LogDebug("Hotkey pressed while {State}; ignoring", _state);
                return;
            }

            _pressedAtTicks = Stopwatch.GetTimestamp();
            SetStateLocked(DictationState.Listening);
        }

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_state != DictationState.Listening)
            {
                return;
            }

            SetStateLocked(DictationState.Transcribing);
        }

        // Leave the caller's thread (the keyboard hook) immediately; hooks have a tight time budget.
        _ = Task.Run(ProcessAsync);
    }

    private async Task ProcessAsync()
    {
        try
        {
            var heldFor = Stopwatch.GetElapsedTime(_pressedAtTicks);
            var clip = _audio.Stop();

            if (heldFor.TotalMilliseconds < _settings.Current.MinimumHoldMs || clip.Samples.Length == 0)
            {
                _logger.LogDebug("Ignoring {Ms} ms tap", (int)heldFor.TotalMilliseconds);
                SetState(DictationState.Idle);
                return;
            }

            var transcript = await _transcriber.TranscribeAsync(clip).ConfigureAwait(false);

            if (_cleaner.MayTakeAWhile)
            {
                SetState(DictationState.Polishing);
            }

            var clean = await _cleaner.CleanAsync(transcript.Text).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(clean))
            {
                _logger.LogInformation("Nothing recognised in {Seconds:F1}s clip", clip.Duration.TotalSeconds);
                SetState(DictationState.Idle);
                return;
            }

            if (_settings.Current.AppendTrailingSpace)
            {
                clean += " ";
            }

            SetState(DictationState.Injecting);
            await _injector.InjectAsync(clean).ConfigureAwait(false);

            _logger.LogInformation(
                "Dictated {Chars} chars from {Audio:F1}s audio in {Ms} ms",
                clean.Length,
                clip.Duration.TotalSeconds,
                (int)transcript.ProcessingTime.TotalMilliseconds);

            Completed?.Invoke(this, new DictationCompleted(transcript.Text, clean, clip.Duration, transcript.ProcessingTime));
            SetState(DictationState.Idle);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        _logger.LogError(ex, "Dictation failed");

        try
        {
            if (_audio.IsCapturing)
            {
                _audio.Stop();
            }
        }
        catch (Exception stopEx)
        {
            _logger.LogWarning(stopEx, "Could not stop audio capture after failure");
        }

        SetState(DictationState.Error);
        Failed?.Invoke(this, ex);
        SetState(DictationState.Idle);
    }

    private void SetState(DictationState next)
    {
        lock (_gate)
        {
            SetStateLocked(next);
        }
    }

    private void SetStateLocked(DictationState next)
    {
        if (_state == next)
        {
            return;
        }

        _state = next;
        StateChanged?.Invoke(this, next);
    }
}
