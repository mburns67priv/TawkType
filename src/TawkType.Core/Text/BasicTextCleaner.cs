using System.Text.RegularExpressions;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;

namespace TawkType.Core.Text;

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
        => ValueTask.FromResult(Clean(rawTranscript, _settings.Current.ActiveModeOrDefault()));

    /// <summary>The old two-argument form, kept for callers that have no mode to hand.</summary>
    public static string Clean(string raw, bool removeFillers)
        => Clean(raw, new DictationMode { RemoveFillerWords = removeFillers });

    public static string Clean(string raw, DictationMode mode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        if (mode.RemoveFillerWords)
        {
            text = FillerPattern().Replace(text, DropUnlessShouting);
            text = DuplicatePunctuation().Replace(text, "$1");
        }

        text = Whitespace().Replace(text, " ").Trim();
        text = SpaceBeforePunctuation().Replace(text, "$1");
        text = text.TrimStart(',', ' ');

        if (mode.Capitalise && text.Length > 0 && char.IsLower(text[0]))
        {
            text = char.ToUpperInvariant(text[0]) + text[1..];
        }

        return text;
    }

    /// <summary>
    /// Keeps a match that arrived in capitals. "ER", "AH" and "UM" are acronyms and initialisms, not
    /// disfluencies — nobody says "um" in block capitals — so "The ER is open" kept its department.
    ///
    /// Only the all-capitals form is protected. "Um," at the start of a sentence is still a filler,
    /// and a single capital letter is not enough evidence of anything.
    /// </summary>
    private static string DropUnlessShouting(Match match)
    {
        var word = match.Value.TrimEnd(',');
        var shouting = word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));
        return shouting ? match.Value : string.Empty;
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
