using TawkType.Core.Models;

namespace TawkType.Core.History;

/// <summary>
/// Totals across the dictation log, for the stats tiles on the Settings home page.
/// Pure and allocation-light so it can be recomputed whenever the log changes.
/// </summary>
public sealed record DictationStats(
    int Count,
    TimeSpan SpeechDuration,
    int Words,
    int Characters)
{
    /// <summary>
    /// Typing speed assumed when working out <see cref="TimeSaved"/>. The average adult types around
    /// 40 wpm; using a deliberately modest number keeps the figure honest rather than flattering.
    /// </summary>
    public const double AssumedTypingWordsPerMinute = 40;

    public static DictationStats Empty { get; } = new(0, TimeSpan.Zero, 0, 0);

    /// <summary>Words actually spoken per minute of speech. Zero when nothing has been dictated.</summary>
    public int WordsPerMinute => SpeechDuration.TotalMinutes > 0
        ? (int)Math.Round(Words / SpeechDuration.TotalMinutes)
        : 0;

    /// <summary>
    /// How much longer these words would have taken to type by hand. Never negative: dictating slower
    /// than you type is possible, and reporting it as time lost would be a strange thing to show.
    /// </summary>
    public TimeSpan TimeSaved
    {
        get
        {
            var typing = TimeSpan.FromMinutes(Words / AssumedTypingWordsPerMinute);
            var saved = typing - SpeechDuration;
            return saved > TimeSpan.Zero ? saved : TimeSpan.Zero;
        }
    }

    public static DictationStats From(IEnumerable<DictationRecord> records)
    {
        var count = 0;
        var seconds = 0d;
        var words = 0;
        var characters = 0;

        foreach (var record in records)
        {
            count++;
            seconds += record.AudioSeconds;
            characters += record.FinalText.Length;
            words += CountWords(record.FinalText);
        }

        return new DictationStats(count, TimeSpan.FromSeconds(seconds), words, characters);
    }

    /// <summary>Whitespace-separated runs. Good enough for a stat tile, and it never allocates a split.</summary>
    public static int CountWords(string text)
    {
        var words = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }

    /// <summary>"12h 25m", "42m", "18s" — the shape the stat tiles want.</summary>
    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m" : $"{(int)span.TotalSeconds}s";
    }
}
