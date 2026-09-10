using Microsoft.Extensions.Logging.Abstractions;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Pipeline;
using Talk2Me.Core.Tests.Fakes;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

/// <summary>
/// What happens when the focused window cannot take typed text: the dictation goes to the clipboard
/// rather than into the void.
/// </summary>
public sealed class FocusDeflectionTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInjector _injector = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeFocusProbe _focus = new();
    private readonly FakeClipboard _clipboard = new();

    private readonly List<DictationCompleted> _completed = new();

    public FocusDeflectionTests()
    {
        _settings.Current.MinimumHoldMs = 0;
        _transcriber.TextToReturn = "hello world";
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

    private async Task<DictationCompleted> DictateAsync(DictationEngine engine)
    {
        _hotkey.Press();
        _hotkey.Release();

        for (var i = 0; i < 200; i++)
        {
            lock (_completed)
            {
                if (_completed.Count > 0)
                {
                    return _completed[0];
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("the dictation never completed");
    }

    [Fact]
    public async Task Types_into_an_editable_target()
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Editable, "notepad");
        using var engine = CreateEngine();

        var done = await DictateAsync(engine);

        Assert.Equal(DictationDelivery.Typed, done.Delivery);
        Assert.Equal("Hello world ", Assert.Single(_injector.Injected));
        Assert.Empty(_clipboard.Copied);
    }

    [Fact]
    public async Task Copies_instead_when_the_target_is_not_a_text_field()
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.NotEditable, "explorer");
        using var engine = CreateEngine();

        var done = await DictateAsync(engine);

        Assert.Equal(DictationDelivery.CopiedToClipboard, done.Delivery);
        Assert.Empty(_injector.Injected);
        Assert.Equal("Hello world", Assert.Single(_clipboard.Copied));
    }

    [Fact]
    public async Task Copies_instead_when_the_target_is_elevated()
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Elevated, "regedit");
        using var engine = CreateEngine();

        var done = await DictateAsync(engine);

        Assert.Equal(DictationDelivery.CopiedToClipboard, done.Delivery);
        Assert.Equal("regedit runs as administrator", done.Reason);
        Assert.Empty(_injector.Injected);
    }

    [Fact]
    public async Task The_trailing_space_is_for_typing_only()
    {
        // It exists so consecutive dictations run together in a document. On the clipboard it is just
        // a stray space the user has to delete.
        _settings.Current.AppendTrailingSpace = true;
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.NotEditable);
        using var engine = CreateEngine();

        await DictateAsync(engine);

        Assert.Equal("Hello world", Assert.Single(_clipboard.Copied));
    }

    [Fact]
    public async Task Types_when_the_probe_cannot_tell()
    {
        // Deliberately permissive: refusing to type into a field that would have worked is worse than
        // the problem this check exists to solve.
        _focus.TargetToReturn = FocusTarget.Unknown;
        using var engine = CreateEngine();

        var done = await DictateAsync(engine);

        Assert.Equal(DictationDelivery.Typed, done.Delivery);
        Assert.Single(_injector.Injected);
    }

    [Fact]
    public async Task Types_when_the_probe_is_too_slow_to_answer()
    {
        // A wedged accessibility tree in some other application must not hold up the user's text.
        _focus.Delay = TimeSpan.FromSeconds(5);
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.NotEditable);
        using var engine = CreateEngine();

        var done = await DictateAsync(engine);

        Assert.Equal(DictationDelivery.Typed, done.Delivery);
        Assert.Single(_injector.Injected);
    }

    [Fact]
    public async Task The_probe_runs_once_per_dictation_and_starts_on_the_key_press()
    {
        _focus.TargetToReturn = new FocusTarget(FocusVerdict.Editable);
        using var engine = CreateEngine();

        _hotkey.Press();

        // Probing at key-down is the whole point: it costs nothing because the user is still speaking,
        // and it captures focus as it was when they started.
        for (var i = 0; i < 50 && _focus.Probes == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, _focus.Probes);

        _hotkey.Release();
        await DictateAsync(engine);

        Assert.Equal(1, _focus.Probes);
    }

    [Theory]
    [InlineData(FocusVerdict.Editable, true)]
    [InlineData(FocusVerdict.Unknown, true)]
    [InlineData(FocusVerdict.NotEditable, false)]
    [InlineData(FocusVerdict.Elevated, false)]
    public void Only_a_confident_negative_stops_typing(FocusVerdict verdict, bool canType)
    {
        Assert.Equal(canType, new FocusTarget(verdict).CanType);
    }
}
