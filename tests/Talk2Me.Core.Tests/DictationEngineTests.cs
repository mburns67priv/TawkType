using Microsoft.Extensions.Logging.Abstractions;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Pipeline;
using Talk2Me.Core.Tests.Fakes;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

public sealed class DictationEngineTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly List<DictationState> _states = new();

    [Fact]
    public async Task Press_then_release_transcribes_cleans_and_injects()
    {
        _transcriber.TextToReturn = "um hello world";
        using var engine = CreateEngine(_transcriber);

        _hotkey.Press();
        Assert.Equal(DictationState.Listening, engine.State);
        Assert.True(_audio.IsCapturing);

        await Task.Delay(_settings.Current.MinimumHoldMs + 50);
        _hotkey.Release();
        await WaitForIdleAsync(engine);

        Assert.Single(_transcriber.Received);
        Assert.Equal(new[] { "Hello world " }, _injector.Injected);
        Assert.Equal(
            new[] { DictationState.Listening, DictationState.Transcribing, DictationState.Injecting, DictationState.Idle },
            _states);
    }

    [Fact]
    public async Task Short_tap_is_ignored()
    {
        _settings.Current.MinimumHoldMs = 500;
        using var engine = CreateEngine(_transcriber);

        _hotkey.Press();
        _hotkey.Release();
        await WaitForIdleAsync(engine);

        Assert.Empty(_transcriber.Received);
        Assert.Empty(_injector.Injected);
    }

    [Fact]
    public async Task Press_while_busy_is_ignored()
    {
        _settings.Current.MinimumHoldMs = 0;
        var gate = new TaskCompletionSource();
        using var engine = CreateEngine(new BlockingTranscriber(gate.Task));

        _hotkey.Press();
        _hotkey.Release();
        await Task.Delay(50);
        Assert.Equal(DictationState.Transcribing, engine.State);

        _hotkey.Press();
        Assert.Equal(DictationState.Transcribing, engine.State);
        Assert.Equal(1, _audio.StartCount);

        gate.SetResult();
        await WaitForIdleAsync(engine);
        Assert.Single(_injector.Injected);
    }

    [Fact]
    public async Task Transcriber_failure_raises_Failed_and_returns_to_idle()
    {
        _settings.Current.MinimumHoldMs = 0;
        _transcriber.ExceptionToThrow = new InvalidOperationException("boom");
        using var engine = CreateEngine(_transcriber);
        Exception? failure = null;
        engine.Failed += (_, ex) => failure = ex;

        _hotkey.Press();
        _hotkey.Release();
        await WaitForIdleAsync(engine);

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains(DictationState.Error, _states);
        Assert.Empty(_injector.Injected);
        Assert.False(_audio.IsCapturing);
    }

    [Fact]
    public async Task Empty_transcript_injects_nothing()
    {
        _settings.Current.MinimumHoldMs = 0;
        _transcriber.TextToReturn = "   ";
        using var engine = CreateEngine(_transcriber);

        _hotkey.Press();
        _hotkey.Release();
        await WaitForIdleAsync(engine);

        Assert.Empty(_injector.Injected);
    }

    private DictationEngine CreateEngine(ITranscriber transcriber)
    {
        var engine = new DictationEngine(
            _hotkey,
            _audio,
            transcriber,
            new BasicTextCleaner(_settings),
            _injector,
            _focus,
            _clipboard,
            _settings,
            NullLogger<DictationEngine>.Instance);
        engine.StateChanged += (_, s) =>
        {
            lock (_states)
            {
                _states.Add(s);
            }
        };
        engine.Start();
        return engine;
    }

    private static async Task WaitForIdleAsync(DictationEngine engine)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (engine.State != DictationState.Idle && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(DictationState.Idle, engine.State);
    }

    private sealed class BlockingTranscriber : ITranscriber
    {
        private readonly Task _gate;

        public BlockingTranscriber(Task gate) => _gate = gate;

        public bool IsModelReady => true;

        public Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            await _gate;
            return new TranscriptResult("done", TimeSpan.Zero);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
