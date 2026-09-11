using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public class VocabularyFormatTests
{
    [Fact]
    public void A_replacement_survives_a_round_trip()
    {
        TextReplacement[] replacements = [new("tawk type", "TawkType"), new("jupitor", "Jupitor Studio")];

        Assert.Equal(replacements, VocabularyFormat.ParseReplacements(VocabularyFormat.Format(replacements)));
    }

    [Fact]
    public void A_multiline_snippet_survives_a_round_trip()
    {
        Snippet[] snippets = [new("bug template", "Steps:" + Environment.NewLine + "1." + Environment.NewLine + "2.")];

        var parsed = VocabularyFormat.ParseSnippets(VocabularyFormat.Format(snippets));

        Assert.Equal(snippets, parsed);
    }

    /// <summary>The saved text stays on one line in the box, so the line breaks have to be written out.</summary>
    [Fact]
    public void A_snippet_with_line_breaks_is_one_line_in_the_box()
    {
        Snippet[] snippets = [new("two lines", "first" + Environment.NewLine + "second")];

        Assert.Single(VocabularyFormat.Format(snippets).Split(Environment.NewLine));
    }

    [Theory]
    [InlineData("no separator here")]
    [InlineData("=> nothing on the left")]
    [InlineData("nothing on the right =>")]
    [InlineData("")]
    public void A_line_that_is_not_a_pair_is_dropped(string line)
    {
        Assert.Empty(VocabularyFormat.ParseReplacements(line));
    }

    [Fact]
    public void Spacing_around_the_separator_does_not_matter()
    {
        var parsed = VocabularyFormat.ParseReplacements("tawk type=>TawkType");

        Assert.Equal([new TextReplacement("tawk type", "TawkType")], parsed);
    }

    [Fact]
    public void A_vocabulary_file_survives_a_round_trip()
    {
        var vocabulary = new VocabularySettings
        {
            Spellings = ["TawkType"],
            Replacements = [new TextReplacement("tawk type", "TawkType")],
            Snippets = [new Snippet("my signature", "Michael Burns")],
        };

        var read = VocabularyFile.Read(VocabularyFile.Write(vocabulary));

        Assert.NotNull(read);
        Assert.Equal(vocabulary.Spellings, read!.Spellings);
        Assert.Equal(vocabulary.Replacements, read.Replacements);
        Assert.Equal(vocabulary.Snippets, read.Snippets);
    }

    /// <summary>
    /// Valid JSON that carries none of the three lists is somebody else's file. Importing it as an
    /// empty vocabulary would quietly delete the user's own.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"somethingElse\":1}")]
    [InlineData("{\"spellings\":[],\"replacements\":[],\"snippets\":[]}")]
    public void A_file_with_nothing_in_it_is_not_a_vocabulary(string json)
    {
        Assert.Null(VocabularyFile.Read(json));
    }

    [Fact]
    public void Half_written_entries_are_dropped_on_import()
    {
        var json = "{\"spellings\":[\"TawkType\",\"\",\"  \"],"
            + "\"replacements\":[{\"from\":\"a\",\"to\":\"\"}],"
            + "\"snippets\":[{\"trigger\":\"\",\"text\":\"orphan\"}]}";

        var read = VocabularyFile.Read(json);

        Assert.NotNull(read);
        Assert.Equal(["TawkType"], read!.Spellings);
        Assert.Empty(read.Replacements);
        Assert.Empty(read.Snippets);
    }
}
