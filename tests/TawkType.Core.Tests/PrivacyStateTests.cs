using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// What the bar and the privacy panel say about where the next dictation goes. The whole value of
/// these labels is that they are true, so most of these are about the cases where the setting and
/// the machine disagree.
/// </summary>
public class PrivacyStateTests
{
    private static TawkTypeSettings Settings(bool useLlm = false, bool history = true, string? mode = null)
    {
        var settings = new TawkTypeSettings();
        settings.Cleanup.UseLlm = useLlm;
        settings.History.Enabled = history;

        if (mode is not null)
        {
            settings.ActiveMode = mode;
        }

        return settings;
    }

    [Fact]
    public void A_default_install_says_nothing_leaves()
    {
        var state = PrivacyState.From(Settings(), hasApiKey: false);

        Assert.Equal(PrivacyPosture.Local, state.Posture);
        Assert.Equal("Local", state.Badge);
        Assert.False(state.IsCloud);
        Assert.Contains("Nothing leaves", state.Summary);
    }

    [Fact]
    public void Recognition_is_always_described_as_local()
        => Assert.All(
            new[] { PrivacyState.From(Settings(), false), PrivacyState.From(Settings(useLlm: true), true) },
            state => Assert.Contains(state.Lines, line => line.Text.Contains("runs on this machine")));

    [Fact]
    public void The_rewrite_running_is_the_one_line_that_asks_for_attention()
    {
        var state = PrivacyState.From(Settings(useLlm: true), hasApiKey: true);

        Assert.Equal(PrivacyPosture.Cloud, state.Posture);
        Assert.Equal("Cloud", state.Badge);
        Assert.Single(state.Lines, line => line.Attention);
        Assert.Contains(state.Lines, line => line.Attention && line.Text.Contains("Anthropic"));
    }

    /// <summary>
    /// The setting is on and nothing is sent, because the cleaner checks for a key too. Reporting
    /// "Cloud" here would be reading the checkbox rather than the machine.
    /// </summary>
    [Fact]
    public void Switched_on_with_no_key_is_still_local_and_says_why()
    {
        var state = PrivacyState.From(Settings(useLlm: true), hasApiKey: false);

        Assert.Equal(PrivacyPosture.Local, state.Posture);
        Assert.Contains(state.Lines, line => line.Text.Contains("no API key is stored"));
        Assert.DoesNotContain(state.Lines, line => line.Attention);
    }

    /// <summary>
    /// A mode can only ever narrow the permission to use Claude. When one has, the dictation about to
    /// happen is local whatever the AI cleanup page says.
    /// </summary>
    [Fact]
    public void A_mode_that_forbids_the_rewrite_makes_the_state_local()
    {
        var settings = Settings(useLlm: true, mode: "Literal");
        var literal = settings.ActiveModeOrDefault();
        Assert.False(literal.MayUseLlm); // the premise of this test, not an assumption about it

        var state = PrivacyState.From(settings, hasApiKey: true);

        Assert.Equal(PrivacyPosture.Local, state.Posture);
        Assert.Contains(state.Lines, line => line.Text.Contains(literal.Name));
    }

    [Fact]
    public void History_on_and_off_both_say_what_that_means()
    {
        Assert.Contains(
            PrivacyState.From(Settings(history: true), false).Lines,
            line => line.Text.Contains("plain text"));

        Assert.Contains(
            PrivacyState.From(Settings(history: false), false).Lines,
            line => line.Text.Contains("nothing you dictate is written to disk"));
    }

    /// <summary>
    /// §9 asks for exactly what leaves, and the answer has to match what `CleanupPrompt` builds. If
    /// the request ever gains the caret context or the screen, this test should fail first.
    /// </summary>
    [Fact]
    public void The_cloud_explanation_names_what_is_sent_and_what_is_not()
    {
        var detail = PrivacyState.From(Settings(useLlm: true), hasApiKey: true).WhatLeaves;

        Assert.Contains("transcript", detail);
        Assert.Contains("vocabulary", detail);
        Assert.Contains("custom instructions", detail);
        Assert.Contains("not", detail);
        Assert.Contains("audio", detail);
    }

    [Fact]
    public void The_local_explanation_does_not_promise_more_than_it_can()
    {
        var detail = PrivacyState.From(Settings(), hasApiKey: false).WhatLeaves;

        Assert.Contains("Nothing is sent", detail);
        Assert.Contains("Claude rewrite", detail); // names the one thing that would change it
    }

    [Fact]
    public void Three_lines_always_and_history_is_never_an_alarm()
    {
        foreach (var useLlm in new[] { true, false })
        {
            foreach (var history in new[] { true, false })
            {
                var state = PrivacyState.From(Settings(useLlm, history), hasApiKey: useLlm);

                Assert.Equal(3, state.Lines.Count);
                Assert.DoesNotContain(state.Lines, line => line.Attention && line.Text.Contains("History"));
            }
        }
    }
}
