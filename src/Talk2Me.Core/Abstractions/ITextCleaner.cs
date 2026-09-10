namespace Talk2Me.Core.Abstractions;

/// <summary>
/// Post-processes a raw transcript into the text that gets typed: filler removal, punctuation,
/// casing, and later an LLM rewrite step (tone per app, list formatting, spoken corrections).
/// </summary>
public interface ITextCleaner
{
    ValueTask<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
