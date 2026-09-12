namespace TawkType.Core.Settings;

/// <summary>The outcome of trying to teach TawkType a correction.</summary>
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

/// <summary>The outcome of adding or editing a snippet.</summary>
public readonly record struct SnippetsEdited(Snippet[] Snippets, string? Problem, bool Replaced)
{
    public bool Ok => Problem is null;
}

/// <summary>The outcome of adding or editing a spelling.</summary>
public readonly record struct SpellingsEdited(string[] Spellings, string? Problem, bool Replaced)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Adding and editing single vocabulary entries.
///
/// Pure, and here rather than in a window, because the rules are the interesting part and every one of
/// them is a way the feature could quietly do damage: a rule matching everything, a rule that replaces
/// a phrase with itself, or a second rule for a phrase that already has one, where the user would have
/// no way to tell which of the two was winning.
///
/// **One collision rule, for adding and for editing alike.** A phrase gets one entry. When a save
/// would give two entries the same phrase, the entry that was already there keeps its position and
/// takes the new value, and the row being edited goes. Adding and editing behaving differently — one
/// overwriting, the other refusing — would mean a user blocked from editing a row could get what they
/// wanted by deleting it and adding it again, which is not a rule, it is an obstacle.
/// </summary>
public static class VocabularyEdit
{
    /// <inheritdoc cref="VocabularyRules.MinimumPhraseLength"/>
    public const int MinimumHeardLength = VocabularyRules.MinimumPhraseLength;

    /// <summary>Adding a replacement from a correction the user has just made, in the history window.</summary>
    public static VocabularyLearned Learn(IEnumerable<TextReplacement> existing, string? heard, string? typed)
        => UpsertReplacement(existing, editing: null, heard, typed);

    /// <param name="editing">The index being edited, or null when this is a new entry.</param>
    public static VocabularyLearned UpsertReplacement(
        IEnumerable<TextReplacement> existing,
        int? editing,
        string? heard,
        string? typed)
    {
        var list = existing.ToArray();
        var from = (heard ?? string.Empty).Trim();
        var to = (typed ?? string.Empty).Trim();

        if (VocabularyRules.CheckReplacement(from, to) is { } problem)
        {
            return new VocabularyLearned(list, problem, false);
        }

        var collision = FindReplacement(list, editing, from);

        if (collision < 0)
        {
            return new VocabularyLearned(Write(list, editing, new TextReplacement(from, to)), null, false);
        }

        if (string.Equals(list[collision].To.Trim(), to, StringComparison.CurrentCulture))
        {
            return new VocabularyLearned(list, "TawkType already knows that one.", false);
        }

        return new VocabularyLearned(Merge(list, editing, collision, new TextReplacement(from, to)), null, true);
    }

    public static SnippetsEdited UpsertSnippet(
        IEnumerable<Snippet> existing,
        int? editing,
        string? trigger,
        string? text)
    {
        var list = existing.ToArray();
        var name = (trigger ?? string.Empty).Trim();

        // The text is normalised but never trimmed: a snippet is typed exactly as written, and the
        // blank line at the end of a signature is part of what the user wrote.
        var body = VocabularyRules.Normalise(text);

        if (VocabularyRules.CheckSnippet(name, body) is { } problem)
        {
            return new SnippetsEdited(list, problem, false);
        }

        var collision = FindSnippet(list, editing, name);

        if (collision < 0)
        {
            return new SnippetsEdited(Write(list, editing, new Snippet(name, body)), null, false);
        }

        if (string.Equals(list[collision].Text, body, StringComparison.CurrentCulture))
        {
            return new SnippetsEdited(list, "TawkType already has that snippet.", false);
        }

        return new SnippetsEdited(Merge(list, editing, collision, new Snippet(name, body)), null, true);
    }

    public static SpellingsEdited UpsertSpelling(IEnumerable<string> existing, int? editing, string? word)
    {
        var list = existing.ToArray();
        var spelling = (word ?? string.Empty).Trim();

        if (VocabularyRules.CheckSpelling(spelling) is { } problem)
        {
            return new SpellingsEdited(list, problem, false);
        }

        var collision = FindSpelling(list, editing, spelling);

        if (collision < 0)
        {
            return new SpellingsEdited(Write(list, editing, spelling), null, false);
        }

        // A spelling is its own value, so a collision that matches exactly leaves nothing to do. Saying
        // so is more use than reporting a change that did not happen.
        if (string.Equals(list[collision], spelling, StringComparison.CurrentCulture))
        {
            return new SpellingsEdited(list, "That word is already in your spellings.", false);
        }

        return new SpellingsEdited(Merge(list, editing, collision, spelling), null, true);
    }

    /// <summary>The index of another entry already holding this phrase, or -1. The row being edited never counts.</summary>
    public static int FindReplacement(IEnumerable<TextReplacement> existing, int? editing, string? heard)
        => IndexOf(existing.Select(r => r.From), editing, heard);

    public static int FindSnippet(IEnumerable<Snippet> existing, int? editing, string? trigger)
        => IndexOf(existing.Select(s => s.Trigger), editing, trigger);

    public static int FindSpelling(IEnumerable<string> existing, int? editing, string? word)
        => IndexOf(existing, editing, word);

    private static int IndexOf(IEnumerable<string> phrases, int? editing, string? phrase)
    {
        var wanted = (phrase ?? string.Empty).Trim();
        var at = 0;

        foreach (var candidate in phrases)
        {
            if (at != editing
                && string.Equals((candidate ?? string.Empty).Trim(), wanted, StringComparison.CurrentCultureIgnoreCase))
            {
                return at;
            }

            at++;
        }

        return -1;
    }

    /// <summary>Replaces the row being edited, or appends when this is a new entry.</summary>
    private static T[] Write<T>(T[] list, int? editing, T entry)
    {
        if (editing is not int at || at < 0 || at >= list.Length)
        {
            return [.. list, entry];
        }

        var updated = list.ToArray();
        updated[at] = entry;
        return updated;
    }

    /// <summary>
    /// The surviving entry takes the position of the one that was already there, and the row being
    /// edited is removed. Keeping the older position matters: the list is in the user's order, and an
    /// entry jumping to the end of it because they corrected its spelling would be a change they did
    /// not ask for.
    /// </summary>
    private static T[] Merge<T>(T[] list, int? editing, int collision, T entry)
    {
        var merged = list.ToArray();
        merged[collision] = entry;

        return editing is int at && at >= 0 && at < merged.Length && at != collision
            ? [.. merged.Where((_, i) => i != at)]
            : merged;
    }
}
