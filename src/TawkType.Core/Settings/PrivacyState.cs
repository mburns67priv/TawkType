namespace TawkType.Core.Settings;

/// <summary>Whether anything about the next dictation will leave this machine.</summary>
public enum PrivacyPosture
{
    /// <summary>Nothing leaves. Recognition, cleanup and delivery all happen here.</summary>
    Local,

    /// <summary>The finished transcript will be sent to Anthropic to be rewritten.</summary>
    Cloud,
}

/// <summary>One statement about the current state, and whether it is the one to look at.</summary>
/// <param name="Attention">
/// True only for text leaving the machine. Not for "history is on" — a local log is not a warning,
/// and colouring it like one would spend the user's attention on the wrong line.
/// </param>
public sealed record PrivacyLine(string Text, bool Attention = false);

/// <summary>
/// What is actually true about privacy right now, in the words to show the user.
///
/// Feature research §9 asks for this to be visible during use rather than buried in settings, and
/// warns that the labels are only worth having if they are true. So this mirrors the pipeline's own
/// three conditions exactly — <c>Cleanup.UseLlm</c>, the active mode's <c>MayUseLlm</c>, and whether a
/// key is configured — rather than reading the one setting a user might think is in charge. Switching
/// the rewrite on with no key stored sends nothing, and the bar has to say so, or the first time
/// somebody checks they will find the indicator lying.
/// </summary>
public sealed record PrivacyState(
    PrivacyPosture Posture,
    string Badge,
    string Summary,
    IReadOnlyList<PrivacyLine> Lines,
    string WhatLeaves)
{
    public bool IsCloud => Posture == PrivacyPosture.Cloud;

    /// <summary>
    /// Reads the state from the settings and whether the LLM client has a key. <paramref name="hasApiKey"/>
    /// is <c>ILlmClient.IsConfigured</c>, which is the same value the cleaner checks — pass anything
    /// else and this can disagree with what the pipeline does.
    /// </summary>
    public static PrivacyState From(TawkTypeSettings settings, bool hasApiKey)
    {
        var mode = settings.ActiveModeOrDefault();
        var rewriteRuns = settings.Cleanup.UseLlm && mode.MayUseLlm && hasApiKey;

        var lines = new List<PrivacyLine>
        {
            new("Speech recognition runs on this machine."),
            new(DescribeRewrite(settings, mode, hasApiKey, rewriteRuns), rewriteRuns),
            new(settings.History.Enabled
                ? "History is on: what you dictate is kept on this machine, in plain text."
                : "History is off: nothing you dictate is written to disk."),
        };

        return new PrivacyState(
            rewriteRuns ? PrivacyPosture.Cloud : PrivacyPosture.Local,
            rewriteRuns ? "Cloud" : "Local",
            rewriteRuns
                ? "Transcripts are sent to Anthropic to be rewritten."
                : "Nothing leaves this machine.",
            lines,
            rewriteRuns ? CloudDetail : LocalDetail);
    }

    /// <summary>
    /// Exactly what is in the request, because §9 asks for exactly. Audio never leaves — the model is
    /// given text — and neither does anything TawkType reads about the window it is typing into: the
    /// focus probe and the caret context are used locally and never sent. If that ever stops being
    /// true, this sentence is the thing that has to change first.
    /// </summary>
    private const string CloudDetail =
        "What is sent: the transcript of what you said, your vocabulary list, and any custom "
        + "instructions you have written. What is not: the audio, the contents of your screen, the "
        + "text around your cursor, and your history.";

    private const string LocalDetail =
        "Nothing is sent anywhere. No audio, no text, no account, no telemetry. The only feature that "
        + "would send anything is the Claude rewrite, and it is not running.";

    private static string DescribeRewrite(
        TawkTypeSettings settings,
        DictationMode mode,
        bool hasApiKey,
        bool rewriteRuns)
    {
        if (rewriteRuns)
        {
            return "Claude rewrite is on: the finished transcript is sent to Anthropic.";
        }

        if (!settings.Cleanup.UseLlm)
        {
            return "Claude rewrite is off.";
        }

        // Switched on, but something else stops it. Saying "on" here would be true of the setting and
        // false of the machine, and the machine is what the user is asking about.
        if (!hasApiKey)
        {
            return "Claude rewrite is switched on, but no API key is stored, so nothing is sent.";
        }

        return $"Claude rewrite is switched on, but the {mode.Name} mode keeps dictation on this machine.";
    }
}
