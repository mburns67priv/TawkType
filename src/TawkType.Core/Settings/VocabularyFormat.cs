namespace TawkType.Core.Settings;

/// <summary>
/// Replacements and snippets as plain text, one per line: <c>heard =&gt; typed</c>.
///
/// This is the bulk path, not the only one. Entries are added and edited through dialogs that know the
/// shape of an entry, so nobody has to remember a separator — but these lists are also written in
/// bursts, pasted from somewhere, sorted and kept in a note, and plain text is the only form that
/// allows any of that. The two are views onto one list.
///
/// Snippet text is a single line here, so <c>\n</c> is written literally and turned into a line break
/// on the way in.
/// </summary>
public static class VocabularyFormat
{
    public const string Separator = "=>";

    public static string Format(IEnumerable<TextReplacement> replacements)
        => string.Join(
            Environment.NewLine,
            replacements.Select(r => $"{r.From} {Separator} {r.To}"));

    public static string Format(IEnumerable<Snippet> snippets)
        => string.Join(
            Environment.NewLine,
            snippets.Select(s => $"{s.Trigger} {Separator} {Escape(s.Text)}"));

    /// <summary>One spelling per line. A comma-separated list still pastes in, which is how they arrive.</summary>
    public static string FormatSpellings(IEnumerable<string> spellings)
        => string.Join(Environment.NewLine, spellings);

    public static TextReplacement[] ParseReplacements(string? text) => ParseReplacementsStrict(text).Entries;

    public static Snippet[] ParseSnippets(string? text) => ParseSnippetsStrict(text).Entries;

    public static string[] ParseSpellings(string? text) => ParseSpellingsStrict(text).Entries;

    /// <summary>As <see cref="ParseReplacements"/>, but naming the lines it could not use.</summary>
    public static VocabularyParse<TextReplacement> ParseReplacementsStrict(string? text)
        => Parse(text, pair => new TextReplacement(pair.Left, pair.Right.Trim()));

    /// <summary>
    /// As <see cref="ParseSnippets"/>, but naming the lines it could not use.
    ///
    /// The snippet's text keeps the spacing it was written with, apart from the single space that
    /// follows the separator by convention. A snippet is typed exactly as written, and that now
    /// includes its whitespace — see <c>SnippetExactnessTests</c>.
    /// </summary>
    public static VocabularyParse<Snippet> ParseSnippetsStrict(string? text)
        => Parse(text, pair => new Snippet(pair.Left, Unescape(pair.Right)));

    /// <summary>
    /// Spellings, one per line or comma-separated. A line cannot really be malformed — it is a word —
    /// so the only thing rejected is a line carrying the replacement separator, which means somebody
    /// has pasted the wrong list into the wrong box and would otherwise get a spelling nobody wants.
    /// </summary>
    public static VocabularyParse<string> ParseSpellingsStrict(string? text)
    {
        var entries = new List<string>();
        var rejected = new List<int>();

        foreach (var (line, number) in Lines(text))
        {
            if (line.Contains(Separator, StringComparison.Ordinal))
            {
                rejected.Add(number);
                continue;
            }

            entries.AddRange(
                line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new VocabularyParse<string>([.. entries], [.. rejected]);
    }

    private static VocabularyParse<T> Parse<T>(string? text, Func<(string Left, string Right), T> make)
    {
        var entries = new List<T>();
        var rejected = new List<int>();

        foreach (var (line, number) in Lines(text))
        {
            var at = line.IndexOf(Separator, StringComparison.Ordinal);
            if (at < 0)
            {
                rejected.Add(number);
                continue;
            }

            var left = line[..at].Trim();

            // One space after the separator is the format's own punctuation; anything beyond that is
            // the user's, and a snippet keeps it.
            var right = line[(at + Separator.Length)..];
            right = right.StartsWith(' ') ? right[1..] : right;

            if (left.Length == 0 || right.Trim().Length == 0)
            {
                rejected.Add(number);
                continue;
            }

            entries.Add(make((left, right)));
        }

        return new VocabularyParse<T>([.. entries], [.. rejected]);
    }

    /// <summary>
    /// Every line that holds something, with its 1-based number counted over the whole text — blank
    /// lines included, because the number has to match the line the user is looking at.
    /// </summary>
    private static IEnumerable<(string Line, int Number)> Lines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0)
            {
                yield return (lines[i], i + 1);
            }
        }
    }

    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    private static string Unescape(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                result.Append(text[i]);
                continue;
            }

            switch (text[i + 1])
            {
                case 'n':
                    // The platform newline, not a bare line feed. This format cannot tell the two
                    // apart — Escape maps CRLF, LF and CR onto the same escape — so the round trip can
                    // only be the identity if every snippet is already in one form. That is what
                    // Snippets.Normalise does, at every point text enters, and this has to agree with
                    // it. Bare LF was tried here and it is the wrong half to change: multiline
                    // snippets are delivered through the clipboard, where Windows expects CRLF.
                    result.Append(Environment.NewLine);
                    i++;
                    break;
                case '\\':
                    result.Append('\\');
                    i++;
                    break;
                default:
                    result.Append(text[i]);
                    break;
            }
        }

        return result.ToString();
    }
}
