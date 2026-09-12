using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// The text box is a view onto a list, not the list itself. That is only safe if a line it cannot use
/// is reported rather than dropped: a silently discarded line is a rule the user wrote, saved, and
/// never sees again — and they would have no reason to look, because the box they typed it into
/// accepted it.
/// </summary>
public class VocabularyStrictParseTests
{
    [Fact]
    public void A_line_with_no_separator_is_reported_with_its_number()
    {
        var text = string.Join(Environment.NewLine, "see sharp => C#", "this line is wrong", "jupitor => Jupitor Studio");

        var parse = VocabularyFormat.ParseReplacementsStrict(text);

        Assert.False(parse.Ok);
        Assert.Equal([2], parse.RejectedLines);

        // The good lines either side still come through: one bad line is not a reason to lose the rest.
        Assert.Equal(2, parse.Entries.Length);
    }

    [Fact]
    public void A_half_that_is_empty_is_reported_rather_than_dropped()
    {
        var parse = VocabularyFormat.ParseReplacementsStrict(string.Join(Environment.NewLine, "=> C#", "see sharp =>"));

        Assert.Equal([1, 2], parse.RejectedLines);
        Assert.Empty(parse.Entries);
    }

    [Fact]
    public void Blank_lines_are_not_rejected()
    {
        var text = string.Join(Environment.NewLine, "see sharp => C#", string.Empty, "   ", "jupitor => Jupitor Studio");

        var parse = VocabularyFormat.ParseReplacementsStrict(text);

        Assert.True(parse.Ok);
        Assert.Equal(2, parse.Entries.Length);
    }

    /// <summary>The number has to match the line the user is looking at, whatever their editor uses.</summary>
    [Fact]
    public void Line_numbers_count_blank_lines_and_survive_crlf()
    {
        var parse = VocabularyFormat.ParseReplacementsStrict("ok => fine\r\n\r\nbroken\r\nalso ok => fine");

        Assert.Equal([3], parse.RejectedLines);
    }

    [Fact]
    public void The_forgiving_parse_still_drops_what_the_strict_one_reports()
    {
        var text = string.Join(Environment.NewLine, "see sharp => C#", "nonsense");

        Assert.Single(VocabularyFormat.ParseReplacements(text));
    }

    /// <summary>
    /// A snippet keeps the spacing it was written with, now that snippet text is typed exactly. The one
    /// space after the separator is the format's own punctuation and is not part of the text.
    /// </summary>
    [Fact]
    public void A_snippets_leading_whitespace_survives_the_format()
    {
        Snippet[] snippets = [new("indented", "    four spaces in")];

        var parsed = VocabularyFormat.ParseSnippets(VocabularyFormat.Format(snippets));

        Assert.Equal(snippets, parsed);
    }

    [Fact]
    public void A_snippet_holding_the_separator_in_its_text_survives_a_round_trip()
    {
        Snippet[] snippets = [new("arrow", "a => b")];

        Assert.Equal(snippets, VocabularyFormat.ParseSnippets(VocabularyFormat.Format(snippets)));
    }

    [Fact]
    public void A_replacement_holding_the_separator_on_the_right_survives_a_round_trip()
    {
        TextReplacement[] replacements = [new("arrow", "=>")];

        Assert.Equal(replacements, VocabularyFormat.ParseReplacements(VocabularyFormat.Format(replacements)));
    }

    /// <summary>
    /// Snippet text is normalised to the platform newline wherever it enters, so the round trip is the
    /// identity from then on. The format cannot tell one kind of line break from another, so the only
    /// way this holds is if everything stored is already in one form.
    /// </summary>
    [Fact]
    public void A_snippet_normalised_on_the_way_in_round_trips_unchanged()
    {
        var edited = VocabularyEdit.UpsertSnippet([], null, "bug template", "Steps:\n1.\n2.");

        Assert.True(edited.Ok);
        Assert.Equal(VocabularyFormat.ParseSnippets(VocabularyFormat.Format(edited.Snippets)), edited.Snippets);
    }

    [Fact]
    public void Spellings_come_one_per_line_or_comma_separated()
    {
        var parse = VocabularyFormat.ParseSpellingsStrict("TawkType, GitHub" + Environment.NewLine + "Jupitor Studio");

        Assert.True(parse.Ok);
        Assert.Equal(["TawkType", "GitHub", "Jupitor Studio"], parse.Entries);
    }

    /// <summary>Somebody has pasted the wrong list into the wrong box, and would get a spelling nobody wants.</summary>
    [Fact]
    public void A_spelling_line_carrying_the_replacement_separator_is_reported()
    {
        var parse = VocabularyFormat.ParseSpellingsStrict("TawkType" + Environment.NewLine + "see sharp => C#");

        Assert.Equal([2], parse.RejectedLines);
        Assert.Equal(["TawkType"], parse.Entries);
    }
}
