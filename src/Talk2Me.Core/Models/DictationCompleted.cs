namespace Talk2Me.Core.Models;

public sealed record DictationCompleted(
    string RawText,
    string CleanText,
    TimeSpan AudioDuration,
    TimeSpan TranscriptionTime)
{
    /// <summary>Where the text ended up. Typed unless the focused window could not take it.</summary>
    public DictationDelivery Delivery { get; init; } = DictationDelivery.Typed;

    /// <summary>Why it was not typed, when it was not. Empty otherwise.</summary>
    public string Reason { get; init; } = string.Empty;
}
