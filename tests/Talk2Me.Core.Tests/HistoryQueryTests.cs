using Talk2Me.Core.History;
using Talk2Me.Core.Models;

namespace Talk2Me.Core.Tests;

public class HistoryQueryTests
{
    private static DictationRecord Record(string raw, string final)
        => new() { RawText = raw, FinalText = final };

    [Fact]
    public void An_empty_query_keeps_everything()
    {
        DictationRecord[] records = [Record("a", "a"), Record("b", "b")];

        Assert.Equal(2, HistoryQuery.Filter(records, "   ").Count());
    }

    [Fact]
    public void Case_does_not_matter()
        => Assert.True(HistoryQuery.Matches(Record("", "The Deadline Is Tuesday"), "deadline"));

    /// <summary>
    /// The reason to search an old dictation is usually that it came out wrong, so the words the user
    /// remembers saying may only be in the raw transcript.
    /// </summary>
    [Fact]
    public void What_was_heard_is_searched_as_well_as_what_was_typed()
    {
        var record = Record("jupitor studio", "Jupiter Studio");

        Assert.True(HistoryQuery.Matches(record, "jupitor"));
        Assert.True(HistoryQuery.Matches(record, "Jupiter"));
    }

    [Fact]
    public void Every_word_has_to_appear_but_the_order_does_not()
    {
        var record = Record("", "the deadline is Tuesday");

        Assert.True(HistoryQuery.Matches(record, "tuesday deadline"));
        Assert.False(HistoryQuery.Matches(record, "tuesday wednesday"));
    }

    [Fact]
    public void A_word_can_match_part_of_a_longer_one()
        => Assert.True(HistoryQuery.Matches(Record("", "rescheduling"), "schedul"));
}
