using TawkType.Core.Settings;

namespace TawkType.Core.Text;

/// <summary>
/// Decides whether an edit the user just made to a past dictation is worth offering as a replacement.
///
/// Editing a dictation and teaching the vocabulary are two separate acts, and the second is a button
/// you have to know is there. But the moment an edit is saved is the moment the evidence exists: what
/// the recogniser heard, and what the user says it should have been. This works out whether that pair
/// is a rule worth proposing, so the history window can offer it instead of waiting to be asked.
///
/// It only ever proposes. Nothing here writes anything, and the user still sees both halves in the
/// Remember dialog before a replacement starts applying to everything they say.
/// </summary>
public static class CorrectionOffer
{
    /// <summary>
    /// The longest phrase worth proposing, on either side.
    ///
    /// <see cref="CorrectionGuess"/> falls back to the whole text when it cannot narrow the change to
    /// a span — a rewrite with no shared edges, or a pure insertion. A replacement rule built from a
    /// whole sentence only ever fires on that exact sentence again, so a long pair is not a rule, it
    /// is noise with a dialog attached.
    /// </summary>
    public const int MaxWords = 6;

    /// <summary>
    /// The replacement worth offering for this edit, or null when there is nothing useful to propose.
    /// </summary>
    /// <param name="heard">What the recogniser produced, before any cleanup.</param>
    /// <param name="typed">What the user saved in its place.</param>
    /// <param name="existing">The replacements already taught, so a rule is not offered twice.</param>
    public static Correction? For(string? heard, string? typed, IEnumerable<TextReplacement>? existing = null)
    {
        if (string.IsNullOrWhiteSpace(heard) || string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var guess = CorrectionGuess.Between(heard, typed);
        var from = guess.Heard.Trim();
        var to = guess.Typed.Trim();

        if (from.Length == 0 || to.Length == 0)
        {
            return null;
        }

        // A change that is only punctuation or only the surrounding edges is cleanup, not a correction
        // anybody wants a standing rule for.
        if (string.Equals(Bare(from), Bare(to), StringComparison.CurrentCulture))
        {
            return null;
        }

        if (WordCount(from) > MaxWords || WordCount(to) > MaxWords)
        {
            return null;
        }

        // Already taught. The rule exists, so either it did not match this dictation or the user has
        // since changed their mind — offering the same thing again helps with neither.
        if (existing is not null
            && existing.Any(entry => string.Equals(entry.From.Trim(), from, StringComparison.CurrentCultureIgnoreCase)))
        {
            return null;
        }

        return new Correction(from, to);
    }

    private static int WordCount(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static string Bare(string text) => text.Trim('.', ',', '!', '?', ';', ':', '"', '\'');
}
