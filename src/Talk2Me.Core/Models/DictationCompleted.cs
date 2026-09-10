namespace Talk2Me.Core.Models;

public sealed record DictationCompleted(
    string RawText,
    string CleanText,
    TimeSpan AudioDuration,
    TimeSpan TranscriptionTime);
