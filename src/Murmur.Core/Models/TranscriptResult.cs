namespace Murmur.Core.Models;

public sealed record TranscriptResult(string Text, TimeSpan ProcessingTime, string? Language = null)
{
    public static TranscriptResult Empty { get; } = new(string.Empty, TimeSpan.Zero);
}
