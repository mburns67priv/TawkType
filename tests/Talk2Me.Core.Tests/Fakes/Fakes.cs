using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Tests.Fakes;

public sealed class FakeHotkey : IPushToTalkHotkey
{
    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public bool IsStarted { get; private set; }

    public void Start() => IsStarted = true;

    public void Stop() => IsStarted = false;

    public void Dispose()
    {
    }

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Release() => Released?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeAudioCapture : IAudioCapture
{
    public event EventHandler<float>? LevelChanged;

    public bool IsCapturing { get; private set; }

    public TimeSpan ClipDuration { get; set; } = TimeSpan.FromSeconds(2);

    public int StartCount { get; private set; }

    public void Start()
    {
        StartCount++;
        IsCapturing = true;
    }

    public AudioClip Stop()
    {
        IsCapturing = false;
        var samples = new float[(int)(ClipDuration.TotalSeconds * AudioClip.WhisperSampleRate)];
        return new AudioClip(samples, AudioClip.WhisperSampleRate);
    }

    public void RaiseLevel(float level) => LevelChanged?.Invoke(this, level);

    public void Dispose()
    {
    }
}

public sealed class FakeTranscriber : ITranscriber
{
    public string TextToReturn { get; set; } = "hello world";

    public Exception? ExceptionToThrow { get; set; }

    public List<AudioClip> Received { get; } = new();

    public Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default)
    {
        Received.Add(clip);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new TranscriptResult(TextToReturn, TimeSpan.FromMilliseconds(5)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeInjector : ITextInjector
{
    public List<string> Injected { get; } = new();

    public Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        Injected.Add(text);
        return Task.CompletedTask;
    }
}

public sealed class FakeSettings : ISettingsProvider
{
    public Talk2MeSettings Current { get; set; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
