namespace TawkType.Core.Settings;

/// <summary>A vocabulary that has been put in order, and what had to be done to it.</summary>
/// <param name="Vocabulary">The cleaned lists.</param>
/// <param name="Notes">
/// One sentence per thing that changed, for telling the user afterwards. Empty when nothing did.
/// </param>
public readonly record struct VocabularyReview(VocabularySettings Vocabulary, string[] Notes)
{
    public bool Changed => Notes.Length > 0;
}

/// <summary>
/// The rules a vocabulary entry has to satisfy, in one place, because there are three ways into these
/// lists and they were not agreeing.
///
/// A dialog refuses a one-character rule, a rule whose halves are identical, and a second rule for a
/// phrase that already has one. Pasting the same thing into the text box accepted all three silently,
/// and importing a file accepted them too — so the careful path was the only path that was careful,
/// and the other two could leave a vocabulary the dialogs would never have allowed anybody to build.
///
/// The three Check methods are the single judgement: <see cref="VocabularyEdit"/> asks them about one
/// entry, and <see cref="Clean"/> asks them about a whole list.
/// </summary>
public static class VocabularyRules
{
    /// <summary>
    /// Long enough to be a phrase rather than a pattern. A one-character rule would fire inside
    /// ordinary words all day and be almost impossible to attribute once it did.
    /// </summary>
    public const int MinimumPhraseLength = 2;

    /// <summary>Why this replacement cannot be kept, or null.</summary>
    public static string? CheckReplacement(string from, string to)
    {
        if (from.Length == 0 || to.Length == 0)
        {
            return "Both halves are needed: what you said, and what it should be.";
        }

        if (from.Length < MinimumPhraseLength)
        {
            return "That is too short to be a phrase — a one-letter rule would fire inside ordinary words all day.";
        }

        return string.Equals(from, to, StringComparison.CurrentCulture)
            ? "Those are the same — there is nothing to correct."
            : null;
    }

    /// <summary>Why this snippet cannot be kept, or null. The text keeps its whitespace; the trigger does not.</summary>
    public static string? CheckSnippet(string trigger, string text)
    {
        if (trigger.Length == 0 || text.Trim().Length == 0)
        {
            return "A snippet needs a trigger and some text.";
        }

        return trigger.Length < MinimumPhraseLength
            ? "That trigger is too short — say a couple of words, so an ordinary sentence cannot set it off."
            : null;
    }

    /// <summary>Why this spelling cannot be kept, or null.</summary>
    public static string? CheckSpelling(string word)
    {
        if (word.Length == 0)
        {
            return "A spelling needs a word.";
        }

        if (word.Length < MinimumPhraseLength)
        {
            return "That is too short to be a spelling — a single letter would be rewritten everywhere it appeared.";
        }

        if (word.Contains('\n') || word.Contains('\r'))
        {
            return "A spelling is one word or phrase, on one line.";
        }

        return word.Any(char.IsLetterOrDigit)
            ? null
            : "A spelling needs a letter or a number in it.";
    }

    /// <summary>
    /// Puts a whole vocabulary in order: trims, drops what cannot be kept, and removes a later entry
    /// for a phrase that already has one.
    ///
    /// The first entry for a phrase wins, not the last, because that is what <c>PhraseBook</c> already
    /// does when two rules are the same length — it applies them in order and the first one to match
    /// has already changed the text. Keeping the last here would mean the cleaned list behaved
    /// differently from the list it cleaned.
    /// </summary>
    public static VocabularyReview Clean(VocabularySettings? source)
    {
        var vocabulary = source ?? new VocabularySettings();
        var notes = new List<string>();

        var spellings = new List<string>();
        var seenSpellings = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var droppedSpellings = 0;
        var duplicateSpellings = 0;

        foreach (var raw in vocabulary.Spellings ?? [])
        {
            var word = (raw ?? string.Empty).Trim();

            if (CheckSpelling(word) is not null)
            {
                droppedSpellings++;
            }
            else if (!seenSpellings.Add(word))
            {
                duplicateSpellings++;
            }
            else
            {
                spellings.Add(word);
            }
        }

        var replacements = new List<TextReplacement>();
        var seenFrom = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var droppedReplacements = 0;
        var duplicateReplacements = 0;

        foreach (var raw in vocabulary.Replacements ?? [])
        {
            var from = (raw?.From ?? string.Empty).Trim();
            var to = (raw?.To ?? string.Empty).Trim();

            if (CheckReplacement(from, to) is not null)
            {
                droppedReplacements++;
            }
            else if (!seenFrom.Add(from))
            {
                duplicateReplacements++;
            }
            else
            {
                replacements.Add(new TextReplacement(from, to));
            }
        }

        var snippets = new List<Snippet>();
        var seenTriggers = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var droppedSnippets = 0;
        var duplicateSnippets = 0;

        foreach (var raw in vocabulary.Snippets ?? [])
        {
            var trigger = (raw?.Trigger ?? string.Empty).Trim();
            var text = Normalise(raw?.Text ?? string.Empty);

            if (CheckSnippet(trigger, text) is not null)
            {
                droppedSnippets++;
            }
            else if (!seenTriggers.Add(trigger))
            {
                duplicateSnippets++;
            }
            else
            {
                snippets.Add(new Snippet(trigger, text));
            }
        }

        Note(notes, droppedSpellings, "spelling", "not usable as written");
        Note(notes, duplicateSpellings, "spelling", "already in the list");
        Note(notes, droppedReplacements, "replacement", "not usable as written");
        Note(notes, duplicateReplacements, "replacement", "that phrase already had a rule");
        Note(notes, droppedSnippets, "snippet", "not usable as written");
        Note(notes, duplicateSnippets, "snippet", "that trigger was already taken");

        return new VocabularyReview(
            new VocabularySettings
            {
                Spellings = [.. spellings],
                Replacements = [.. replacements],
                Snippets = [.. snippets],
            },
            [.. notes]);
    }

    /// <summary>
    /// Snippet text in one form, so a round trip through the text box gives back what went in.
    ///
    /// <c>VocabularyFormat</c> writes every kind of line break as the same escape and cannot tell them
    /// apart on the way back, so the only way the round trip is lossless is if everything stored is
    /// already in one form. The platform newline, because multiline snippets are delivered through the
    /// clipboard and Windows expects CRLF there.
    /// </summary>
    public static string Normalise(string? text)
        => (text ?? string.Empty).ReplaceLineEndings(Environment.NewLine);

    /// <summary>
    /// True when two vocabularies hold the same entries in the same order.
    ///
    /// The Settings window works on a clone and saves the whole thing, so it has to be able to tell
    /// whether the copy on disk has moved underneath it — the history window's Remember… writes a
    /// replacement straight away, and a Settings window that opened before that would otherwise save
    /// its older copy back over it. Only the vocabulary is compared, because a settings file also
    /// changes when the history window is merely dragged.
    /// </summary>
    public static bool Same(VocabularySettings? left, VocabularySettings? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // The two entry types are records, so this is entry-by-entry value equality and not identity.
        return left.Spellings.SequenceEqual(right.Spellings, StringComparer.Ordinal)
            && left.Replacements.SequenceEqual(right.Replacements)
            && left.Snippets.SequenceEqual(right.Snippets);
    }

    private static void Note(List<string> notes, int count, string thing, string why)
    {
        if (count > 0)
        {
            notes.Add($"{count} {thing}{(count == 1 ? string.Empty : "s")} dropped - {why}.");
        }
    }
}
