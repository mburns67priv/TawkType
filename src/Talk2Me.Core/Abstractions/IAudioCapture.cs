using Talk2Me.Core.Models;

namespace Talk2Me.Core.Abstractions;

/// <summary>Captures microphone audio as 16 kHz mono PCM float samples, ready for the transcriber.</summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Approximate input level in the range 0..1, raised roughly every 50 ms while capturing.</summary>
    event EventHandler<float>? LevelChanged;

    bool IsCapturing { get; }

    void Start();

    /// <summary>Stops capturing and returns everything recorded since <see cref="Start"/>.</summary>
    AudioClip Stop();
}
