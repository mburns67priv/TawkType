using CommunityToolkit.Mvvm.ComponentModel;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;

namespace Talk2Me.Desktop.ViewModels;

/// <summary>Drives the floating status pill. All members must be touched on the UI thread.</summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private const double MeterWidth = 72;
    private static readonly TimeSpan IdleHideDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ErrorHideDelay = TimeSpan.FromSeconds(3);

    private readonly ISettingsProvider _settings;

    private CancellationTokenSource? _hideTimer;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// True while the pill is sitting idle on screen rather than reporting a dictation. The view fades
    /// it out at this point so a permanently visible pill is not a permanent distraction.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity))]
    private bool _isResting;

    /// <summary>Matches a DictationState name; the view maps it to a colour.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListening))]
    private string _stateKey = nameof(DictationState.Idle);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelWidth))]
    private float _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private double? _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isBusyWithModel;

    public OverlayViewModel(ISettingsProvider settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => ApplyVisibilityMode();
        ApplyVisibilityMode();
    }

    public bool IsListening => StateKey == nameof(DictationState.Listening);

    public double LevelWidth => Math.Max(4, Level * MeterWidth);

    public bool ShowProgress => IsBusyWithModel;

    public bool IsIndeterminate => IsBusyWithModel && Progress is null;

    public double Opacity => IsResting ? Math.Clamp(_settings.Current.Overlay.RestingOpacity, 0.05, 1.0) : 1.0;

    /// <summary>True when a state change should also bring the pill back to the front.</summary>
    public bool IsActive => !IsResting && IsVisible;

    public void ApplyState(DictationState state)
    {
        switch (state)
        {
            case DictationState.Listening:
                Level = 0;
                Show("Listening…", state);
                break;
            case DictationState.Transcribing:
                Show("Transcribing…", state);
                break;
            case DictationState.Polishing:
                Show("Polishing…", state);
                break;
            case DictationState.Injecting:
                Show("Typing…", state);
                break;
            case DictationState.Idle:
                if (!IsBusyWithModel)
                {
                    SettleAfter(IdleHideDelay);
                }

                break;
            case DictationState.Error:
                // Message arrives through ShowError.
                break;
        }
    }

    public void ShowError(string message)
    {
        Show(message, DictationState.Error);
        SettleAfter(ErrorHideDelay);
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
        SettleAfter(IdleHideDelay);
    }

    /// <summary>Re-reads the always-visible setting and rests or hides the pill accordingly.</summary>
    private void ApplyVisibilityMode()
    {
        if (IsResting || !IsVisible)
        {
            Settle();
        }
        else
        {
            // Mid-dictation: leave the pill alone and let the next settle pick the new mode up.
            OnPropertyChanged(nameof(Opacity));
        }
    }

    private void Show(string text, DictationState state)
    {
        CancelSettle();
        IsResting = false;
        StatusText = text;
        StateKey = state.ToString();
        IsVisible = true;
    }

    /// <summary>Idle presentation: dimmed and showing the hotkey, or gone if the user turned that off.</summary>
    private void Settle()
    {
        StateKey = nameof(DictationState.Idle);
        Level = 0;

        if (_settings.Current.Overlay.AlwaysVisible)
        {
            StatusText = $"Hold {FriendlyHotkey(_settings.Current.Hotkey)}";
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
        var cts = _hideTimer = new CancellationTokenSource();
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
        _hideTimer?.Cancel();
        _hideTimer = null;
    }

    /// <summary>"RightControl" reads badly on a pill; "Right Ctrl" does.</summary>
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
