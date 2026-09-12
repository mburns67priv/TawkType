using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

/// <summary>
/// A snippet is saved text, typed exactly as written. That is what the README promises, what the
/// settings hint says, and what `PhraseBook` itself claimed in a comment while doing the opposite:
/// snippets were expanded first and then the replacement and spelling passes ran straight over the
/// text that had just been inserted, and the final Trim took whatever whitespace the user had put at
/// either end of it.
///
/// These are the cases that were wrong. They are their own file because "exactly as written" is a
/// promise rather than an implementation detail, and the next person to reorder the passes in
/// `Apply` needs to fail a test named after the promise.
/// </summary>
public class SnippetExactnessTests
{
    private static VocabularySettings Vocabulary(
        string[]? spellings = null,
        TextReplacement[]? replacements = null,
        Snippet[]? snippets = null) => new()
    {
        Spellings = spellings ?? [],
        Replacements = replacements ?? [],
        Snippets = snippets ?? [],
    };

    [Fact]
    public void A_replacement_does_not_rewrite_the_text_a_snippet_just_inserted()
    {
        var vocabulary = Vocabulary(
            replacements: [new TextReplacement("see sharp", "C#"), new TextReplacement("Burns", "BURNS")],
            snippets: [new Snippet("my signature", "Michael Burns, Jupitor Studio")]);

        Assert.Equal(
            "Michael Burns, Jupitor Studio",
            PhraseBook.Apply("insert my signature", vocabulary).Text);
    }

    [Fact]
    public void A_spelling_does_not_rewrite_the_text_a_snippet_just_inserted()
    {
        var vocabulary = Vocabulary(
            spellings: ["TawkType"],
            snippets: [new Snippet("the old name", "we used to call it tawktype")]);

        Assert.Equal(
            "we used to call it tawktype",
            PhraseBook.Apply("insert the old name", vocabulary).Text);
    }

    /// <summary>
    /// The rest of the dictation is still the recogniser's output, and still wants correcting. Only
    /// the snippet is exempt.
    /// </summary>
    [Fact]
    public void The_words_around_a_snippet_are_still_corrected()
    {
        var vocabulary = Vocabulary(
            replacements: [new TextReplacement("see sharp", "C#")],
            snippets: [new Snippet("my signature", "Michael Burns")]);

        Assert.Equal(
            "I write C# all day. Michael Burns",
            PhraseBook.Apply("I write see sharp all day. insert my signature", vocabulary).Text);
    }

    /// <summary>
    /// A signature usually ends with a blank line, and the user put it there on purpose. Trim used to
    /// take it, because it ran over the finished text with the snippet already in it.
    /// </summary>
    [Fact]
    public void A_snippets_own_leading_and_trailing_whitespace_survives()
    {
        var signature = "\n--\nMichael Burns\n\n";
        var vocabulary = Vocabulary(snippets: [new Snippet("my signature", signature)]);

        Assert.Equal(signature, PhraseBook.Apply("insert my signature", vocabulary).Text);
    }

    /// <summary>Trimming still does its job on the dictation around the snippet.</summary>
    [Fact]
    public void The_dictation_around_a_snippet_is_still_trimmed()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("my signature", "Michael Burns")]);

        Assert.Equal(
            "regards Michael Burns",
            PhraseBook.Apply("   regards insert my signature   ", vocabulary).Text);
    }

    [Fact]
    public void Two_snippets_in_one_dictation_both_come_out_whole()
    {
        var vocabulary = Vocabulary(
            replacements: [new TextReplacement("Burns", "BURNS")],
            snippets:
            [
                new Snippet("my signature", "Michael Burns"),
                new Snippet("my address", "1 Burns Road"),
            ]);

        Assert.Equal(
            "Michael Burns and 1 Burns Road",
            PhraseBook.Apply("insert my signature and insert my address", vocabulary).Text);
    }

    /// <summary>
    /// The placeholder that stands in for a snippet must not be reachable by anything the user can
    /// type. A digit would be: a replacement whose left-hand side is a number would match one.
    /// </summary>
    [Fact]
    public void A_numeric_replacement_cannot_reach_the_placeholder()
    {
        var vocabulary = Vocabulary(
            replacements: [new TextReplacement("0", "zero"), new TextReplacement("1", "one")],
            snippets: [new Snippet("my signature", "Michael Burns")]);

        Assert.Equal(
            "Michael Burns",
            PhraseBook.Apply("insert my signature", vocabulary).Text);
    }

    [Fact]
    public void Expanding_a_snippet_is_still_reported()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("my signature", "Michael Burns")]);

        Assert.True(PhraseBook.Apply("insert my signature", vocabulary).ExpandedSnippet);
        Assert.False(PhraseBook.Apply("nothing to expand here", vocabulary).ExpandedSnippet);
    }
}
