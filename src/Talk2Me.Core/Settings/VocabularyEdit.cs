namespace Talk2Me.Core.Settings;

/// <summary>The outcome of trying to teach Talk2Me a correction.</summary>
/// <param name="Replacements">The new list, or the old one unchanged when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">A sentence to show the user, or null when the correction was taken.</param>
/// <param name="Replaced">True when an existing rule for the same phrase was overwritten.</param>
public readonly record struct VocabularyLearned(
    TextReplacement[] Replacements,
    string? Problem,
    bool Replaced)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Adding a replacement to the vocabulary from a correction the user has just made.
///
/// Pure, and here rather than in the window, because the rules are the interesting part and every one
/// of them is a way the feature could quietly do damage: a rule matching everything, a rule that
/// replaces a phrase with itself and loops, or a second rule for a phrase that already has one, where
/// the user would have no way to tell which of the two was winning.
/// </summary>
public static class VocabularyEdit
{
    /// <summary>
    /// Long enough to be a phrase rather than a pattern. A one-character rule would fire inside
    /// ordinary words all day and be almost impossible to attribute once it did.
    /// </summary>
    public const int MinimumHeardLength = 2;

    public static VocabularyLearned Learn(IEnumerable<TextReplacement> existing, string? heard, string? typed)
    {
        var from = heard?.Trim() ?? string.Empty;
        var to = typed?.Trim() ?? string.Empty;
        var replacements = existing.ToArray();

        if (from.Length < MinimumHeardLength || to.Length == 0)
        {
            return new VocabularyLearned(replacements, "Both halves are needed: what you said, and what it should be.", false);
        }

        if (string.Equals(from, to, StringComparison.CurrentCulture))
        {
            return new VocabularyLearned(replacements, "Those are the same — there is nothing to correct.", false);
        }

        var at = Array.FindIndex(
            replacements,
            r => string.Equals(r.From.Trim(), from, StringComparison.CurrentCultureIgnoreCase));

        if (at >= 0)
        {
            if (string.Equals(replacements[at].To.Trim(), to, StringComparison.CurrentCulture))
            {
                return new VocabularyLearned(replacements, "TawkType already knows that one.", false);
            }

            // Overwrite rather than append. Two rules for one phrase leaves the user no way to see
            // which is in charge.
            var updated = replacements.ToArray();
            updated[at] = new TextReplacement(from, to);
            return new VocabularyLearned(updated, null, true);
        }

        return new VocabularyLearned([.. replacements, new TextReplacement(from, to)], null, false);
    }
}
