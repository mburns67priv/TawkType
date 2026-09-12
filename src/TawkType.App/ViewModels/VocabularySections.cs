using System.Windows;
using TawkType.Core.Settings;
using TawkType.Core.Text;
using TawkType.Desktop.Views;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// Names, products and acronyms, written the way they should come out.
///
/// The simplest of the three: one field, no separator, and the least harmful as a plain list — which
/// is why it was the last of the three to need a dialog at all.
/// </summary>
public sealed class SpellingSection(Func<VocabularySettings> vocabulary, Action<string?> changed)
    : VocabularySection(
        "spelling",
        "spellings",
        "Names, products and acronyms, written the way you want them typed. However they are capitalised when they are heard, they come out like this.",
        changed)
{
    public override string EmptyText =>
        "No spellings yet. Add the names TawkType keeps getting wrong and they will come out right from the next dictation.";

    public override string TextHint => "One per line.";

    public override string PrimaryHeader => "Word";

    public override string? SecondaryHeader => null;

    protected override IEnumerable<VocabularyRow> BuildRows()
        => vocabulary().Spellings.Select((word, i) => new VocabularyRow(this, i, word, null, null));

    protected override string FormatText() => VocabularyFormat.FormatSpellings(vocabulary().Spellings);

    protected override int[] TryCommit(string text)
    {
        var parse = VocabularyFormat.ParseSpellingsStrict(text);
        if (!parse.Ok)
        {
            return parse.RejectedLines;
        }

        Commit(parse.Entries);
        return [];
    }

    protected override bool ShowAddDialog(Window? owner) => Edit(owner, null, string.Empty);

    protected override bool ShowEditDialog(VocabularyRow row, Window? owner)
        => Edit(owner, row.Index, vocabulary().Spellings[row.Index]);

    protected override void RemoveAt(int index)
        => vocabulary().Spellings = [.. vocabulary().Spellings.Where((_, i) => i != index)];

    private bool Edit(Window? owner, int? editing, string word)
        => Show(
            new VocabularyEntryWindow(new VocabularyEntryPrompt(
                editing is null ? "Add a spelling" : "Edit a spelling",
                "Write the word exactly as you want it typed. TawkType matches it whatever the capitalisation, and types this.",
                "Word",
                "Example: Jupitor Studio",
                word,
                null,
                null,
                string.Empty,
                editing is null ? "Add spelling" : "Save changes",
                (first, _) =>
                {
                    var result = VocabularyEdit.UpsertSpelling(vocabulary().Spellings, editing, first);
                    if (result.Ok)
                    {
                        vocabulary().Spellings = result.Spellings;
                    }

                    return result.Problem;
                })),
            owner);

    private void Commit(string[] spellings)
    {
        var review = VocabularyRules.Clean(new VocabularySettings { Spellings = spellings });
        vocabulary().Spellings = review.Vocabulary.Spellings;
        Announce(review.Changed ? string.Join(" ", review.Notes) : null);
    }
}

/// <summary>Heard this, type that: the mishearings a spelling cannot reach.</summary>
public sealed class ReplacementSection(Func<VocabularySettings> vocabulary, Action<string?> changed)
    : VocabularySection(
        "replacement",
        "replacements",
        "For the mishearings a spelling cannot fix, where what you say and what you want are different words — say \"see sharp\", get C#.",
        changed)
{
    public override string EmptyText =>
        "No replacements yet. Add one when a dictation gives you the wrong words rather than the wrong spelling.";

    public override string TextHint => $"One per line, as heard {VocabularyFormat.Separator} typed.";

    public override string PrimaryHeader => "When you hear";

    public override string? SecondaryHeader => "Type this instead";

    protected override IEnumerable<VocabularyRow> BuildRows()
        => vocabulary().Replacements.Select((r, i) => new VocabularyRow(this, i, r.From, r.To, null));

    protected override string FormatText() => VocabularyFormat.Format(vocabulary().Replacements);

    protected override int[] TryCommit(string text)
    {
        var parse = VocabularyFormat.ParseReplacementsStrict(text);
        if (!parse.Ok)
        {
            return parse.RejectedLines;
        }

        var review = VocabularyRules.Clean(new VocabularySettings { Replacements = parse.Entries });
        vocabulary().Replacements = review.Vocabulary.Replacements;
        Announce(review.Changed ? string.Join(" ", review.Notes) : null);
        return [];
    }

    protected override bool ShowAddDialog(Window? owner) => Edit(owner, null, new TextReplacement());

    protected override bool ShowEditDialog(VocabularyRow row, Window? owner)
        => Edit(owner, row.Index, vocabulary().Replacements[row.Index]);

    protected override void RemoveAt(int index)
        => vocabulary().Replacements = [.. vocabulary().Replacements.Where((_, i) => i != index)];

    private bool Edit(Window? owner, int? editing, TextReplacement entry)
        => Show(
            new VocabularyEntryWindow(new VocabularyEntryPrompt(
                editing is null ? "Add a replacement" : "Edit a replacement",
                "TawkType will make this change to every dictation from now on, whether or not the Claude pass is switched on.",
                "When you hear",
                "What you say, as the recogniser writes it down: see sharp",
                entry.From,
                "Type this instead",
                "What should be typed in its place: C#",
                entry.To,
                editing is null ? "Add replacement" : "Save changes",
                (first, second) =>
                {
                    var result = VocabularyEdit.UpsertReplacement(vocabulary().Replacements, editing, first, second);
                    if (result.Ok)
                    {
                        vocabulary().Replacements = result.Replacements;
                    }

                    return result.Problem;
                })),
            owner);
}

/// <summary>Saved text, spoken for. The list where the escape sequence used to live.</summary>
public sealed class SnippetSection(Func<VocabularySettings> vocabulary, Action<string?> changed)
    : VocabularySection(
        "snippet",
        "snippets",
        "Saved text you insert by saying \"insert\" and the trigger. Snippets are typed exactly as written and are never sent for rewriting.",
        changed)
{
    public override string EmptyText =>
        "No snippets yet. Add text you type often — a signature, an address, a template — and say \"insert\" and its trigger to have it typed for you.";

    public override string TextHint =>
        $"One per line, as trigger {VocabularyFormat.Separator} text, with \\n for a line break.";

    public override string PrimaryHeader => "Say";

    public override string? SecondaryHeader => "Types";

    protected override IEnumerable<VocabularyRow> BuildRows()
        => vocabulary().Snippets.Select((s, i) => new VocabularyRow(
            this,
            i,
            $"{PhraseBook.SnippetPrefix} {s.Trigger}",
            OneLine(s.Text),
            s.Text));

    protected override string FormatText() => VocabularyFormat.Format(vocabulary().Snippets);

    protected override int[] TryCommit(string text)
    {
        var parse = VocabularyFormat.ParseSnippetsStrict(text);
        if (!parse.Ok)
        {
            return parse.RejectedLines;
        }

        var review = VocabularyRules.Clean(new VocabularySettings { Snippets = parse.Entries });
        vocabulary().Snippets = review.Vocabulary.Snippets;
        Announce(review.Changed ? string.Join(" ", review.Notes) : null);
        return [];
    }

    protected override bool ShowAddDialog(Window? owner) => Edit(owner, null, new Snippet());

    protected override bool ShowEditDialog(VocabularyRow row, Window? owner)
        => Edit(owner, row.Index, vocabulary().Snippets[row.Index]);

    protected override void RemoveAt(int index)
        => vocabulary().Snippets = [.. vocabulary().Snippets.Where((_, i) => i != index)];

    /// <summary>
    /// The snippet's text on one line for the row. Line breaks become a separator rather than the
    /// literal escape: a row is an index, not an editor, and the full text is in the tooltip.
    /// </summary>
    private static string OneLine(string text)
        => string.Join(" · ", text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));

    private bool Edit(Window? owner, int? editing, Snippet entry)
        => Show(
            new SnippetWindow(
                editing is null ? "Add a snippet" : "Edit a snippet",
                entry.Trigger,
                entry.Text,
                editing is null ? "Add snippet" : "Save changes",
                (trigger, text) =>
                {
                    var result = VocabularyEdit.UpsertSnippet(vocabulary().Snippets, editing, trigger, text);
                    if (result.Ok)
                    {
                        vocabulary().Snippets = result.Snippets;
                    }

                    return result.Problem;
                }),
            owner);
}
