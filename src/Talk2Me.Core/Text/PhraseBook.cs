using System.Text.RegularExpressions;
using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Text;

/// <summary>What a pass over the text did, and whether the result should be left alone from here.</summary>
/// <param name="Text">The text after substitution.</param>
/// <param name="ExpandedSnippet">
/// True when saved text was inserted. Snippets are typed exactly as written — a signature, a URL, a
/// template — so a dictation that expanded one is not sent for rewriting afterwards.
/// </param>
public readonly record struct PhraseResult(string Text, bool ExpandedSnippet);

/// <summary>
/// Local spellings, replacements and snippets, applied to a transcript without a model in sight.
///
/// This is the part of personalisation that has to work offline and with the Claude pass switched
/// off. Correcting the same name every day makes an otherwise accurate app feel broken, and a
/// vocabulary that only exists inside someone else's prompt does nothing for a local-only user.
///
/// Matching is on whole phrases, ignoring case, with flexible spacing between the words — the
/// recogniser is not consistent about either. Longer phrases are tried first, so "Jupitor Studio"
/// wins over "studio". Replacement text is inserted verbatim, casing and all, because deciding it was
/// worth writing down is the whole point.
/// </summary>
public static class PhraseBook
{
    /// <summary>What has to be said before a snippet trigger. Explicit, to stop accidental expansion.</summary>
    public const string SnippetPrefix = "insert";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static PhraseResult Apply(string text, VocabularySettings vocabulary)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PhraseResult(text, ExpandedSnippet: false);
        }

        // Snippets first: their text is exact, and nothing after this should be rewriting it.
        var expanded = false;
        foreach (var snippet in Ordered(vocabulary.Snippets, s => s.Trigger))
        {
            var before = text;
            text = Replace(text, SnippetPrefix + " " + snippet.Trigger, snippet.Text);
            expanded |= !ReferenceEquals(before, text) && !string.Equals(before, text, StringComparison.Ordinal);
        }

        foreach (var replacement in Ordered(vocabulary.Replacements, r => r.From))
        {
            text = Replace(text, replacement.From, replacement.To);
        }

        foreach (var spelling in Ordered(vocabulary.Spellings, s => s))
        {
            text = Replace(text, spelling, spelling);
        }

        return new PhraseResult(text.Trim(), expanded);
    }

    /// <summary>Longest phrase first, so a short entry cannot eat the start of a longer one.</summary>
    private static IEnumerable<T> Ordered<T>(IEnumerable<T>? entries, Func<T, string> phrase)
        => (entries ?? []).Where(e => !string.IsNullOrWhiteSpace(phrase(e))).OrderByDescending(e => phrase(e).Length);

    private static string Replace(string text, string phrase, string with)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return text;
        }

        try
        {
            return Regex.Replace(text, Pattern(phrase), with.Replace("$", "$$"), RegexOptions.IgnoreCase, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological phrase against a long transcript. The words matter more than the rule.
            return text;
        }
    }

    /// <summary>
    /// The phrase as a whole-word match with flexible spacing. The boundaries are letters and digits
    /// rather than \b so that entries ending in punctuation — "TawkType." — still behave.
    /// </summary>
    private static string Pattern(string phrase)
    {
        var words = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        return @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", words) + @"(?![\p{L}\p{N}])";
    }
}
