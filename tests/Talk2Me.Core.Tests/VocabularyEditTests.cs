using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Tests;

public class VocabularyEditTests
{
    private static readonly TextReplacement[] Existing = [new("jupitor", "Jupitor Studio")];

    [Fact]
    public void A_new_correction_is_added()
    {
        var result = VocabularyEdit.Learn(Existing, "talk to me", "Talk2Me");

        Assert.True(result.Ok);
        Assert.False(result.Replaced);
        Assert.Equal(2, result.Replacements.Length);
        Assert.Contains(new TextReplacement("talk to me", "Talk2Me"), result.Replacements);
    }

    [Fact]
    public void Both_halves_are_needed()
    {
        Assert.False(VocabularyEdit.Learn(Existing, "", "Talk2Me").Ok);
        Assert.False(VocabularyEdit.Learn(Existing, "talk to me", "  ").Ok);
    }

    /// <summary>A one-character rule would fire inside ordinary words and be impossible to attribute.</summary>
    [Fact]
    public void A_single_character_is_not_a_phrase()
        => Assert.False(VocabularyEdit.Learn(Existing, "a", "A").Ok);

    [Fact]
    public void A_phrase_cannot_be_replaced_with_itself()
        => Assert.False(VocabularyEdit.Learn(Existing, "talk to me", "talk to me").Ok);

    /// <summary>
    /// Two rules for one phrase would leave the user no way to see which was winning, so the newer
    /// one takes over rather than joining it.
    /// </summary>
    [Fact]
    public void A_second_rule_for_the_same_phrase_overwrites_the_first()
    {
        var result = VocabularyEdit.Learn(Existing, "JUPITOR", "Jupitor Studio Ltd");

        Assert.True(result.Ok);
        Assert.True(result.Replaced);
        Assert.Single(result.Replacements);
        Assert.Equal("Jupitor Studio Ltd", result.Replacements[0].To);
    }

    [Fact]
    public void Teaching_it_something_it_already_knows_is_refused_rather_than_duplicated()
    {
        var result = VocabularyEdit.Learn(Existing, "jupitor", "Jupitor Studio");

        Assert.False(result.Ok);
        Assert.Single(result.Replacements);
    }

    [Fact]
    public void A_refused_correction_leaves_the_list_exactly_as_it_was()
    {
        var result = VocabularyEdit.Learn(Existing, "x", "y");

        Assert.Equal(Existing, result.Replacements);
    }
}
