using TawkType.Core.Abstractions;

namespace TawkType.Core.Models;

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

    /// <summary>
    /// Where the text was aimed. Carried so recovery can offer to send it there after the fact, and
    /// can say which application it means. Not persisted — a window handle is meaningless next week.
    /// </summary>
    public FocusTarget? Target { get; init; }
}
