using System.Text.RegularExpressions;
using TawkType.Core.Settings;

namespace TawkType.Core.Text;

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
    public static bool ShouldPaste(string text, TextInjectionMode mode) => mode switch
    {
        TextInjectionMode.Paste => true,
        TextInjectionMode.TypeUnicode => IsMultiline(text),
        _ => IsMultiline(text) || text.Length > PasteThresholdChars,
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
