using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// There are three ways into these lists — a dialog, a pasted text box, and an imported file — and
/// they were not agreeing. The dialog refused a one-character rule, a rule whose halves were identical,
/// and a second rule for a phrase that already had one. Pasting or importing the same thing accepted
/// all three in silence, so the careful path was the only path that was careful.
///
/// `Clean` is the one policy the other two now go through. It is not a formality: a vocabulary full of
/// rules the dialogs would never have allowed anybody to build is a vocabulary nobody can reason about
/// when a dictation comes out wrong.
/// </summary>
public class VocabularyRulesTests
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
    public void A_clean_vocabulary_is_left_alone_and_says_nothing()
    {
        var review = VocabularyRules.Clean(Vocabulary(
            spellings: ["TawkType"],
            replacements: [new("see sharp", "C#")],
            snippets: [new("my signature", "Michael Burns")]));

        Assert.False(review.Changed);
        Assert.Empty(review.Notes);
    }

    [Fact]
    public void An_imported_rule_the_dialog_would_refuse_is_dropped_and_reported()
    {
        var review = VocabularyRules.Clean(Vocabulary(replacements:
        [
            new("see sharp", "C#"),
            new("a", "A"),            // one character: would fire inside ordinary words
            new("same", "same"),      // nothing to correct
            new("", "orphan"),        // no left-hand side
        ]));

        Assert.Single(review.Vocabulary.Replacements);
        Assert.True(review.Changed);
        Assert.Contains(review.Notes, note => note.Contains("3 replacements"));
    }

    /// <summary>
    /// The first entry wins, because that is what PhraseBook already does with two rules of the same
    /// length: it applies them in order and the first to match has already changed the text. Keeping
    /// the last would make the cleaned list behave differently from the list it cleaned.
    /// </summary>
    [Fact]
    public void A_second_rule_for_a_phrase_is_dropped_and_the_first_one_stands()
    {
        var review = VocabularyRules.Clean(Vocabulary(replacements:
        [
            new("see sharp", "C#"),
            new("SEE SHARP", "C sharp"),
        ]));

        Assert.Single(review.Vocabulary.Replacements);
        Assert.Equal("C#", review.Vocabulary.Replacements[0].To);
    }

    [Fact]
    public void Duplicate_spellings_and_triggers_go_the_same_way()
    {
        var review = VocabularyRules.Clean(Vocabulary(
            spellings: ["TawkType", "tawktype", "GitHub"],
            snippets: [new("my signature", "Michael Burns"), new("My Signature", "someone else")]));

        Assert.Equal(["TawkType", "GitHub"], review.Vocabulary.Spellings);
        Assert.Single(review.Vocabulary.Snippets);
        Assert.Equal("Michael Burns", review.Vocabulary.Snippets[0].Text);
    }

    [Fact]
    public void Everything_is_trimmed_except_a_snippets_text()
    {
        var review = VocabularyRules.Clean(Vocabulary(
            spellings: ["  TawkType  "],
            replacements: [new("  see sharp  ", "  C#  ")],
            snippets: [new("  my signature  ", "  Michael Burns\n\n")]));

        Assert.Equal("TawkType", review.Vocabulary.Spellings[0]);
        Assert.Equal(new TextReplacement("see sharp", "C#"), review.Vocabulary.Replacements[0]);
        Assert.Equal("my signature", review.Vocabulary.Snippets[0].Trigger);
        Assert.Equal(VocabularyRules.Normalise("  Michael Burns\n\n"), review.Vocabulary.Snippets[0].Text);
    }

    [Fact]
    public void Snippet_text_is_normalised_so_a_round_trip_can_be_lossless()
    {
        var review = VocabularyRules.Clean(Vocabulary(snippets: [new("bug", "Steps:\n1.\n2.")]));

        var text = review.Vocabulary.Snippets[0].Text;

        Assert.Equal("Steps:" + Environment.NewLine + "1." + Environment.NewLine + "2.", text);
        Assert.Equal(review.Vocabulary.Snippets, VocabularyFormat.ParseSnippets(VocabularyFormat.Format(review.Vocabulary.Snippets)));
    }

    [Fact]
    public void A_null_vocabulary_is_an_empty_one_rather_than_a_crash()
    {
        var review = VocabularyRules.Clean(null);

        Assert.False(review.Changed);
        Assert.Empty(review.Vocabulary.Spellings);
        Assert.Empty(review.Vocabulary.Replacements);
        Assert.Empty(review.Vocabulary.Snippets);
    }

    /// <summary>
    /// Cleaning a cleaned vocabulary changes nothing. Import, text mode and Save all run this, in any
    /// order and more than once, so it has to be safe to run twice.
    /// </summary>
    [Fact]
    public void Cleaning_is_idempotent()
    {
        var messy = Vocabulary(
            spellings: ["TawkType", "tawktype", "x"],
            replacements: [new("see sharp", "C#"), new("SEE SHARP", "C sharp"), new("a", "A")],
            snippets: [new("my signature", "Michael Burns\n"), new("my signature", "other")]);

        var once = VocabularyRules.Clean(messy).Vocabulary;
        var twice = VocabularyRules.Clean(once);

        Assert.False(twice.Changed);
        Assert.Equal(once.Spellings, twice.Vocabulary.Spellings);
        Assert.Equal(once.Replacements, twice.Vocabulary.Replacements);
        Assert.Equal(once.Snippets, twice.Vocabulary.Snippets);
    }
}
