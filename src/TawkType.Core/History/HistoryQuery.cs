using TawkType.Core.Models;

namespace TawkType.Core.History;

/// <summary>
/// Filtering the dictation log by what was said.
///
/// Searches what was typed <i>and</i> what was heard, because the reason to search an old dictation is
/// usually that it came out wrong: the words the user remembers saying may only exist in the raw
/// transcript, and the words they can see may only exist in the cleaned one.
///
/// Every word has to appear, in any order and anywhere in either text. Ordinary substring matching per
/// word rather than anything cleverer, since the corpus is a few hundred short entries and anyone
/// searching it already knows roughly what they said.
/// </summary>
public static class HistoryQuery
{
    public static IEnumerable<DictationRecord> Filter(IEnumerable<DictationRecord> records, string? query)
    {
        var words = Words(query);
        return words.Length == 0 ? records : records.Where(record => Matches(record, words));
    }

    public static bool Matches(DictationRecord record, string? query)
    {
        var words = Words(query);
        return words.Length == 0 || Matches(record, words);
    }

    private static bool Matches(DictationRecord record, string[] words)
        => words.All(word =>
            record.FinalText.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || record.RawText.Contains(word, StringComparison.CurrentCultureIgnoreCase));

    private static string[] Words(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
