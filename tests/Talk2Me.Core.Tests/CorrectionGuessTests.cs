using Talk2Me.Core.Text;

namespace Talk2Me.Core.Tests;

public class CorrectionGuessTests
{
    /// <summary>
    /// The whole point: a rule saved for a whole sentence only ever fires on that sentence again.
    /// </summary>
    [Fact]
    public void Only_the_words_that_changed_are_offered()
    {
        var guess = CorrectionGuess.Between(
            "send it to jupitor studio please",
            "Send it to Jupiter Studio please.");

        Assert.Equal("jupitor studio", guess.Heard);
        Assert.Equal("Jupiter Studio", guess.Typed);
    }

    [Fact]
    public void A_correction_at_the_start_keeps_its_tail_off()
    {
        var guess = CorrectionGuess.Between("jupitor is open", "Jupitor Studio is open");

        Assert.Equal("jupitor", guess.Heard);
        Assert.Equal("Jupitor Studio", guess.Typed);
    }

    [Fact]
    public void A_correction_at_the_end_keeps_its_head_off()
    {
        var guess = CorrectionGuess.Between("meet me on tuesday", "meet me on Thursday");

        Assert.Equal("tuesday", guess.Heard);
        Assert.Equal("Thursday", guess.Typed);
    }

    /// <summary>Punctuation added by cleanup, and a sentence capital, are not what is being corrected.</summary>
    [Fact]
    public void Added_punctuation_alone_is_not_a_correction()
    {
        var guess = CorrectionGuess.Between("the er is open until nine", "The ER is open until nine.");

        Assert.Equal("er", guess.Heard);
        Assert.Equal("ER", guess.Typed);
    }

    /// <summary>
    /// A pure insertion leaves nothing on the heard side to match on, so there is no pair to save.
    /// Offer the whole thing rather than a rule that would match everywhere.
    /// </summary>
    [Fact]
    public void A_pure_insertion_falls_back_to_the_whole_text()
    {
        var guess = CorrectionGuess.Between("meet me tuesday", "meet me on tuesday");

        Assert.Equal("meet me tuesday", guess.Heard);
        Assert.Equal("meet me on tuesday", guess.Typed);
    }

    [Fact]
    public void Identical_text_falls_back_to_the_whole_text()
    {
        var guess = CorrectionGuess.Between("same words", "same words");

        Assert.Equal("same words", guess.Heard);
        Assert.Equal("same words", guess.Typed);
    }

    [Fact]
    public void Empty_input_does_not_throw()
    {
        var guess = CorrectionGuess.Between("", "something");

        Assert.Equal(string.Empty, guess.Heard);
        Assert.Equal("something", guess.Typed);
    }
}
