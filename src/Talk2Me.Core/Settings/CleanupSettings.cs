namespace Talk2Me.Core.Settings;

public enum CleanupStyle
{
    /// <summary>Fix disfluencies and punctuation, change nothing else. Closest to what was said.</summary>
    Verbatim,

    /// <summary>Default. Reads like the user wrote it deliberately: tidy sentences, kept voice.</summary>
    Natural,

    /// <summary>Full sentences, no contractions, no slang. For documents and mail.</summary>
    Formal,

    /// <summary>Relaxed and conversational. For chat and comments.</summary>
    Casual,
}

/// <summary>
/// The LLM rewrite pass that runs after the regex cleaner. Off by default: it is the only part of
/// Talk2Me that leaves the machine, so it must be turned on deliberately.
/// </summary>
public sealed class CleanupSettings
{
    public bool UseLlm { get; set; }

    /// <summary>Anthropic model id. Cheaper/faster models trade rewrite quality for latency.</summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>Give up after this long and type the regex-cleaned text instead.</summary>
    public int TimeoutMs { get; set; } = 2000;

    public CleanupStyle Style { get; set; } = CleanupStyle.Natural;

    /// <summary>Names, jargon and acronyms the recogniser gets wrong. Spelled the way they should be typed.</summary>
    public string[] Vocabulary { get; set; } = [];

    /// <summary>Free-text rules appended to the prompt, e.g. "British spelling", "never use em dashes".</summary>
    public string CustomInstructions { get; set; } = string.Empty;

    public CleanupSettings Clone() => new()
    {
        UseLlm = UseLlm,
        Model = Model,
        TimeoutMs = TimeoutMs,
        Style = Style,
        Vocabulary = (string[])Vocabulary.Clone(),
        CustomInstructions = CustomInstructions,
    };
}
