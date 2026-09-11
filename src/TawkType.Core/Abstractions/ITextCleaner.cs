namespace TawkType.Core.Abstractions;

/// <summary>
/// Post-processes a raw transcript into the text that gets typed: filler removal, punctuation,
/// casing, and later an LLM rewrite step (tone per app, list formatting, spoken corrections).
/// </summary>
public interface ITextCleaner
{
    /// <summary>
    /// True when <see cref="CleanAsync"/> is slow enough that the user should see it happening.
    /// Purely a UI hint; the pipeline works the same either way.
    /// </summary>
    bool MayTakeAWhile => false;

    ValueTask<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
