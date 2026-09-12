using System.Text.RegularExpressions;
using TawkType.Core.Settings;

namespace TawkType.Core.Text;

/// <summary>Which way finished text was delivered, and why. One value per reason, so a log line can say.</summary>
public enum DeliveryRoute
{
    /// <summary>Typed character by character. The normal path for a sentence.</summary>
    Typed,

    /// <summary>Pasted because it has a line break, which typing would deliver as a Return keypress.</summary>
    PastedAsMultiline,

    /// <summary>Pasted because typing this much, one synthetic keystroke at a time, is slow.</summary>
    PastedAsLong,

    /// <summary>Pasted because the user set the injection mode to Paste.</summary>
    PastedByChoice,
}

/// <summary>
/// How finished text should reach the window the user is working in. Pure, so the rule that keeps
/// TawkType from pressing Return in someone's chat window or terminal can be tested.
/// </summary>
public static partial class Delivery
{
    /// <summary>Past this, typing character by character is slow enough to be worth the clipboard.</summary>
    public const int PasteThresholdChars = 400;

    /// <summary>
    /// Whether to paste rather than type. Multiline text always pastes, whatever the user chose:
    /// typing it means synthesising Return, and Return is not a character — it sends the message in a
    /// chat composer and runs the line in a terminal. A dictation should never be able to do that.
    /// </summary>
    public static bool ShouldPaste(string text, TextInjectionMode mode) => Route(text, mode) != DeliveryRoute.Typed;

    /// <summary>
    /// The same decision as <see cref="ShouldPaste"/>, keeping the reason.
    ///
    /// One function rather than two, because a log line that disagreed with what actually happened
    /// would be worse than no log line — and the threshold in particular is a number nobody can see
    /// from the outside. Order matters: the mode is checked first because choosing Paste is a
    /// deliberate instruction, and the line break next because it is the one that is not a preference.
    /// </summary>
    public static DeliveryRoute Route(string text, TextInjectionMode mode)
    {
        if (mode == TextInjectionMode.Paste)
        {
            return DeliveryRoute.PastedByChoice;
        }

        if (IsMultiline(text))
        {
            return DeliveryRoute.PastedAsMultiline;
        }

        return mode == TextInjectionMode.TypeUnicode || text.Length <= PasteThresholdChars
            ? DeliveryRoute.Typed
            : DeliveryRoute.PastedAsLong;
    }

    /// <summary>The route in words, for the log. Never shown in the UI — see gotcha 42 if it ever is.</summary>
    public static string Describe(DeliveryRoute route) => route switch
    {
        DeliveryRoute.PastedAsMultiline => "pasted: it has a line break, and typing one means pressing Return",
        DeliveryRoute.PastedAsLong => $"pasted: over {PasteThresholdChars} characters",
        DeliveryRoute.PastedByChoice => "pasted: the injection mode is set to Paste",
        _ => "typed",
    };

    public static bool IsMultiline(string text) => text.Contains('\n') || text.Contains('\r');

    /// <summary>
    /// Folds line breaks into single spaces. The last line of defence for the typing path: if multiline
    /// text ever reaches it anyway, losing the layout is a far smaller harm than sending a message.
    /// </summary>
    public static string SingleLine(string text) => LineBreaks().Replace(text, " ").Trim();

    // \s on both sides so a blank line between paragraphs folds to one space, not two.
    [GeneratedRegex(@"\s*(?:\r\n|\r|\n)\s*")]
    private static partial Regex LineBreaks();
}
