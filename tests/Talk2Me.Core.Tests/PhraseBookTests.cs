using Talk2Me.Core.Settings;
using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

/// <summary>
/// The personalisation that has to work with the Claude pass switched off. Correcting the same name
/// every day makes an otherwise accurate app feel broken.
/// </summary>
public class PhraseBookTests
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
    public void A_preferred_spelling_is_applied_however_it_was_capitalised()
    {
        var vocabulary = Vocabulary(spellings: ["Talk2Me", "GitHub"]);

        Assert.Equal(
            "I pushed Talk2Me to GitHub",
            PhraseBook.Apply("I pushed talk2me to github", vocabulary).Text);
    }

    [Fact]
    public void A_replacement_turns_what_was_heard_into_what_was_meant()
    {
        var vocabulary = Vocabulary(replacements: [new TextReplacement("talk to me", "Talk2Me")]);

        Assert.Equal("Talk2Me is a dictation app", PhraseBook.Apply("talk to me is a dictation app", vocabulary).Text);
    }

    /// <summary>Spacing is not something the recogniser is consistent about.</summary>
    [Theory]
    [InlineData("jupitor  studio")]
    [InlineData("Jupitor\tStudio")]
    public void Extra_space_inside_a_phrase_still_matches(string heard)
    {
        var vocabulary = Vocabulary(spellings: ["Jupitor Studio"]);

        Assert.Equal("Jupitor Studio", PhraseBook.Apply(heard, vocabulary).Text);
    }

    /// <summary>
    /// Whole phrases only. Substring replacement would turn "restudio" into something unrecognisable
    /// and make the feature a liability.
    /// </summary>
    [Theory]
    [InlineData("restudio")]
    [InlineData("studios")]
    [InlineData("mystudio")]
    public void A_phrase_inside_a_longer_word_is_left_alone(string text)
    {
        var vocabulary = Vocabulary(spellings: ["studio"]);

        Assert.Equal(text, PhraseBook.Apply(text, vocabulary).Text);
    }

    [Fact]
    public void Punctuation_around_a_phrase_does_not_stop_it_matching()
    {
        var vocabulary = Vocabulary(spellings: ["GitHub"]);

        Assert.Equal("(GitHub), yes.", PhraseBook.Apply("(github), yes.", vocabulary).Text);
    }

    /// <summary>Otherwise the short entry eats the start of the long one and leaves a mess behind.</summary>
    [Fact]
    public void The_longest_phrase_wins()
    {
        var vocabulary = Vocabulary(spellings: ["Jupitor Studio", "Studio"]);

        Assert.Equal("Jupitor Studio", PhraseBook.Apply("jupitor studio", vocabulary).Text);
    }

    [Fact]
    public void A_snippet_is_inserted_by_saying_insert_and_its_trigger()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("my signature", "Michael Burns, Jupitor Studio")]);

        var result = PhraseBook.Apply("insert my signature", vocabulary);

        Assert.Equal("Michael Burns, Jupitor Studio", result.Text);
        Assert.True(result.ExpandedSnippet);
    }

    [Fact]
    public void A_snippet_trigger_without_the_word_insert_is_just_words()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("my signature", "Michael Burns")]);

        var result = PhraseBook.Apply("this is my signature really", vocabulary);

        Assert.Equal("this is my signature really", result.Text);
        Assert.False(result.ExpandedSnippet);
    }

    [Fact]
    public void A_snippet_can_be_inserted_mid_sentence()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("project link", "https://github.com/JupitorStudioDev/TawkType")]);

        var result = PhraseBook.Apply("see insert project link for details", vocabulary);

        Assert.Equal("see https://github.com/JupitorStudioDev/TawkType for details", result.Text);
        Assert.True(result.ExpandedSnippet);
    }

    /// <summary>Multiline snippets exist; the delivery path knows to paste rather than type them.</summary>
    [Fact]
    public void A_snippet_keeps_its_line_breaks_exactly()
    {
        var template = "Steps:" + Environment.NewLine + "1." + Environment.NewLine + "2.";
        var vocabulary = Vocabulary(snippets: [new Snippet("bug template", template)]);

        var result = PhraseBook.Apply("insert bug template", vocabulary);

        Assert.Equal(template, result.Text);
        Assert.True(Delivery.IsMultiline(result.Text));
    }

    /// <summary>A dollar sign in saved text is a dollar sign, not a regex group reference.</summary>
    [Fact]
    public void Saved_text_containing_a_dollar_sign_survives()
    {
        var vocabulary = Vocabulary(snippets: [new Snippet("price", "$20 and $0 shipping")]);

        Assert.Equal("$20 and $0 shipping", PhraseBook.Apply("insert price", vocabulary).Text);
    }

    [Fact]
    public void Nothing_configured_changes_nothing()
    {
        var result = PhraseBook.Apply("just an ordinary sentence", Vocabulary());

        Assert.Equal("just an ordinary sentence", result.Text);
        Assert.False(result.ExpandedSnippet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_is_left_alone(string text)
    {
        Assert.Equal(text, PhraseBook.Apply(text, Vocabulary(spellings: ["Talk2Me"])).Text);
    }

    [Fact]
    public void Blank_entries_are_ignored_rather_than_matching_everything()
    {
        var vocabulary = Vocabulary(
            spellings: ["", "  "],
            replacements: [new TextReplacement("", "nonsense")],
            snippets: [new Snippet("", "nonsense")]);

        Assert.Equal("untouched", PhraseBook.Apply("untouched", vocabulary).Text);
    }
}
