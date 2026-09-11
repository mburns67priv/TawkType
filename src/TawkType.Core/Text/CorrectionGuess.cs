namespace TawkType.Core.Text;

/// <summary>The part of a dictation that actually changed.</summary>
/// <param name="Heard">What the recogniser produced, narrowed to the words that differ.</param>
/// <param name="Typed">What it should have been, narrowed the same way.</param>
public readonly record struct Correction(string Heard, string Typed);

/// <summary>
/// Works out which words a correction is really about, by trimming the parts that both versions agree
/// on from each end.
///
/// Without this, "remember this correction" offers the user the whole sentence on both sides, and a
/// replacement rule for a whole sentence is close to useless: it only ever fires on that exact
/// sentence again. "send it to jupitor studio please" against "Send it to Jupiter Studio please."
/// is really a correction of two words, and those two are what should be saved.
///
/// Words, not characters: a character-level diff of "jupitor"/"Jupiter" proposes the letters that
/// differ, which is not a phrase anyone can read or edit.
/// </summary>
public static class CorrectionGuess
{
    public static Correction Between(string? heard, string? typed)
    {
        var left = Words(heard);
        var right = Words(typed);

        if (left.Length == 0 || right.Length == 0)
        {
            return new Correction(heard?.Trim() ?? string.Empty, typed?.Trim() ?? string.Empty);
        }

        // The suffix first, because whether the leading sentence capital can be trimmed depends on
        // what is left after it.
        var end = 0;
        while (end < left.Length && end < right.Length && Same(left[^(end + 1)], right[^(end + 1)]))
        {
            end++;
        }

        var start = 0;
        while (start < left.Length - end && start < right.Length - end && Same(left[start], right[start]))
        {
            start++;
        }

        // A capital at the very start is cleanup punctuating a sentence, not the user correcting a
        // word — unless stepping over it leaves nothing on one side, in which case that first word
        // really is what changed.
        if (start == 0
            && CapitalOnly(left[0], right[0])
            && left.Length - end > 1
            && right.Length - end > 1)
        {
            start = 1;
            while (start < left.Length - end && start < right.Length - end && Same(left[start], right[start]))
            {
                start++;
            }
        }

        var heardSpan = left[start..(left.Length - end)];
        var typedSpan = right[start..(right.Length - end)];

        // Both sides identical word for word, or a pure insertion or deletion: there is no pair of
        // phrases to swap, so offer the whole thing and let the user decide what they meant.
        return heardSpan.Length == 0 || typedSpan.Length == 0
            ? new Correction(heard!.Trim(), typed!.Trim())
            : new Correction(string.Join(' ', heardSpan), string.Join(' ', typedSpan));
    }

    /// <summary>The same word but for the case of its first letter.</summary>
    private static bool CapitalOnly(string left, string right)
        => !string.Equals(Bare(left), Bare(right), StringComparison.CurrentCulture)
            && string.Equals(Bare(left), Bare(right), StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(Bare(left)[1..], Bare(right)[1..], StringComparison.CurrentCulture);

    /// <summary>
    /// Edge punctuation is ignored — a full stop added by cleanup is not a correction — but case is
    /// not. "studio" becoming "Studio" is exactly the kind of thing a vocabulary entry is for, and
    /// treating it as unchanged would trim it out of its own correction.
    /// </summary>
    private static bool Same(string left, string right)
        => string.Equals(Bare(left), Bare(right), StringComparison.CurrentCulture);

    private static string Bare(string word) => word.Trim('.', ',', '!', '?', ';', ':', '"', '\'');

    private static string[] Words(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
