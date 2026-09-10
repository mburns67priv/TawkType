namespace Murmur.Core.Models;

public sealed record DictationCompleted(
    string RawText,
    string CleanText,
    TimeSpan AudioDuration,
    TimeSpan TranscriptionTime);
