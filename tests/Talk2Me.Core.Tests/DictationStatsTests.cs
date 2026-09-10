using Talk2Me.Core.History;
using Talk2Me.Core.Models;

namespace Talk2Me.Core.Tests;

public sealed class DictationStatsTests
{
    private static DictationRecord Record(string text, double seconds) => new()
    {
        FinalText = text,
        RawText = text,
        AudioSeconds = seconds,
    };

    [Fact]
    public void Totals_every_record()
    {
        var stats = DictationStats.From([
            Record("hello there world", 6),
            Record("second one", 4),
        ]);

        Assert.Equal(2, stats.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), stats.SpeechDuration);
        Assert.Equal(5, stats.Words);
        Assert.Equal("hello there world".Length + "second one".Length, stats.Characters);
    }

    [Fact]
    public void Empty_log_reports_zeroes_rather_than_dividing_by_zero()
    {
        var stats = DictationStats.From([]);

        Assert.Equal(0, stats.Count);
        Assert.Equal(0, stats.WordsPerMinute);
        Assert.Equal(TimeSpan.Zero, stats.TimeSaved);
    }

    [Fact]
    public void Words_per_minute_is_words_over_speech_time()
    {
        // 30 words in 30 seconds is 60 wpm.
        var text = string.Join(' ', Enumerable.Repeat("word", 30));
        var stats = DictationStats.From([Record(text, 30)]);

        Assert.Equal(60, stats.WordsPerMinute);
    }

    [Fact]
    public void Time_saved_is_typing_time_minus_speaking_time()
    {
        // 40 words is one minute of typing at the assumed 40 wpm; spoken in 20 s, so 40 s saved.
        var text = string.Join(' ', Enumerable.Repeat("word", 40));
        var stats = DictationStats.From([Record(text, 20)]);

        Assert.Equal(TimeSpan.FromSeconds(40), stats.TimeSaved);
    }

    [Fact]
    public void Time_saved_never_goes_negative()
    {
        // Speaking slower than you type is possible; reporting it as time lost would be strange.
        var stats = DictationStats.From([Record("one two", 600)]);

        Assert.Equal(TimeSpan.Zero, stats.TimeSaved);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData("  leading and trailing  ", 3)]
    [InlineData("line\nbreaks\tcount", 3)]
    public void Counts_whitespace_separated_words(string text, int expected)
    {
        Assert.Equal(expected, DictationStats.CountWords(text));
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(90, "1m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(44700, "12h 25m")]
    public void Formats_durations_for_the_stat_tiles(int seconds, string expected)
    {
        Assert.Equal(expected, DictationStats.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }
}
