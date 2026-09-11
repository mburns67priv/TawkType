using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Desktop.ViewModels;

/// <summary>One bar of the level meter. Its own object so the bars animate without rebuilding the list.</summary>
public sealed partial class WaveBar : ObservableObject
{
    [ObservableProperty]
    private double _height = WaveMinimum;

    public const double WaveMinimum = 3;

    public const double WaveMaximum = 26;
}

/// <summary>
/// Drives the floating status bar. All members must be touched on the UI thread.
/// </summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    /// <summary>Enough bars to read as a waveform, few enough to stay legible at this width.</summary>
    private const int BarCount = 21;

    private static readonly TimeSpan IdleSettleDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ErrorSettleDelay = TimeSpan.FromSeconds(3);

    private readonly SettingsStore _settings;
    private readonly IDictationHistory _history;
    private readonly DispatcherTimer _elapsedTimer;

    private CancellationTokenSource? _settleTimer;
    private CancellationTokenSource? _statusTimer;
    private long _listeningSince;
    private bool _wasAlwaysVisible;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Minimised out of the way. Session-only; the tray icon brings it back.</summary>
    [ObservableProperty]
    private bool _isHidden;

    /// <summary>
    /// No model on disk for the selected engine. The bar says so instead of showing the hotkey: the
    /// hotkey does nothing until a model is downloaded, and there is nowhere else the user would look.
    /// </summary>
    [ObservableProperty]
    private bool _modelMissing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// True while the bar is sitting idle on screen rather than reporting a dictation. The view fades
    /// it out at this point so a permanently visible bar is not a permanent distraction.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity))]
    private bool _isResting;

    /// <summary>Matches a DictationState name; the view maps it to a colour.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListening))]
    private string _stateKey = nameof(DictationState.Idle);

    /// <summary>"2.5s" while listening; empty otherwise.</summary>
    [ObservableProperty]
    private string _elapsedText = string.Empty;

    /// <summary>Transient toast for the toolbar actions, e.g. after Copy.</summary>
    [ObservableProperty]
    private string _toast = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private double? _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isBusyWithModel;

    public OverlayViewModel(SettingsStore settings, IDictationHistory history)
    {
        _settings = settings;
        _history = history;

        for (var i = 0; i < BarCount; i++)
        {
            Bars.Add(new WaveBar());
        }

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _elapsedTimer.Tick += (_, _) =>
            ElapsedText = $"{Stopwatch.GetElapsedTime(_listeningSince).TotalSeconds:F1}s";

        _wasAlwaysVisible = settings.Current.Overlay.AlwaysVisible;
        _settings.Changed += (_, _) => ApplyVisibilityMode();
        ApplyVisibilityMode();
    }

    /// <summary>Raised when the toolbar asks the app to open a window; the App owns those.</summary>
    public event EventHandler? SettingsRequested;

    public event EventHandler? HistoryRequested;

    public ObservableCollection<WaveBar> Bars { get; } = new();

    public bool IsListening => StateKey == nameof(DictationState.Listening);

    public bool ShowProgress => IsBusyWithModel;

    public bool IsIndeterminate => IsBusyWithModel && Progress is null;

    public double Opacity => IsResting ? Math.Clamp(_settings.Current.Overlay.RestingOpacity, 0.05, 1.0) : 1.0;

    public void ApplyState(DictationState state)
    {
        switch (state)
        {
            case DictationState.Listening:
                StartListening();
                break;
            case DictationState.Transcribing:
                StopListening();
                Show("Transcribing…", state);
                break;
            case DictationState.Polishing:
                Show("Polishing…", state);
                break;
            case DictationState.Injecting:
                Show("Typing…", state);
                break;
            case DictationState.Idle:
                StopListening();
                if (!IsBusyWithModel)
                {
                    SettleAfter(IdleSettleDelay);
                }

                break;
            case DictationState.Error:
                StopListening();
                break;
        }
    }

    /// <summary>Feeds the level meter. 0..1.</summary>
    public void PushLevel(float level)
    {
        // Shift left by one and put the newest sample at the right, so the wave scrolls as you speak.
        for (var i = 0; i < Bars.Count - 1; i++)
        {
            Bars[i].Height = Bars[i + 1].Height;
        }

        var scaled = WaveBar.WaveMinimum + (Math.Clamp(level, 0, 1) * (WaveBar.WaveMaximum - WaveBar.WaveMinimum));
        Bars[^1].Height = scaled;
    }

    /// <summary>
    /// A non-error result worth reading, e.g. the text went to the clipboard because the focused
    /// window could not take it. Sits on the bar for a few seconds like an error, but without the
    /// error colour.
    /// </summary>
    public void ShowNotice(string message, string? detail = null)
    {
        Show(message, DictationState.Injecting);
        Flash(string.IsNullOrWhiteSpace(detail) ? message : detail);
        SettleAfter(ErrorSettleDelay);
    }

    public void ShowError(string message)
    {
        Show(message, DictationState.Error);
        SettleAfter(ErrorSettleDelay);
    }

    public void ReportProgress(ModelProgress progress)
    {
        IsBusyWithModel = true;
        Progress = progress.Fraction;
        var text = progress.Fraction is { } fraction
            ? $"{progress.Stage}… {fraction:P0}"
            : $"{progress.Stage}…";
        Show(text, DictationState.Transcribing);
    }

    public void HideProgress()
    {
        IsBusyWithModel = false;
        Progress = null;
        SettleAfter(IdleSettleDelay);
    }

    /// <summary>Persists where the user dragged the bar to.</summary>
    public void SavePlacement(double left, double top)
    {
        var next = _settings.Current.Clone();
        next.Overlay.WindowLeft = left;
        next.Overlay.WindowTop = top;
        _settings.Save(next);
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenHistory() => HistoryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CopyLast()
    {
        if (_history.Last is not { } record || string.IsNullOrWhiteSpace(record.FinalText))
        {
            Flash("Nothing dictated yet");
            return;
        }

        try
        {
            Clipboard.SetText(record.FinalText);
            Flash("Copied");
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; never worth an error dialog from the bar.
            Flash(ex.Message.Length > 28 ? "Could not copy" : ex.Message);
        }
    }

    /// <summary>
    /// The minimise button: puts the bar away entirely, including while dictating. Deliberately not
    /// persisted — minimising is a "not right now", and a restart brings the bar back. The way back in
    /// the meantime is the tray icon, so <see cref="Restore"/> is what that calls.
    /// </summary>
    [RelayCommand]
    private void Hide()
    {
        IsHidden = true;
        IsResting = false;
        IsVisible = false;
    }

    /// <summary>
    /// The close button. It closes Talk2Me — a close button that only hid the bar read as broken, and
    /// the button beside it already hides it. Dictation stops with the app, so this is the one control
    /// on the bar that ends the session.
    /// </summary>
    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The close button was pressed; the app should shut down.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Brings the bar back from the tray, whichever way it went away. It undoes both the minimise
    /// button and the close button, because from the tray they look like the same problem: the bar is
    /// not there and the user wants it back.
    /// </summary>
    public void Restore()
    {
        IsHidden = false;

        if (!_settings.Current.Overlay.AlwaysVisible)
        {
            var next = _settings.Current.Clone();
            next.Overlay.AlwaysVisible = true;
            _settings.Save(next); // this fires Changed, which settles the bar back onto the screen
        }
        else
        {
            Settle();
        }

        // Asking for the bar when it is already on screen must still do something visible, or the menu
        // item looks broken. This puts it back in front and back on a monitor that still exists.
        AttentionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The bar has been asked for explicitly; show it and raise it, wherever it was.</summary>
    public event EventHandler? AttentionRequested;

    /// <summary>Re-settles so the resting line switches between the hotkey and the download prompt.</summary>
    partial void OnModelMissingChanged(bool value)
    {
        if (IsResting || !IsVisible)
        {
            Settle();
        }
    }

    private void StartListening()
    {
        foreach (var bar in Bars)
        {
            bar.Height = WaveBar.WaveMinimum;
        }

        _listeningSince = Stopwatch.GetTimestamp();
        ElapsedText = "0.0s";
        _elapsedTimer.Start();
        Show("Listening", DictationState.Listening);
    }

    private void StopListening()
    {
        _elapsedTimer.Stop();
        ElapsedText = string.Empty;
    }

    /// <summary>Re-reads the always-visible setting and rests or hides the bar accordingly.</summary>
    private void ApplyVisibilityMode()
    {
        var alwaysVisible = _settings.Current.Overlay.AlwaysVisible;

        // Ticking "keep the bar on screen" back on is an explicit ask for the bar, so it also undoes a
        // minimise. Only on the transition: an unrelated settings save should not un-minimise it.
        if (alwaysVisible && !_wasAlwaysVisible)
        {
            IsHidden = false;
        }

        _wasAlwaysVisible = alwaysVisible;

        if (IsResting || !IsVisible)
        {
            Settle();
        }
        else
        {
            // Mid-dictation: leave the bar alone and let the next settle pick the new mode up.
            OnPropertyChanged(nameof(Opacity));
        }
    }

    private void Show(string text, DictationState state)
    {
        CancelSettle();
        IsResting = false;
        StatusText = text;
        StateKey = state.ToString();
        IsVisible = !IsHidden;
    }

    /// <summary>Idle presentation: dimmed and showing the hotkey, or gone if the user turned that off.</summary>
    private void Settle()
    {
        StateKey = nameof(DictationState.Idle);

        foreach (var bar in Bars)
        {
            bar.Height = WaveBar.WaveMinimum;
        }

        if (_settings.Current.Overlay.AlwaysVisible && !IsHidden)
        {
            StatusText = ModelMissing
                ? "Download a model in Settings"
                : $"Hold {FriendlyHotkey(_settings.Current.Hotkey)}";
            IsResting = true;
            IsVisible = true;
        }
        else
        {
            IsResting = false;
            IsVisible = false;
        }
    }

    private async void SettleAfter(TimeSpan delay)
    {
        CancelSettle();
        var cts = _settleTimer = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, cts.Token);
            Settle();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelSettle()
    {
        _settleTimer?.Cancel();
        _settleTimer = null;
    }

    private async void Flash(string message)
    {
        _statusTimer?.Cancel();
        var cts = _statusTimer = new CancellationTokenSource();
        Toast = message;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.6), cts.Token);
            Toast = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>"RightControl" reads badly on a bar; "Right Ctrl" does.</summary>
    private static string FriendlyHotkey(string hotkey) => hotkey switch
    {
        "RightControl" => "Right Ctrl",
        "LeftControl" => "Left Ctrl",
        "RightShift" => "Right Shift",
        "LeftShift" => "Left Shift",
        "RightAlt" => "Right Alt",
        "LeftAlt" => "Left Alt",
        "CapsLock" => "Caps Lock",
        _ => hotkey,
    };
}
