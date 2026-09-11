namespace Talk2Me.Core.Models;

/// <summary>
/// One completed dictation, as written to the history log: what was heard, what was typed, and the
/// numbers behind it. Persisted as a single line of JSON, so adding a property is backwards compatible.
/// </summary>
public sealed record DictationRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    /// <summary>Straight off the recogniser, before any cleanup.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>What was actually typed into the focused window.</summary>
    public string FinalText { get; init; } = string.Empty;

    /// <summary>"Parakeet" or "Whisper".</summary>
    public string Engine { get; init; } = string.Empty;

    public double AudioSeconds { get; init; }

    public int TranscriptionMs { get; init; }

    /// <summary>
    /// What became of the text. Worth keeping: it is the difference between "the app lost my
    /// dictation" and "it is right here". Records written before this existed read as Typed.
    /// </summary>
    public DictationDelivery Delivery { get; init; } = DictationDelivery.Typed;

    /// <summary>True when cleanup changed the text beyond trimming it.</summary>
    public bool WasCleaned => !string.Equals(RawText.Trim(), FinalText.Trim(), StringComparison.Ordinal);
}
