using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Pipeline;

/// <summary>
/// The push-to-talk state machine:
/// Idle --press--> Listening --release--> Transcribing --> Injecting --> Idle.
/// Presses while busy are ignored; failures surface through <see cref="Failed"/> and return to Idle.
///
/// Each dictation owns a session: an id, a cancellation source, and the task doing the work. That is
/// what makes stopping mean something. Without it, Stop unsubscribed from the hotkey and returned
/// while a dictation carried on transcribing, rewriting and typing into whatever was in front —
/// through a shutdown that was busy disposing the very services it was using.
/// </summary>
public sealed class DictationEngine : IDisposable
{
    private readonly IPushToTalkHotkey _hotkey;
    private readonly IAudioCapture _audio;
    private readonly ITranscriber _transcriber;
    private readonly ITextCleaner _cleaner;
    private readonly ITextInjector _injector;
    private readonly IFocusProbe _focus;
    private readonly IClipboard _clipboard;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<DictationEngine> _logger;
    private readonly object _gate = new();

    /// <summary>How long the probe may still be running once there is text ready to deliver.</summary>
    private static readonly TimeSpan FocusProbeGrace = TimeSpan.FromMilliseconds(400);

    /// <summary>Shorter than the focus probe's: this one is on the path of already-finished text.</summary>
    private static readonly TimeSpan CaretGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>Long enough for a rewrite and an injection to finish; short enough to close the app.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(3);

    /// <summary>Stops the recording running away when a key is stuck, a book is on it, or a toggle is left on.</summary>
    private Timer? _recordingLimit;

    private DictationState _state = DictationState.Idle;
    private long _pressedAtTicks;
    private bool _started;

    /// <summary>The dictation in flight, or null. Guarded by <see cref="_gate"/>.</summary>
    private Session? _session;

    private long _nextSessionId;

    /// <summary>Started when the key goes down and read when it comes up, so the probe is free.</summary>
    private Task<FocusTarget>? _focusProbe;

    public DictationEngine(
        IPushToTalkHotkey hotkey,
        IAudioCapture audio,
        ITranscriber transcriber,
        ITextCleaner cleaner,
        ITextInjector injector,
        IFocusProbe focus,
        IClipboard clipboard,
        ISettingsProvider settings,
        ILogger<DictationEngine> logger)
    {
        _hotkey = hotkey;
        _audio = audio;
        _transcriber = transcriber;
        _cleaner = cleaner;
        _injector = injector;
        _focus = focus;
        _clipboard = clipboard;
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

    /// <summary>
    /// The words exist, before anything has been done with them. Subscribers can hold on to the result
    /// so that a delivery which fails — or a window that has moved on — does not lose what was said.
    /// Carries <see cref="DictationDelivery.Pending"/>; <see cref="Completed"/> carries the outcome.
    /// </summary>
    public event EventHandler<DictationCompleted>? Recognised;

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
        _hotkey.CancelRequested += OnCancelRequested;
        _audio.LevelChanged += OnLevelChanged;
        _hotkey.Start();
        _logger.LogInformation("Dictation engine started; hotkey = {Hotkey}", _settings.Current.Hotkey);
    }

    /// <summary>
    /// Refuses new dictations, ends any in flight, and waits a moment for it to unwind before
    /// returning. The wait is the point: the caller is about to dispose the transcriber, the injector
    /// and the settings this work is still holding.
    /// </summary>
    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        // Nothing new, from this line on.
        _started = false;
        _hotkey.Stop();
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _hotkey.CancelRequested -= OnCancelRequested;
        _audio.LevelChanged -= OnLevelChanged;

        CancelDictation();
        StopRecordingLimit();

        Task? work;
        lock (_gate)
        {
            work = _session?.Work;
        }

        if (work is not null && !work.Wait(ShutdownGrace))
        {
            _logger.LogWarning("A dictation was still running after {Seconds}s; leaving it", ShutdownGrace.TotalSeconds);
        }

        StopCapture();
        DiscardSession();
    }

    /// <summary>
    /// Abandons the dictation in flight, if any. Nothing is typed, nothing is recorded. The hook stays
    /// installed, so the next press starts a new one.
    /// </summary>
    public void CancelDictation()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null || session.Cancelled)
        {
            return;
        }

        _logger.LogInformation("Dictation {Id} cancelled", session.Id);
        session.Cancel();
        StopRecordingLimit();
        StopCapture();
        SetState(DictationState.Idle);

        // Cancelled while still listening: no worker will ever run, so nothing else will clear it up.
        if (session.Work is null)
        {
            DiscardSession(session);
        }
    }

    public void Dispose() => Stop();

    /// <summary>Starts listening as if the hotkey were pressed. Used by the tray "test" action and tooling.</summary>
    public void BeginDictation() => OnPressed(this, EventArgs.Empty);

    /// <summary>Stops listening and runs the transcribe/clean/inject pipeline, as if the hotkey were released.</summary>
    public void EndDictation() => OnReleased(this, EventArgs.Empty);

    private void OnLevelChanged(object? sender, float level) => AudioLevelChanged?.Invoke(this, level);

    private void OnCancelRequested(object? sender, EventArgs e) => CancelDictation();

    private void OnPressed(object? sender, EventArgs e)
    {
        DictationState? started;

        lock (_gate)
        {
            if (!_started)
            {
                return; // shutting down
            }

            if (_state != DictationState.Idle)
            {
                _logger.LogDebug("Hotkey pressed while {State}; ignoring", _state);
                return;
            }

            _session?.Dispose();
            _session = new Session(Interlocked.Increment(ref _nextSessionId));
            _pressedAtTicks = Stopwatch.GetTimestamp();

            // From here until the session is discarded, Escape belongs to us rather than to whatever
            // the user is typing into.
            _hotkey.DictationInProgress = true;
            StartRecordingLimit();
            started = SetStateLocked(DictationState.Listening);
        }

        Announce(started);

        // Off the hook thread immediately: hooks have a tight time budget, and the probe talks to
        // another process. It has the whole utterance to answer in, so nothing waits on it here.
        _focusProbe = Task.Run(() =>
        {
            try
            {
                return _focus.Probe();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Focus probe failed; assuming the target can be typed into");
                return FocusTarget.Unknown;
            }
        });

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
        Session session;
        TimeSpan heldFor;
        DictationState? transcribing;

        lock (_gate)
        {
            if (_state != DictationState.Listening || _session is null)
            {
                return;
            }

            session = _session;

            // Measured here rather than in the worker: a busy machine can delay the worker by longer
            // than the minimum hold, which turned an accidental tap into an accepted recording.
            heldFor = Stopwatch.GetElapsedTime(_pressedAtTicks);
            transcribing = SetStateLocked(DictationState.Transcribing);
            StopRecordingLimit();
        }

        Announce(transcribing);

        // Leave the caller's thread (the keyboard hook) immediately; hooks have a tight time budget.
        session.Work = Task.Run(() => ProcessAsync(session, heldFor));
    }

    private async Task ProcessAsync(Session session, TimeSpan heldFor)
    {
        var token = session.Token;

        try
        {
            var clip = _audio.Stop();

            if (heldFor.TotalMilliseconds < _settings.Current.MinimumHoldMs || clip.Samples.Length == 0)
            {
                _logger.LogDebug("Ignoring {Ms} ms tap", (int)heldFor.TotalMilliseconds);
                SetState(DictationState.Idle);
                return;
            }

            var transcript = await _transcriber.TranscribeAsync(clip, token).ConfigureAwait(false);

            if (_cleaner.MayTakeAWhile)
            {
                SetState(DictationState.Polishing);
            }

            var clean = await _cleaner.CleanAsync(transcript.Text, token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(clean))
            {
                _logger.LogInformation("Nothing recognised in {Seconds:F1}s clip", clip.Duration.TotalSeconds);
                SetState(DictationState.Idle);
                return;
            }

            token.ThrowIfCancellationRequested();

            var target = await ResolveFocusAsync().ConfigureAwait(false);

            var result = new DictationCompleted(
                transcript.Text,
                clean,
                clip.Duration,
                transcript.ProcessingTime);

            // Before delivery, not after. Injection is the step most likely to fail, and a result
            // announced only on success is missing exactly when the user needs to get it back.
            Recognised?.Invoke(this, result with { Delivery = DictationDelivery.Pending });

            var delivery = DictationDelivery.Typed;
            var reason = string.Empty;

            if (MovedAway(target) is { } movedReason)
            {
                delivery = DictationDelivery.CopiedToClipboard;
                reason = movedReason;
                _clipboard.SetText(clean);
            }
            else if (target.CanType)
            {
                // Only when typing: spacing and capitals are about the place the text is landing, and
                // mean nothing on the clipboard. Kept off `clean` so a fallback copy does not carry
                // them, and so the history records the words rather than their punctuation.
                var mode = _settings.Current.ActiveModeOrDefault();
                var caret = await ReadCaretAsync(token).ConfigureAwait(false);
                var typed = CaretFit.Fit(
                    clean,
                    caret,
                    mode.AppendTrailingSpace,
                    // Only ever undoing a capital cleanup added, and only in a mode that adds one.
                    mode.Capitalise && CaretFit.WasCapitalisedByCleanup(transcript.Text, clean));

                SetState(DictationState.Injecting);

                try
                {
                    await _injector.InjectAsync(typed, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not deliver the dictation; keeping the text");
                    delivery = DictationDelivery.Failed;
                    reason = KeepForRecovery(clean);
                }
            }
            else
            {
                delivery = DictationDelivery.CopiedToClipboard;
                reason = target.Explain();
                _clipboard.SetText(clean);
            }

            _logger.LogInformation(
                "Dictated {Chars} chars from {Audio:F1}s audio in {Ms} ms; {Delivery} ({Target})",
                clean.Length,
                clip.Duration.TotalSeconds,
                (int)transcript.ProcessingTime.TotalMilliseconds,
                delivery,
                target.Description ?? target.Verdict.ToString());

            Completed?.Invoke(this, result with { Delivery = delivery, Reason = reason, Target = target });

            SetState(DictationState.Idle);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogInformation("Dictation {Id} abandoned", session.Id);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            DiscardSession(session);
        }
    }

    /// <summary>
    /// Null when the window the user was speaking into is still in front; otherwise why it is no longer
    /// safe to type. The probe answers for the moment the key went down, and transcription can take
    /// seconds — long enough to alt-tab into a chat window and have the last sentence sent to someone.
    ///
    /// Conservative on purpose: an unknown identity counts as unchanged, because refusing to type into
    /// a window that would have been fine is its own kind of failure. It compares windows, not fields,
    /// so moving between two boxes in the same form still looks unchanged.
    /// </summary>
    private string? MovedAway(FocusTarget target)
    {
        if (target.Window is not { } probed)
        {
            return null;
        }

        var now = _focus.CurrentWindow();
        if (now is null || now == probed)
        {
            return null;
        }

        _logger.LogInformation("Focus moved while transcribing; copying rather than typing elsewhere");
        return "You moved window — it is on the clipboard";
    }

    /// <summary>
    /// Puts a result the user cannot see anywhere else within reach. Returns what to tell them.
    /// </summary>
    private string KeepForRecovery(string clean)
    {
        try
        {
            _clipboard.SetText(clean);
            return "Could not type it — it is on the clipboard";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not put the undelivered dictation on the clipboard either");
            return "Could not type it — open History to copy it";
        }
    }

    /// <summary>
    /// The probe's answer, or Unknown if it is still running. It is given a slice of the transcription
    /// time to finish; a slow accessibility tree must never hold up text the user is waiting for.
    /// </summary>
    private async Task<FocusTarget> ResolveFocusAsync()
    {
        if (_focusProbe is null)
        {
            return FocusTarget.Unknown;
        }

        var probe = _focusProbe;
        _focusProbe = null;

        var finished = await Task.WhenAny(probe, Task.Delay(FocusProbeGrace)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, probe))
        {
            _logger.LogDebug("Focus probe still running at injection time; assuming typeable");
            return FocusTarget.Unknown;
        }

        return await probe.ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the text around the caret, on a pool thread and against a short deadline.
    ///
    /// It has to happen here rather than at key-down with the rest of the probe, because the caret is
    /// exactly the thing that moves while a dictation is being transcribed. That makes it the one
    /// accessibility call on the hot path, so it gets a small budget and an answer of "do not know"
    /// the moment it runs out — finished text must never wait on somebody else's message loop.
    /// </summary>
    private async Task<CaretContext> ReadCaretAsync(CancellationToken token)
    {
        if (!_settings.Current.ActiveModeOrDefault().FitToCaret)
        {
            return CaretContext.Unknown;
        }

        try
        {
            var read = Task.Run(() => _focus.ReadCaret(token), token);
            var finished = await Task.WhenAny(read, Task.Delay(CaretGrace, token)).ConfigureAwait(false);

            if (finished != read)
            {
                _logger.LogDebug("Reading around the caret took too long; delivering without it");
                return CaretContext.Unknown;
            }

            return await read.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not read around the caret");
            return CaretContext.Unknown;
        }
    }

    /// <summary>
    /// Finishes the recording on its own after the configured time. Deliberately a finish rather than a
    /// cancel: whatever was said up to that point is worth more than the silence after it.
    /// </summary>
    private void StartRecordingLimit()
    {
        StopRecordingLimit();

        var seconds = _settings.Current.MaxRecordingSeconds;
        if (seconds <= 0)
        {
            return;
        }

        _recordingLimit = new Timer(
            _ =>
            {
                _logger.LogInformation("Recording reached the {Seconds}s limit; finishing it", seconds);
                OnReleased(this, EventArgs.Empty);
            },
            null,
            TimeSpan.FromSeconds(seconds),
            Timeout.InfiniteTimeSpan);
    }

    private void StopRecordingLimit()
    {
        var timer = Interlocked.Exchange(ref _recordingLimit, null);
        timer?.Dispose();
    }

    private void StopCapture()
    {
        try
        {
            if (_audio.IsCapturing)
            {
                _audio.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop audio capture");
        }
    }

    /// <summary>Forgets the session, if it is still the current one. Idempotent.</summary>
    private void DiscardSession(Session? only = null)
    {
        lock (_gate)
        {
            if (_session is null || (only is not null && !ReferenceEquals(_session, only)))
            {
                return;
            }

            _session.Dispose();
            _session = null;
        }

        _hotkey.DictationInProgress = false;
    }

    private void Fail(Exception ex)
    {
        _logger.LogError(ex, "Dictation failed");
        StopCapture();
        SetState(DictationState.Error);
        Failed?.Invoke(this, ex);
        SetState(DictationState.Idle);
    }

    private void SetState(DictationState next)
    {
        DictationState? changed;

        lock (_gate)
        {
            changed = SetStateLocked(next);
        }

        Announce(changed);
    }

    /// <summary>
    /// Records the new state and returns it if it changed, for the caller to announce once the lock is
    /// released. Subscribers draw windows and touch the clipboard; running them inside the lock meant
    /// an unrelated hotkey press could block on somebody's UI thread.
    /// </summary>
    private DictationState? SetStateLocked(DictationState next)
    {
        if (_state == next)
        {
            return null;
        }

        _state = next;
        return next;
    }

    private void Announce(DictationState? changed)
    {
        if (changed is { } state)
        {
            StateChanged?.Invoke(this, state);
        }
    }

    /// <summary>
    /// One dictation, from key-down to delivery. Owning the cancellation source and the task here is
    /// what lets anyone else — a shutdown, a cancel — end this particular piece of work and know when
    /// it has actually stopped.
    /// </summary>
    private sealed class Session(long id) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();

        public long Id => id;

        public CancellationToken Token => _cancellation.Token;

        public bool Cancelled => _cancellation.IsCancellationRequested;

        /// <summary>The processing task, once the key has come up. Null while still listening.</summary>
        public Task? Work { get; set; }

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished and cleaned up; there is nothing left to cancel.
            }
        }

        public void Dispose() => _cancellation.Dispose();
    }
}
