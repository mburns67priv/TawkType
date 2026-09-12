using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// Adding and editing one entry. The collision rule is the whole point of this file: a phrase gets one
/// entry, and adding and editing have to reach that the same way. If editing refused where adding
/// overwrote, a user blocked from editing a row could get what they wanted by deleting it and adding
/// it again — which is not a rule, it is an obstacle.
/// </summary>
public class VocabularyUpsertTests
{
    private static readonly TextReplacement[] Existing =
    [
        new("jupitor", "Jupitor Studio"),
        new("see sharp", "C#"),
    ];

    [Fact]
    public void Editing_a_row_replaces_it_in_place()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: 0, "jupitor", "Jupitor Studio Ltd");

        Assert.True(result.Ok);
        Assert.False(result.Replaced);
        Assert.Equal(2, result.Replacements.Length);
        Assert.Equal(new TextReplacement("jupitor", "Jupitor Studio Ltd"), result.Replacements[0]);
    }

    /// <summary>A row does not collide with itself, so saving an edit that changed nothing is fine.</summary>
    [Fact]
    public void Editing_a_row_to_the_value_it_already_has_is_allowed()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: 1, "see sharp", "C#");

        Assert.True(result.Ok);
        Assert.Equal(Existing, result.Replacements);
    }

    /// <summary>Only the casing differs, so this is still the same row and still not a collision.</summary>
    [Fact]
    public void Editing_only_the_case_of_a_phrase_is_allowed()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: 0, "JUPITOR", "Jupitor Studio");

        Assert.True(result.Ok);
        Assert.Equal("JUPITOR", result.Replacements[0].From);
    }

    [Fact]
    public void Adding_a_phrase_that_already_has_a_rule_overwrites_it_where_it_stands()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: null, "SEE SHARP", "C sharp");

        Assert.True(result.Ok);
        Assert.True(result.Replaced);
        Assert.Equal(2, result.Replacements.Length);

        // Position kept: the list is in the user's order, and an entry jumping to the end because they
        // corrected it would be a change they did not ask for.
        Assert.Equal(new TextReplacement("SEE SHARP", "C sharp"), result.Replacements[1]);
    }

    /// <summary>
    /// The same rule as adding, reached from the other direction: the entry that was already there
    /// keeps its place and takes the new value, and the row being edited goes.
    /// </summary>
    [Fact]
    public void Editing_a_row_onto_another_rows_phrase_merges_the_two()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: 0, "see sharp", "C sharp");

        Assert.True(result.Ok);
        Assert.True(result.Replaced);
        Assert.Single(result.Replacements);
        Assert.Equal(new TextReplacement("see sharp", "C sharp"), result.Replacements[0]);
    }

    [Fact]
    public void An_entry_that_changes_nothing_at_all_says_so()
    {
        var result = VocabularyEdit.UpsertReplacement(Existing, editing: null, "see sharp", "C#");

        Assert.False(result.Ok);
        Assert.Equal(Existing, result.Replacements);
    }

    [Theory]
    [InlineData("", "C#")]
    [InlineData("see sharp", "  ")]
    [InlineData("a", "A")]
    [InlineData("same", "same")]
    public void The_rules_that_refuse_a_replacement_are_the_same_ones_the_history_window_uses(string from, string to)
    {
        Assert.False(VocabularyEdit.UpsertReplacement(Existing, null, from, to).Ok);
        Assert.False(VocabularyEdit.Learn(Existing, from, to).Ok);
    }

    [Fact]
    public void A_snippets_text_keeps_its_whitespace_and_its_trigger_does_not()
    {
        var result = VocabularyEdit.UpsertSnippet([], null, "  my signature  ", "\nMichael Burns\n\n");

        Assert.True(result.Ok);
        Assert.Equal("my signature", result.Snippets[0].Trigger);
        Assert.Equal(VocabularyRules.Normalise("\nMichael Burns\n\n"), result.Snippets[0].Text);
    }

    [Fact]
    public void A_snippet_needs_a_trigger_and_some_text()
    {
        Assert.False(VocabularyEdit.UpsertSnippet([], null, "my signature", "   ").Ok);
        Assert.False(VocabularyEdit.UpsertSnippet([], null, "", "Michael Burns").Ok);
        Assert.False(VocabularyEdit.UpsertSnippet([], null, "x", "Michael Burns").Ok);
    }

    [Fact]
    public void A_snippet_trigger_that_is_taken_overwrites_where_it_stands()
    {
        Snippet[] existing = [new("my signature", "Michael Burns"), new("my address", "1 Burns Road")];

        var result = VocabularyEdit.UpsertSnippet(existing, null, "My Signature", "Michael Burns, Jupitor Studio");

        Assert.True(result.Replaced);
        Assert.Equal(2, result.Snippets.Length);
        Assert.Equal("Michael Burns, Jupitor Studio", result.Snippets[0].Text);
    }

    [Fact]
    public void A_spelling_that_differs_only_in_case_replaces_the_one_that_is_there()
    {
        var result = VocabularyEdit.UpsertSpelling(["tawktype", "GitHub"], null, "TawkType");

        Assert.True(result.Ok);
        Assert.True(result.Replaced);
        Assert.Equal(["TawkType", "GitHub"], result.Spellings);
    }

    [Fact]
    public void A_spelling_that_is_already_there_exactly_says_so_rather_than_pretending_to_change_it()
    {
        var result = VocabularyEdit.UpsertSpelling(["TawkType"], null, "TawkType");

        Assert.False(result.Ok);
        Assert.Equal(["TawkType"], result.Spellings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("...")]
    public void A_spelling_has_to_be_a_word(string word)
        => Assert.False(VocabularyEdit.UpsertSpelling(["GitHub"], null, word).Ok);

    [Fact]
    public void A_spelling_cannot_run_to_two_lines()
        => Assert.False(VocabularyEdit.UpsertSpelling(["GitHub"], null, "two" + Environment.NewLine + "lines").Ok);
}
