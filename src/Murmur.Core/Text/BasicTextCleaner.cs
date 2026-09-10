using System.Text.RegularExpressions;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Text;

/// <summary>
/// Cheap, deterministic cleanup: strips filler words, normalises whitespace, capitalises the first letter.
/// The LLM-backed cleaner that adds tone/format rewriting will sit behind the same interface.
/// </summary>
public sealed partial class BasicTextCleaner : ITextCleaner
{
    private readonly ISettingsProvider _settings;

    public BasicTextCleaner(ISettingsProvider settings)
    {
        _settings = settings;
    }

    public ValueTask<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Clean(rawTranscript, _settings.Current.RemoveFillerWords));

    public static string Clean(string raw, bool removeFillers)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        if (removeFillers)
        {
            text = FillerPattern().Replace(text, string.Empty);
            text = DuplicatePunctuation().Replace(text, "$1");
        }

        text = Whitespace().Replace(text, " ").Trim();
        text = SpaceBeforePunctuation().Replace(text, "$1");
        text = text.TrimStart(',', ' ');

        if (text.Length > 0 && char.IsLower(text[0]))
        {
            text = char.ToUpperInvariant(text[0]) + text[1..];
        }

        return text;
    }

    // "um", "uh", "er", "erm", "hmm", "ah" as whole words, with any directly following comma.
    [GeneratedRegex(@"(?<!\w)(?:u+m+|u+h+|e+r+m*|h+m+|a+h+)(?!\w),?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FillerPattern();

    // ", ," or ", ." left behind after filler removal: keep the last mark.
    [GeneratedRegex(@"[,.!?;:]\s*(?=[,.!?;:])")]
    private static partial Regex DuplicatePunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s+([,.!?;:])")]
    private static partial Regex SpaceBeforePunctuation();
}
