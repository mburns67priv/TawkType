using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

public class CaretFitTests
{
    private static CaretContext At(string before, string after = "", bool selection = false)
        => new(before, after, selection);

    /// <summary>
    /// A control that tells us nothing has to behave exactly as it did before any of this existed.
    /// Guessing into a destination we cannot see is how visible rubbish ends up in someone's document.
    /// </summary>
    [Fact]
    public void An_unreadable_destination_behaves_exactly_as_before()
    {
        Assert.Equal("Hello. ", CaretFit.Fit("Hello.", CaretContext.Unknown, appendTrailingSpace: true));
        Assert.Equal("Hello.", CaretFit.Fit("Hello.", CaretContext.Unknown, appendTrailingSpace: false));
    }

    [Fact]
    public void A_separating_space_is_added_after_a_word()
        => Assert.Equal(" world", CaretFit.Fit("world", At("hello"), appendTrailingSpace: false));

    [Fact]
    public void No_space_is_added_where_one_is_already_there()
    {
        Assert.Equal("world", CaretFit.Fit("world", At("hello "), appendTrailingSpace: false));
        Assert.Equal("world", CaretFit.Fit("world", At(""), appendTrailingSpace: false));
    }

    [Theory]
    [InlineData("(")]
    [InlineData("\"")]
    [InlineData("/")]
    [InlineData("-")]
    public void Nothing_is_wedged_in_after_something_that_opens_a_phrase(string before)
        => Assert.Equal("world", CaretFit.Fit("world", At(before), appendTrailingSpace: false));

    [Fact]
    public void A_trailing_space_is_not_doubled()
        => Assert.Equal(" world", CaretFit.Fit("world", At("hello", " and then"), appendTrailingSpace: true));

    [Fact]
    public void A_trailing_space_is_added_at_the_end_of_a_field()
        => Assert.Equal("world ", CaretFit.Fit("world", At("", ""), appendTrailingSpace: true));

    /// <summary>"the deadline , which" is worse than no space at all.</summary>
    [Fact]
    public void No_space_is_pushed_in_front_of_punctuation()
        => Assert.Equal(" Tuesday", CaretFit.Fit("Tuesday", At("the deadline is", ", which"), appendTrailingSpace: true));

    // ----- capitalisation -----

    [Fact]
    public void A_capital_talk2me_added_is_undone_mid_sentence()
        => Assert.Equal(" and then we left", CaretFit.Fit("And then we left", At("we arrived"), false, mayLowerFirst: true));

    [Fact]
    public void A_capital_is_kept_at_the_start_of_a_sentence()
    {
        Assert.Equal(" And then we left", CaretFit.Fit("And then we left", At("We arrived."), false, mayLowerFirst: true));
        Assert.Equal("And then we left", CaretFit.Fit("And then we left", At(""), false, mayLowerFirst: true));
    }

    /// <summary>A new paragraph starts a sentence just as a full stop does.</summary>
    [Fact]
    public void A_capital_is_kept_after_a_line_break()
        => Assert.Equal("And then we left", CaretFit.Fit("And then we left", At("we arrived\n"), false, mayLowerFirst: true));

    /// <summary>
    /// The whole safety of this rests on only ever undoing a capital Talk2Me itself added. A name the
    /// recogniser produced must survive being dictated mid-sentence.
    /// </summary>
    [Fact]
    public void A_capital_the_recogniser_produced_is_never_touched()
        => Assert.Equal(" Michael is here", CaretFit.Fit("Michael is here", At("I think"), false, mayLowerFirst: false));

    [Fact]
    public void Cleanups_own_capital_is_recognised()
    {
        Assert.True(CaretFit.WasCapitalisedByCleanup("and then we left", "And then we left"));
        Assert.False(CaretFit.WasCapitalisedByCleanup("Michael is here", "Michael is here"));
        Assert.False(CaretFit.WasCapitalisedByCleanup("", "And then"));
        Assert.False(CaretFit.WasCapitalisedByCleanup("9 o'clock", "9 o'clock"));
    }

    /// <summary>A rewrite can change the first word entirely; that is not a capital we added.</summary>
    [Fact]
    public void A_different_first_word_after_a_rewrite_is_not_our_capital()
        => Assert.False(CaretFit.WasCapitalisedByCleanup("and then we left", "Then we left"));

    // ----- selections -----

    /// <summary>
    /// Replacing a selection is not inserting at a caret: the text either side is already spaced for
    /// the words being replaced, so anything added doubles up.
    /// </summary>
    [Fact]
    public void Replacing_a_selection_adds_no_trailing_space()
        => Assert.Equal("Thursday", CaretFit.Fit("Thursday", At("meet me on ", " at noon", selection: true), true));

    [Fact]
    public void Replacing_a_selection_never_lowercases_it()
        => Assert.Equal("Thursday", CaretFit.Fit("Thursday", At("meet me on ", "", selection: true), false, mayLowerFirst: true));

    [Fact]
    public void Empty_text_is_left_alone()
        => Assert.Equal(string.Empty, CaretFit.Fit(string.Empty, At("hello"), appendTrailingSpace: true));
}
