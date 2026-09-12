using System.Text.RegularExpressions;
using TawkType.Core.Settings;

namespace TawkType.Core.Text;

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

        // Snippets first, and each one goes in as a placeholder rather than as itself. The passes
        // below would otherwise rewrite the text of a snippet that has only just been inserted — a
        // replacement or a spelling matching inside somebody's signature — and the final Trim would
        // eat the blank line a signature usually ends with. This comment used to claim that nothing
        // after this point rewrites a snippet, while the code went straight on and did.
        var snippets = new List<string>();
        foreach (var snippet in Ordered(vocabulary.Snippets, s => s.Trigger))
        {
            var before = text;
            var placeholder = Placeholder(snippets.Count);

            text = Replace(text, SnippetPrefix + " " + snippet.Trigger, placeholder);

            if (!string.Equals(before, text, StringComparison.Ordinal))
            {
                snippets.Add(snippet.Text);
            }
        }

        foreach (var replacement in Ordered(vocabulary.Replacements, r => r.From))
        {
            text = Replace(text, replacement.From, replacement.To);
        }

        foreach (var spelling in Ordered(vocabulary.Spellings, s => s))
        {
            text = Replace(text, spelling, spelling);
        }

        // Trim before the snippets go back, so it tidies the dictation around them and never the text
        // the user saved. A snippet that is the whole dictation comes out exactly as it was written.
        text = text.Trim();

        for (var i = 0; i < snippets.Count; i++)
        {
            text = text.Replace(Placeholder(i), snippets[i], StringComparison.Ordinal);
        }

        return new PhraseResult(text, snippets.Count > 0);
    }

    /// <summary>
    /// Stands in for one snippet's text while the other passes run.
    ///
    /// A single character from the Unicode private use area, which is neither a letter nor a digit, so
    /// the whole-word boundaries in <see cref="Pattern"/> behave exactly as they would around any
    /// other punctuation — and no phrase anybody types can contain one, so nothing can match it by
    /// accident. Digits would not do: a replacement whose left-hand side is a number could match one.
    /// </summary>
    private static string Placeholder(int index) => ((char)(0xE000 + index)).ToString();

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
