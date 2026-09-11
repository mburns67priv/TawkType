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
    public bool IsModelReady { get; set; } = true;

    public string TextToReturn { get; set; } = "hello world";

    public Exception? ExceptionToThrow { get; set; }

    public List<AudioClip> Received { get; } = new();

    public Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>Blocks transcription until released, so a dictation can be caught mid-flight.</summary>
    public TaskCompletionSource Gate { get; } = new();

    public bool UseGate { get; set; }

    public async Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default)
    {
        Received.Add(clip);

        if (UseGate)
        {
            await Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return new TranscriptResult(TextToReturn, TimeSpan.FromMilliseconds(5));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeInjector : ITextInjector
{
    public List<string> Injected { get; } = new();

    /// <summary>Set to make delivery fail the way SendInput can.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow is not null)
        {
            return Task.FromException(ExceptionToThrow);
        }

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

public sealed class FakeFocusProbe : IFocusProbe
{
    public FocusTarget TargetToReturn { get; set; } = FocusTarget.Unknown;

    /// <summary>What is in front now. Set it to something else to simulate the user moving away.</summary>
    public long? Window { get; set; }

    public long? CurrentWindow() => Window;

    /// <summary>Delay before answering, so the engine's grace period can be exercised.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public int Probes { get; private set; }

    public FocusTarget Probe(CancellationToken cancellationToken = default)
    {
        Probes++;

        if (Delay > TimeSpan.Zero)
        {
            Thread.Sleep(Delay);
        }

        return TargetToReturn;
    }
}

public sealed class FakeClipboard : IClipboard
{
    public List<string> Copied { get; } = new();

    public Exception? ExceptionToThrow { get; set; }

    public void SetText(string text)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        Copied.Add(text);
    }
}
