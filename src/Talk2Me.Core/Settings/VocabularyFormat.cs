namespace Talk2Me.Core.Settings;

/// <summary>
/// Replacements and snippets as plain text, one per line: <c>heard =&gt; typed</c>.
///
/// A text box rather than a grid of rows with add and remove buttons. These lists are edited rarely
/// and in bursts — usually pasted from somewhere, or corrected after a bad dictation — and plain text
/// can be selected, sorted, diffed and kept in a note somewhere, which a grid cannot.
///
/// Snippet text is a single line here, so <c>\n</c> is written literally and turned into a line break
/// on the way in. Anything without a separator, or with an empty half, is dropped rather than saved as
/// a rule that would match everything.
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

    public static TextReplacement[] ParseReplacements(string? text)
        => Pairs(text).Select(pair => new TextReplacement(pair.Left, pair.Right)).ToArray();

    public static Snippet[] ParseSnippets(string? text)
        => Pairs(text).Select(pair => new Snippet(pair.Left, Unescape(pair.Right))).ToArray();

    private static IEnumerable<(string Left, string Right)> Pairs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf(Separator, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var left = line[..at].Trim();
            var right = line[(at + Separator.Length)..].Trim();

            if (left.Length > 0 && right.Length > 0)
            {
                yield return (left, right);
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
