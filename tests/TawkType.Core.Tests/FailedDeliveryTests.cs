using Microsoft.Extensions.Logging.Abstractions;
using TawkType.Core.Abstractions;
using TawkType.Core.History;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;
using TawkType.Core.Tests.Fakes;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

/// <summary>
/// Delivery is the step most likely to fail, and it used to be the step after which the result was
/// announced — so a failed injection meant the words were simply gone, precisely when the user needed
/// to get them back.
/// </summary>
public sealed class FailedDeliveryTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();

    [Fact]
    public async Task The_words_are_announced_before_delivery_is_attempted()
    {
        _transcriber.TextToReturn = "hello world";
        DictationCompleted? recognised = null;

        using var engine = CreateEngine();
        engine.Recognised += (_, r) => recognised = r;

        await DictateAsync(engine);

        Assert.NotNull(recognised);
        Assert.Equal("Hello world", recognised!.CleanText);
        Assert.Equal(DictationDelivery.Pending, recognised.Delivery);
    }

    [Fact]
    public async Task A_failed_injection_still_completes_with_the_text()
    {
        _transcriber.TextToReturn = "the words that did not arrive";
        _injector.ExceptionToThrow = new InvalidOperationException("SendInput refused");
        DictationCompleted? completed = null;

        using var engine = CreateEngine();
        engine.Completed += (_, c) => completed = c;

        await DictateAsync(engine);

        Assert.NotNull(completed);
        Assert.Equal(DictationDelivery.Failed, completed!.Delivery);
        Assert.Equal("The words that did not arrive", completed.CleanText);
        Assert.NotEmpty(completed.Reason);
    }

    [Fact]
    public async Task A_failed_injection_leaves_the_text_on_the_clipboard()
    {
        _transcriber.TextToReturn = "recoverable";
        _injector.ExceptionToThrow = new InvalidOperationException("SendInput refused");

        using var engine = CreateEngine();
        await DictateAsync(engine);

        Assert.Equal(["Recoverable"], _clipboard.Copied);
    }

    /// <summary>The trailing space is a typing convenience; it has no business on the clipboard.</summary>
    [Fact]
    public async Task The_recovered_text_does_not_carry_the_trailing_space()
    {
        _settings.Current.AppendTrailingSpace = true;
        _transcriber.TextToReturn = "no trailing space here";
        _injector.ExceptionToThrow = new InvalidOperationException("SendInput refused");

        using var engine = CreateEngine();
        await DictateAsync(engine);

        Assert.Equal(["No trailing space here"], _clipboard.Copied);
    }

    [Fact]
    public async Task A_failed_injection_does_not_report_the_dictation_as_an_error()
    {
        _transcriber.TextToReturn = "still fine";
        _injector.ExceptionToThrow = new InvalidOperationException("SendInput refused");
        var failures = 0;

        using var engine = CreateEngine();
        engine.Failed += (_, _) => failures++;

        await DictateAsync(engine);

        Assert.Equal(0, failures);
        Assert.Equal(DictationState.Idle, engine.State);
    }

    /// <summary>
    /// The buffer exists so that switching history off does not also switch off the Copy button. It
    /// holds the words in memory only, and is filled before delivery.
    /// </summary>
    [Fact]
    public void The_session_buffer_holds_the_last_result_with_history_switched_off()
    {
        var settings = new FakeSettings();
        settings.Current.History.Enabled = false;

        var history = new DictationHistoryStore(
            settings,
            NullLogger<DictationHistoryStore>.Instance,
            Path.Combine(Path.GetTempPath(), "TawkType.Tests." + Guid.NewGuid().ToString("N"), "history.jsonl"));

        var last = new LastDictation(history);
        Assert.Null(last.Value);

        last.Set(new DictationRecord { FinalText = "said out loud" });

        Assert.Equal("said out loud", last.Value?.FinalText);
        Assert.Empty(history.Recent);
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

    private async Task DictateAsync(DictationEngine engine)
    {
        _hotkey.Press();
        await Task.Delay(_settings.Current.MinimumHoldMs + 50);
        _hotkey.Release();

        for (var i = 0; i < 100 && engine.State != DictationState.Idle; i++)
        {
            await Task.Delay(20);
        }
    }
}
