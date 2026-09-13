using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// The comparison the Settings window uses to notice that the vocabulary on disk has moved under it.
/// It has to be exact in both directions: a false "same" loses a replacement the user just taught,
/// and a false "different" nags them about a change nobody made.
/// </summary>
public class VocabularySameTests
{
    [Fact]
    public void An_empty_vocabulary_matches_another_empty_one()
    {
        Assert.True(VocabularyRules.Same(new VocabularySettings(), new VocabularySettings()));
    }

    [Fact]
    public void A_clone_matches_what_it_was_cloned_from()
    {
        var vocabulary = Full();

        Assert.True(VocabularyRules.Same(vocabulary, vocabulary.Clone()));
    }

    [Fact]
    public void An_added_replacement_is_a_difference()
    {
        var before = Full();
        var after = Full();
        after.Replacements = [.. after.Replacements, new TextReplacement("dot net", ".NET")];

        Assert.False(VocabularyRules.Same(before, after));
    }

    [Fact]
    public void A_changed_replacement_is_a_difference()
    {
        var before = Full();
        var after = Full();
        after.Replacements = [new TextReplacement("see sharp", "C sharp")];

        Assert.False(VocabularyRules.Same(before, after));
    }

    [Fact]
    public void A_removed_spelling_is_a_difference()
    {
        var before = Full();
        var after = Full();
        after.Spellings = [];

        Assert.False(VocabularyRules.Same(before, after));
    }

    [Fact]
    public void A_changed_snippet_is_a_difference()
    {
        var before = Full();
        var after = Full();
        after.Snippets = [new Snippet("sign off", "Best,")];

        Assert.False(VocabularyRules.Same(before, after));
    }

    /// <summary>
    /// Order is part of the vocabulary, not a detail of how it is stored: the first entry wins on a
    /// duplicate phrase, so reordering changes which rule applies.
    /// </summary>
    [Fact]
    public void Reordering_is_a_difference()
    {
        var before = new VocabularySettings { Spellings = ["TawkType", "GitHub"] };
        var after = new VocabularySettings { Spellings = ["GitHub", "TawkType"] };

        Assert.False(VocabularyRules.Same(before, after));
    }

    [Fact]
    public void Case_matters_in_a_spelling()
    {
        var before = new VocabularySettings { Spellings = ["GitHub"] };
        var after = new VocabularySettings { Spellings = ["github"] };

        Assert.False(VocabularyRules.Same(before, after));
    }

    [Fact]
    public void Null_matches_only_null()
    {
        Assert.True(VocabularyRules.Same(null, null));
        Assert.False(VocabularyRules.Same(null, new VocabularySettings()));
        Assert.False(VocabularyRules.Same(new VocabularySettings(), null));
    }

    private static VocabularySettings Full() => new()
    {
        Spellings = ["TawkType", "Jupitor Studio"],
        Replacements = [new TextReplacement("see sharp", "C#")],
        Snippets = [new Snippet("sign off", "Best," + "\n" + "Michael")],
    };
}
