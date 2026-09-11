using Microsoft.Extensions.Logging.Abstractions;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Pipeline;
using Talk2Me.Core.Tests.Fakes;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

/// <summary>
/// The whole pipeline, checking that what reaches the injector is fitted to where it is landing —
/// <see cref="CaretFitTests"/> covers the rules themselves; these cover them being applied at all, to
/// the typed text only, and never at the expense of delivering something.
/// </summary>
public sealed class CaretAwareDeliveryTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly List<DictationCompleted> _completed = new();

    public CaretAwareDeliveryTests()
    {
        _settings.Current.MinimumHoldMs = 0;
        _settings.Current.ActiveModeOrDefault().AppendTrailingSpace = true;
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Editable, "notepad", "edit");
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

        engine.Completed += (_, done) =>
        {
            lock (_completed)
            {
                _completed.Add(done);
            }
        };

        engine.Start();
        return engine;
    }

    private async Task DictateAsync()
    {
        using var engine = CreateEngine();
        _hotkey.Press();
        _hotkey.Release();

        for (var i = 0; i < 200; i++)
        {
            lock (_completed)
            {
                if (_completed.Count > 0)
                {
                    return;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("the dictation never completed");
    }

    private string Typed => Assert.Single(_injector.Injected);

    [Fact]
    public async Task A_continuation_gets_its_separating_space()
    {
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("we arrived", string.Empty);

        await DictateAsync();

        Assert.StartsWith(" ", Typed);
    }

    [Fact]
    public async Task No_space_is_doubled()
    {
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("we arrived ", string.Empty);

        await DictateAsync();

        Assert.False(Typed.StartsWith("  ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The cleaner capitalises the first letter of every dictation. Mid-sentence that is wrong, and
    /// undoing a capital Talk2Me itself added is the only safe way to fix it.
    /// </summary>
    [Fact]
    public async Task A_continuation_is_not_capitalised()
    {
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("we arrived", string.Empty);

        await DictateAsync();

        Assert.Equal(" and then we left ", Typed);
    }

    [Fact]
    public async Task A_new_sentence_keeps_its_capital()
    {
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("We arrived.", string.Empty);

        await DictateAsync();

        Assert.Equal(" And then we left ", Typed);
    }

    /// <summary>
    /// Spacing and capitals belong to the destination, not to the words. The history and a fallback
    /// copy must record what was said, not how it was punched into one particular text box.
    /// </summary>
    [Fact]
    public async Task The_recorded_result_is_the_words_not_their_spacing()
    {
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("we arrived", string.Empty);

        await DictateAsync();

        Assert.Equal("And then we left", _completed[0].CleanText);
    }

    [Fact]
    public async Task An_unreadable_caret_delivers_exactly_as_before()
    {
        _transcriber.TextToReturn = "hello world";
        _focus.Caret = CaretContext.Unknown;

        await DictateAsync();

        Assert.Equal("Hello world ", Typed);
    }

    /// <summary>
    /// Finished text must never wait on somebody else's message loop. A destination that cannot
    /// answer in time is treated as one that cannot answer at all.
    /// </summary>
    [Fact]
    public async Task A_slow_destination_does_not_hold_up_the_text()
    {
        _transcriber.TextToReturn = "hello world";
        _focus.Caret = new CaretContext("we arrived", string.Empty);
        _focus.CaretDelay = TimeSpan.FromSeconds(3);

        await DictateAsync();

        Assert.Equal("Hello world ", Typed);
    }

    [Fact]
    public async Task The_setting_turns_it_off()
    {
        _settings.Current.ActiveModeOrDefault().FitToCaret = false;
        _transcriber.TextToReturn = "and then we left";
        _focus.Caret = new CaretContext("we arrived", string.Empty);

        await DictateAsync();

        Assert.Equal("And then we left ", Typed);
    }
}
