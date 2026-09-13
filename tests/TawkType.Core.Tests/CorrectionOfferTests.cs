using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

/// <summary>
/// What the history window is allowed to offer after an edit is saved. The bar is deliberately high:
/// an offer nobody wants is an interruption, and it appears at the moment the user has just finished
/// doing something else.
/// </summary>
public class CorrectionOfferTests
{
    [Fact]
    public void Offers_the_words_that_changed_not_the_sentence()
    {
        var offer = CorrectionOffer.For(
            "send it to jupyter studio please",
            "Send it to Jupitor Studio please.");

        Assert.NotNull(offer);
        Assert.Equal("jupyter studio", offer!.Value.Heard);
        Assert.Equal("Jupitor Studio", offer.Value.Typed);
    }

    [Fact]
    public void Offers_a_single_word_correction()
    {
        var offer = CorrectionOffer.For("the see sharp compiler", "the C# compiler");

        Assert.NotNull(offer);
        Assert.Equal("see sharp", offer!.Value.Heard);
        Assert.Equal("C#", offer.Value.Typed);
    }

    [Fact]
    public void Offers_nothing_when_the_edit_changed_nothing()
    {
        Assert.Null(CorrectionOffer.For("the same words", "the same words"));
    }

    [Fact]
    public void Offers_nothing_when_only_the_punctuation_moved()
    {
        Assert.Null(CorrectionOffer.For("thanks very much", "thanks very much."));
    }

    [Fact]
    public void Offers_nothing_when_there_is_no_transcript_to_compare()
    {
        Assert.Null(CorrectionOffer.For(null, "something"));
        Assert.Null(CorrectionOffer.For("  ", "something"));
        Assert.Null(CorrectionOffer.For("something", string.Empty));
    }

    /// <summary>
    /// The case this rule exists for. A rewrite shares no edges, so the guess falls back to both whole
    /// sentences — and a replacement built from those only ever fires on that exact sentence again.
    /// </summary>
    [Fact]
    public void Offers_nothing_for_a_wholesale_rewrite()
    {
        var offer = CorrectionOffer.For(
            "i think we should probably ship this on friday if the tests are green",
            "Let us hold the release until Monday and take another look at the clipboard work.");

        Assert.Null(offer);
    }

    [Fact]
    public void Offers_nothing_when_either_half_runs_past_the_word_limit()
    {
        var heard = string.Join(' ', Enumerable.Repeat("word", CorrectionOffer.MaxWords + 1));
        var typed = string.Join(' ', Enumerable.Repeat("other", CorrectionOffer.MaxWords + 1));

        Assert.Null(CorrectionOffer.For(heard, typed));
    }

    [Fact]
    public void Offers_a_pair_that_is_exactly_at_the_word_limit()
    {
        var heard = string.Join(' ', Enumerable.Repeat("word", CorrectionOffer.MaxWords));
        var typed = string.Join(' ', Enumerable.Repeat("other", CorrectionOffer.MaxWords));

        Assert.NotNull(CorrectionOffer.For(heard, typed));
    }

    [Fact]
    public void Offers_nothing_when_the_replacement_is_already_taught()
    {
        var existing = new[] { new TextReplacement("see sharp", "C#") };

        Assert.Null(CorrectionOffer.For("the see sharp compiler", "the C# compiler", existing));
    }

    /// <summary>Whatever case the rule was written in, it is the same rule.</summary>
    [Fact]
    public void Matches_an_existing_replacement_regardless_of_case()
    {
        var existing = new[] { new TextReplacement("See Sharp", "C#") };

        Assert.Null(CorrectionOffer.For("the see sharp compiler", "the C# compiler", existing));
    }

    [Fact]
    public void Still_offers_when_the_existing_rules_are_about_something_else()
    {
        var existing = new[] { new TextReplacement("dot net", ".NET") };

        Assert.NotNull(CorrectionOffer.For("the see sharp compiler", "the C# compiler", existing));
    }

    /// <summary>A capitalisation fix is exactly what a spelling or replacement is for.</summary>
    [Fact]
    public void Offers_a_correction_that_is_only_a_capital()
    {
        var offer = CorrectionOffer.For("we use postgres here", "we use Postgres here");

        Assert.NotNull(offer);
        Assert.Equal("postgres", offer!.Value.Heard);
        Assert.Equal("Postgres", offer.Value.Typed);
    }
}
