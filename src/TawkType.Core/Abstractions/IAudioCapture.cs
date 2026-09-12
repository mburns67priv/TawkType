using TawkType.Core.Models;

namespace TawkType.Core.Abstractions;

/// <summary>Captures microphone audio as 16 kHz mono PCM float samples, ready for the transcriber.</summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// RMS of the last window of audio, 0..1, raised roughly every 50 ms while capturing. Raw: no
    /// display gain is applied here. <c>AudioLevel.Meter</c> maps it for a meter and
    /// <c>MicrophoneCheck</c> judges it; both are calibrated on this unit.
    /// </summary>
    event EventHandler<float>? LevelChanged;

    bool IsCapturing { get; }

    void Start();

    /// <summary>Stops capturing and returns everything recorded since <see cref="Start"/>.</summary>
    AudioClip Stop();
}
