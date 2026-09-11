using System.Globalization;

namespace TawkType.Core.Text;

/// <summary>
/// Fits finished text to the place it is about to be typed: the space in front of it, the space after
/// it, and whether its first letter should still be a capital.
///
/// The rule throughout is that not knowing must cost nothing. A control that exposes no text produces
/// <see cref="CaretContext.Unknown"/> and gets precisely the behaviour TawkType had before this existed
/// — the configured trailing space and nothing else. Guessing wrongly here types visible rubbish into
/// someone's document, so every rule below only fires on evidence.
/// </summary>
public static class CaretFit
{
    /// <summary>Nothing needs a separating space after one of these.</summary>
    private const string OpensSomething = "([{“‘\"'/\\-—";

    /// <summary>A dictation that starts after one of these is starting a new sentence.</summary>
    private const string EndsSentence = ".!?:…";

    public static string Fit(string text, CaretContext context, bool appendTrailingSpace, bool mayLowerFirst = false)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (context.IsUnknown)
        {
            // Exactly the old behaviour, for a destination that tells us nothing about itself.
            return appendTrailingSpace ? text + " " : text;
        }

        if (mayLowerFirst && IsMidSentence(context))
        {
            text = char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..];
        }

        if (NeedsSpaceBefore(context.Before))
        {
            text = " " + text;
        }

        if (appendTrailingSpace && NeedsSpaceAfter(context))
        {
            text += " ";
        }

        return text;
    }

    /// <summary>
    /// True when cleanup is the reason the first letter is a capital — the raw transcript had it
    /// lower and the finished text has it upper, and it is otherwise the same letter.
    ///
    /// This is what makes lowercasing a continuation safe. Undoing a capital TawkType added is
    /// harmless; lowercasing one the recogniser produced would turn someone's name into a common
    /// noun, and no amount of context can tell those apart after the fact.
    /// </summary>
    public static bool WasCapitalisedByCleanup(string? raw, string? finished)
    {
        var rawFirst = FirstLetter(raw);
        var finishedFirst = FirstLetter(finished);

        return rawFirst is { } from && finishedFirst is { } to
            && char.IsLower(from)
            && char.IsUpper(to)
            && char.ToLower(to, CultureInfo.CurrentCulture) == from;
    }

    /// <summary>
    /// Mid-sentence means there is something before the caret and it did not end a sentence. A line
    /// break counts as a fresh start: a new paragraph takes a capital like a new sentence does.
    /// </summary>
    private static bool IsMidSentence(CaretContext context)
    {
        if (context.HasSelection || context.Before is not { } before)
        {
            return false;
        }

        var trimmed = before.TrimEnd(' ', '\t');
        if (trimmed.Length == 0)
        {
            return false;
        }

        var last = trimmed[^1];
        return last is not ('\n' or '\r') && !EndsSentence.Contains(last);
    }

    /// <summary>
    /// A space is needed when there is something immediately before the caret that is not already a
    /// space, a line break, or something that opens a phrase.
    /// </summary>
    private static bool NeedsSpaceBefore(string? before)
        => before is { Length: > 0 }
            && !char.IsWhiteSpace(before[^1])
            && !OpensSomething.Contains(before[^1]);

    /// <summary>
    /// Not into a space that is already there, and not in front of punctuation that belongs to the
    /// sentence being joined — "the deadline ," is worse than no space at all.
    /// </summary>
    private static bool NeedsSpaceAfter(CaretContext context)
    {
        if (context.HasSelection)
        {
            // Replacing a selection: the text on both sides is already spaced for the words that were
            // there, so anything added here doubles up.
            return false;
        }

        if (context.After is not { Length: > 0 } after)
        {
            return true;
        }

        return !char.IsWhiteSpace(after[0]) && !",.!?;:)]}".Contains(after[0]);
    }

    private static char? FirstLetter(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var first = text.TrimStart();
        return first.Length == 0 || !char.IsLetter(first[0]) ? null : first[0];
    }
}
