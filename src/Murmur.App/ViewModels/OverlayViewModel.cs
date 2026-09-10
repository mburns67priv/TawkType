using CommunityToolkit.Mvvm.ComponentModel;
using Murmur.Core.Models;

namespace Murmur.Desktop.ViewModels;

/// <summary>Drives the floating status pill. All members must be touched on the UI thread.</summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private const double MeterWidth = 72;
    private static readonly TimeSpan IdleHideDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ErrorHideDelay = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _hideTimer;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

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

    public bool IsListening => StateKey == nameof(DictationState.Listening);

    public double LevelWidth => Math.Max(4, Level * MeterWidth);

    public bool ShowProgress => IsBusyWithModel;

    public bool IsIndeterminate => IsBusyWithModel && Progress is null;

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
            case DictationState.Injecting:
                Show("Typing…", state);
                break;
            case DictationState.Idle:
                if (!IsBusyWithModel)
                {
                    HideAfter(IdleHideDelay);
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
        HideAfter(ErrorHideDelay);
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
        HideAfter(IdleHideDelay);
    }

    private void Show(string text, DictationState state)
    {
        CancelHide();
        StatusText = text;
        StateKey = state.ToString();
        IsVisible = true;
    }

    private async void HideAfter(TimeSpan delay)
    {
        CancelHide();
        var cts = _hideTimer = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, cts.Token);
            IsVisible = false;
            StateKey = nameof(DictationState.Idle);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelHide()
    {
        _hideTimer?.Cancel();
        _hideTimer = null;
    }
}
