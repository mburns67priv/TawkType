using System.Globalization;

namespace Talk2Me.Core.Input;

/// <summary>
/// A push-to-talk combination: any number of held modifiers and, usually, one other key.
///
/// Stored and shown in the same form — "Right Ctrl", "Ctrl + Shift + Space" — so settings.json stays
/// readable and the pill can print what it is given. Parsing accepts the older single-name values
/// ("RightControl") and raw codes ("0xA3") so nobody's existing settings stop working.
///
/// Sides are kept: Right Ctrl is not Left Ctrl. That is the whole point of the usual choice of
/// Right Ctrl for dictation, and it is what the user actually pressed.
/// </summary>
public sealed record Hotkey(IReadOnlyList<int> Modifiers, int Key)
{
    /// <summary>What a fresh install uses: a key nothing else wants, on a side nothing else uses.</summary>
    public static Hotkey Default { get; } = new([], VirtualKey.RightControl);

    /// <summary>The key whose press starts dictation. The modifiers only have to be held.</summary>
    public int Trigger => Key != 0 ? Key : Modifiers.Count > 0 ? Modifiers[^1] : VirtualKey.RightControl;

    /// <summary>Everything that must already be down when <see cref="Trigger"/> arrives.</summary>
    public IReadOnlyList<int> Required => Key != 0 ? Modifiers : Modifiers.Take(Modifiers.Count - 1).ToArray();

    /// <summary>True when the combination is nothing but a modifier, where swallowing it would hurt.</summary>
    public bool IsBareModifier => Key == 0;

    /// <summary>
    /// Two combinations are the same when they hold the same keys. A record would compare the modifier
    /// list by reference, which makes every freshly parsed copy a different hotkey.
    /// </summary>
    public bool Equals(Hotkey? other)
        => other is not null && Key == other.Key && Modifiers.SequenceEqual(other.Modifiers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        foreach (var modifier in Modifiers)
        {
            hash.Add(modifier);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        string.Join(" + ", Modifiers.Concat(Key == 0 ? [] : new[] { Key }).Select(VirtualKey.Describe));

    /// <summary>
    /// Reads a combination. Returns false for anything it cannot make sense of, leaving the caller to
    /// decide — which in practice means falling back to the default rather than losing the hotkey.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = new List<int>();
        var key = 0;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!VirtualKey.TryParse(part, out var code))
            {
                return false;
            }

            if (VirtualKey.IsModifier(code))
            {
                modifiers.Add(code);
            }
            else if (key != 0)
            {
                return false; // two ordinary keys is not a combination anyone can hold
            }
            else
            {
                key = code;
            }
        }

        if (modifiers.Count == 0 && key == 0)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>Parses, or gives back the default. For the places where carrying on matters more.</summary>
    public static Hotkey ParseOrDefault(string? text) => TryParse(text, out var hotkey) ? hotkey : Default;
}

/// <summary>Virtual-key codes and the names Talk2Me shows for them.</summary>
public static class VirtualKey
{
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;

    /// <summary>Display name first; the parser also takes the aliases below and raw codes.</summary>
    private static readonly Dictionary<int, string> Names = new()
    {
        [LeftShift] = "Left Shift",
        [RightShift] = "Right Shift",
        [LeftControl] = "Left Ctrl",
        [RightControl] = "Right Ctrl",
        [LeftAlt] = "Left Alt",
        [RightAlt] = "Right Alt",
        [LeftWindows] = "Left Win",
        [RightWindows] = "Right Win",
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5D] = "Menu",
        // Spelled out, not punctuation: "+" separates the keys in a combination, so a key whose own
        // name contains one cannot survive a round trip through settings. See NoNameContainsSeparator.
        [0x6A] = "Numpad Multiply",
        [0x6B] = "Numpad Plus",
        [0x6D] = "Numpad Minus",
        [0x6E] = "Numpad Dot",
        [0x6F] = "Numpad Divide",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };

    /// <summary>Older settings files and hand edits. Never produced by <see cref="Describe"/>.</summary>
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RightControl"] = RightControl,
        ["LeftControl"] = LeftControl,
        ["RightCtrl"] = RightControl,
        ["LeftCtrl"] = LeftControl,
        ["Ctrl"] = LeftControl,
        ["Control"] = LeftControl,
        ["RightAlt"] = RightAlt,
        ["LeftAlt"] = LeftAlt,
        ["Alt"] = LeftAlt,
        ["RightShift"] = RightShift,
        ["LeftShift"] = LeftShift,
        ["Shift"] = LeftShift,
        ["RightWindows"] = RightWindows,
        ["LeftWindows"] = LeftWindows,
        ["Win"] = LeftWindows,
        ["CapsLock"] = 0x14,
        ["ScrollLock"] = 0x91,
        ["NumLock"] = 0x90,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Escape"] = 0x1B,
        ["Return"] = 0x0D,
        ["NumpadDecimal"] = 0x6E,
        ["NumpadAdd"] = 0x6B,
        ["NumpadSubtract"] = 0x6D,
        ["NumpadMultiply"] = 0x6A,
        ["NumpadDivide"] = 0x6F,
        ["Numpad ."] = 0x6E,
        ["Numpad -"] = 0x6D,
        ["Numpad *"] = 0x6A,
        ["Numpad /"] = 0x6F,
    };

    /// <summary>Every key this can name, for the round-trip invariant the tests check.</summary>
    public static IReadOnlyCollection<int> AllNamed => Names.Keys;

    /// <summary>The name shown to the user, e.g. "Right Ctrl", "F9", "A".</summary>
    public static string Describe(int virtualKey)
    {
        if (Names.TryGetValue(virtualKey, out var name))
        {
            return name;
        }

        return virtualKey switch
        {
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),              // 0-9
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),              // A-Z
            >= 0x60 and <= 0x69 => "Numpad " + (virtualKey - 0x60),
            >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),                  // F1-F24
            _ => "0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>A display name, an older alias, or a raw code. One key, not a combination.</summary>
    public static bool TryParse(string? text, out int virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();

        foreach (var (code, name) in Names)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = code;
                return true;
            }
        }

        if (Aliases.TryGetValue(text, out virtualKey))
        {
            return true;
        }

        if (text.Length == 1 && (char.IsAsciiLetterOrDigit(text[0])))
        {
            virtualKey = char.ToUpperInvariant(text[0]);
            return true;
        }

        if (text.StartsWith('F') && int.TryParse(text[1..], out var f) && f is >= 1 and <= 24)
        {
            virtualKey = 0x6F + f;
            return true;
        }

        if (text.StartsWith("Numpad ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[7..], out var pad) && pad is >= 0 and <= 9)
        {
            virtualKey = 0x60 + pad;
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey))
        {
            return virtualKey is > 0 and < 256;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out virtualKey)
               && virtualKey is > 0 and < 256;
    }

    /// <summary>True for Ctrl, Alt, Shift and Windows, either side.</summary>
    public static bool IsModifier(int virtualKey)
        => virtualKey is >= LeftShift and <= RightAlt or LeftWindows or RightWindows;
}
