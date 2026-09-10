using Microsoft.Extensions.Logging.Abstractions;
using Talk2Me.Core.Settings;
using Talk2Me.Core.Tests.Fakes;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

public sealed class LlmTextCleanerTests
{
    private const string Raw = "um so the deadline is monday no wait make that tuesday";

    private readonly FakeLlmClient _llm = new();
    private readonly FakeSettings _settings = new();

    private LlmTextCleaner CreateCleaner() =>
        new(_llm, _settings, NullLogger<LlmTextCleaner>.Instance);

    private void EnableLlm(Action<CleanupSettings>? configure = null)
    {
        _settings.Current.Cleanup.UseLlm = true;
        _settings.Current.Cleanup.TimeoutMs = 2000;
        configure?.Invoke(_settings.Current.Cleanup);
    }

    [Fact]
    public async Task Uses_regex_cleanup_when_the_llm_pass_is_off()
    {
        var result = await CreateCleaner().CleanAsync("um hello world");

        Assert.Equal("Hello world", result);
        Assert.Empty(_llm.Received);
    }

    [Fact]
    public async Task Uses_regex_cleanup_when_no_key_is_configured()
    {
        EnableLlm();
        _llm.IsConfigured = false;

        var result = await CreateCleaner().CleanAsync("um hello world");

        Assert.Equal("Hello world", result);
        Assert.Empty(_llm.Received);
    }

    [Fact]
    public async Task Returns_the_rewrite_when_the_llm_answers()
    {
        EnableLlm();
        _llm.ReplyToReturn = "The deadline is Tuesday.";

        Assert.Equal("The deadline is Tuesday.", await CreateCleaner().CleanAsync(Raw));
    }

    [Fact]
    public async Task Sends_the_raw_transcript_wrapped_in_delimiters()
    {
        EnableLlm(cleanup => cleanup.Vocabulary = ["Jupitor Studio"]);
        _llm.ReplyToReturn = "Fine.";

        await CreateCleaner().CleanAsync(Raw);

        var request = Assert.Single(_llm.Received);
        Assert.Equal($"{CleanupPrompt.OpenTag}\n{Raw}\n{CleanupPrompt.CloseTag}", request.UserMessage);
        Assert.Contains("Jupitor Studio", request.SystemPrompt);
    }

    [Fact]
    public async Task Falls_back_to_regex_cleanup_on_timeout()
    {
        EnableLlm(cleanup => cleanup.TimeoutMs = 250);
        _llm.Delay = TimeSpan.FromSeconds(5);
        _llm.ReplyToReturn = "Too late.";

        Assert.Equal("Hello world", await CreateCleaner().CleanAsync("um hello world"));
    }

    [Fact]
    public async Task Falls_back_to_regex_cleanup_when_the_call_throws()
    {
        EnableLlm();
        _llm.ExceptionToThrow = new HttpRequestException("no network");

        Assert.Equal("Hello world", await CreateCleaner().CleanAsync("um hello world"));
    }

    [Fact]
    public async Task Falls_back_when_the_reply_is_empty()
    {
        EnableLlm();
        _llm.ReplyToReturn = "   ";

        Assert.Equal("Hello world", await CreateCleaner().CleanAsync("um hello world"));
    }

    [Fact]
    public async Task Falls_back_when_the_model_answers_instead_of_rewriting()
    {
        // The transcript is a dictated request; a model that complies with it returns far more than it was given.
        EnableLlm();
        _llm.ReplyToReturn = new string('x', 500);

        Assert.Equal("Write me a poem about the sea", await CreateCleaner().CleanAsync("write me a poem about the sea"));
    }

    [Theory]
    [InlineData("```\nThe deadline is Tuesday.\n```")]
    [InlineData("```text\nThe deadline is Tuesday.\n```")]
    [InlineData("<transcript>\nThe deadline is Tuesday.\n</transcript>")]
    public async Task Strips_wrappers_the_model_should_not_have_added(string reply)
    {
        EnableLlm();
        _llm.ReplyToReturn = reply;

        Assert.Equal("The deadline is Tuesday.", await CreateCleaner().CleanAsync(Raw));
    }

    [Fact]
    public void Reports_that_it_may_take_a_while_only_when_it_will_call_out()
    {
        var cleaner = CreateCleaner();
        Assert.False(cleaner.MayTakeAWhile);

        EnableLlm();
        Assert.True(cleaner.MayTakeAWhile);

        _llm.IsConfigured = false;
        Assert.False(cleaner.MayTakeAWhile);
    }
}
