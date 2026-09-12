namespace TawkType.Core.Settings;

/// <summary>
/// What came out of a block of vocabulary text, and which of its lines were not entries.
///
/// The forgiving parse drops an unusable line and says nothing, which is fine for a box the user is
/// looking at and typing into. It stops being fine the moment that text is a view onto a list held
/// somewhere else: a line silently dropped on the way back is a rule the user wrote, saved, and never
/// sees again.
/// </summary>
/// <param name="Entries">The lines that were entries, in the order they were written.</param>
/// <param name="RejectedLines">1-based line numbers that held something, but not an entry.</param>
public readonly record struct VocabularyParse<T>(T[] Entries, int[] RejectedLines)
{
    public bool Ok => RejectedLines.Length == 0;

    /// <summary>The first thing wrong, for a message that names one line rather than counting them.</summary>
    public int FirstRejected => RejectedLines.Length == 0 ? 0 : RejectedLines[0];
}
