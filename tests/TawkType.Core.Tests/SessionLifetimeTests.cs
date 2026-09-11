using Microsoft.Extensions.Logging.Abstractions;
using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;
using TawkType.Core.Tests.Fakes;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

/// <summary>
/// Stopping used to mean "unsubscribe from the hotkey and return", while the dictation in flight
/// carried on transcribing, rewriting and typing — through a shutdown that was busy disposing the
/// services it was still using. And the focus verdict was made at key-down and trusted at delivery,
/// so moving window during transcription typed the result into wherever you had gone.
/// </summary>
public sealed class SessionLifetimeTests
{
    private const long TheWindowTheyStartedIn = 111;
    private const long SomewhereElse = 222;

    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();

    [Fact]
    public async Task Moving_to_another_window_copies_instead_of_typing_into_it()
    {
        InThisWindow(TheWindowTheyStartedIn);
        _transcriber.TextToReturn = "meant for the first window";

        using var engine = CreateEngine();
        await SpeakAsync(engine, whileTranscribing: () => _focus.Window = SomewhereElse);

        Assert.Empty(_injector.Injected);
        Assert.Equal(["Meant for the first window"], _clipboard.Copied);
    }

    [Fact]
    public async Task Staying_put_types_as_usual()
    {
        InThisWindow(TheWindowTheyStartedIn);
        _transcriber.TextToReturn = "still here";

        using var engine = CreateEngine();
        await SpeakAsync(engine);

        // With the trailing space, which is the typing path's and not the clipboard's.
        Assert.Equal(["Still here "], _injector.Injected);
        Assert.Empty(_clipboard.Copied);
    }

    /// <summary>
    /// Refusing to type is its own failure, so an unknown window is treated as unchanged rather than
    /// diverting every dictation to the clipboard on platforms that cannot answer.
    /// </summary>
    [Fact]
    public async Task An_unknown_window_is_treated_as_unchanged()
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Editable, "notepad", "edit");
        _focus.Window = null;
        _transcriber.TextToReturn = "no identity available";

        using var engine = CreateEngine();
        await SpeakAsync(engine);

        Assert.Single(_injector.Injected);
    }

    [Fact]
    public async Task Cancelling_a_dictation_delivers_nothing_and_returns_to_idle()
    {
        InThisWindow(TheWindowTheyStartedIn);
        _transcriber.UseGate = true;

        using var engine = CreateEngine();
        await StartProcessingAsync(engine);

        engine.CancelDictation();
        _transcriber.Gate.SetResult();
        await SettleAsync(engine);

        Assert.Empty(_injector.Injected);
        Assert.Empty(_clipboard.Copied);
        Assert.Equal(DictationState.Idle, engine.State);
    }

    [Fact]
    public async Task Cancelling_does_not_report_a_failure()
    {
        _transcriber.UseGate = true;
        var failures = 0;

        using var engine = CreateEngine();
        engine.Failed += (_, _) => failures++;
        await StartProcessingAsync(engine);

        engine.CancelDictation();
        _transcriber.Gate.SetResult();
        await SettleAsync(engine);

        Assert.Equal(0, failures);
    }

    /// <summary>
    /// The reason Stop waits: the caller disposes the transcriber and the injector next, and work that
    /// is still running would be using them.
    /// </summary>
    [Fact]
    public async Task Stopping_ends_the_dictation_in_flight_before_it_returns()
    {
        _transcriber.UseGate = true;

        var engine = CreateEngine();
        await StartProcessingAsync(engine);

        engine.Stop();

        // Releasing the gate now proves the difference: work that was merely not-yet-finished would
        // carry on from here and type into whatever is in front, long after the app said it stopped.
        _transcriber.Gate.SetResult();
        await Task.Delay(200);

        Assert.Empty(_injector.Injected);
        Assert.Empty(_clipboard.Copied);
        Assert.False(_audio.IsCapturing);
    }

    [Fact]
    public async Task A_press_after_stopping_starts_nothing()
    {
        var engine = CreateEngine();
        engine.Stop();

        _hotkey.Press();
        await Task.Delay(50);

        Assert.False(_audio.IsCapturing);
        Assert.Equal(DictationState.Idle, engine.State);
    }

    private void InThisWindow(long window)
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Editable, "notepad", "edit", window);
        _focus.Window = window;
    }

    private DictationEngine CreateEngine()
    {
        var engine = new DictationEngine(
            _hotkey,
            _audio,
            _transcriber,
            new BasicTextCleaner(_settings),
            _injector,
            _focus,
            _clipboard,
            _settings,
            NullLogger<DictationEngine>.Instance);
        engine.Start();
        return engine;
    }

    /// <summary>Holds the key long enough to count, then releases it.</summary>
    private async Task StartProcessingAsync(DictationEngine engine)
    {
        _hotkey.Press();
        await Task.Delay(_settings.Current.MinimumHoldMs + 50);
        _hotkey.Release();

        for (var i = 0; i < 50 && _transcriber.Received.Count == 0; i++)
        {
            await Task.Delay(10);
        }
    }

    private async Task SpeakAsync(DictationEngine engine, Action? whileTranscribing = null)
    {
        if (whileTranscribing is not null)
        {
            _transcriber.UseGate = true;
        }

        await StartProcessingAsync(engine);

        if (whileTranscribing is not null)
        {
            whileTranscribing();
            _transcriber.Gate.SetResult();
        }

        await SettleAsync(engine);
    }

    private static async Task SettleAsync(DictationEngine engine)
    {
        for (var i = 0; i < 100 && engine.State != DictationState.Idle; i++)
        {
            await Task.Delay(20);
        }
    }
}
