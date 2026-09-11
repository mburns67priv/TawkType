using Microsoft.Extensions.Logging.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;
using TawkType.Core.Tests.Fakes;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

/// <summary>
/// Escape abandons a dictation, and the engine tells the hotkey layer when there is one to abandon —
/// without which Escape would have to be taken from the focused application at all times.
/// </summary>
public sealed class CancelAndToggleTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();

    [Fact]
    public void Escape_belongs_to_the_focused_application_until_a_dictation_starts()
    {
        using var engine = CreateEngine();

        Assert.False(_hotkey.DictationInProgress);

        _hotkey.Press();

        Assert.True(_hotkey.DictationInProgress);
    }

    [Fact]
    public async Task Escape_while_listening_abandons_the_dictation()
    {
        using var engine = CreateEngine();
        _hotkey.Press();
        await Task.Delay(_settings.Current.MinimumHoldMs + 50);

        _hotkey.RequestCancel();

        Assert.Equal(DictationState.Idle, engine.State);
        Assert.False(_audio.IsCapturing);
        Assert.False(_hotkey.DictationInProgress);

        // The key coming up afterwards must not start anything or deliver the abandoned recording.
        _hotkey.Release();
        await Task.Delay(100);

        Assert.Empty(_injector.Injected);
    }

    [Fact]
    public async Task Escape_while_transcribing_abandons_the_dictation()
    {
        _transcriber.UseGate = true;

        using var engine = CreateEngine();
        _hotkey.Press();
        await Task.Delay(_settings.Current.MinimumHoldMs + 50);
        _hotkey.Release();

        for (var i = 0; i < 50 && _transcriber.Received.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        _hotkey.RequestCancel();
        _transcriber.Gate.SetResult();
        await Task.Delay(200);

        Assert.Empty(_injector.Injected);
        Assert.Empty(_clipboard.Copied);
        Assert.Equal(DictationState.Idle, engine.State);
    }

    [Fact]
    public async Task Escape_with_nothing_running_does_nothing()
    {
        using var engine = CreateEngine();

        _hotkey.RequestCancel();
        await Task.Delay(50);

        Assert.Equal(DictationState.Idle, engine.State);
    }

    /// <summary>
    /// A toggled dictation reaches the engine as the same press and release a held one does, so the
    /// state machine has no idea there are two ways in. This is that contract, from the engine's side.
    /// </summary>
    [Fact]
    public async Task Separate_press_and_release_events_dictate_normally()
    {
        _transcriber.TextToReturn = "toggled";

        using var engine = CreateEngine();
        _hotkey.Press();
        await Task.Delay(_settings.Current.MinimumHoldMs + 50);
        _hotkey.Release();

        for (var i = 0; i < 100 && engine.State != DictationState.Idle; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(["Toggled "], _injector.Injected);
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
}
