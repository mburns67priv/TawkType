using System.Globalization;

namespace Murmur.Windows.Input;

/// <summary>Maps friendly key names used in settings to Win32 virtual-key codes.</summary>
public static class VirtualKeys
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RightControl"] = 0xA3,
        ["LeftControl"] = 0xA2,
        ["RightAlt"] = 0xA5,
        ["LeftAlt"] = 0xA4,
        ["RightShift"] = 0xA1,
        ["LeftShift"] = 0xA0,
        ["RightWindows"] = 0x5C,
        ["CapsLock"] = 0x14,
        ["ScrollLock"] = 0x91,
        ["Pause"] = 0x13,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Menu"] = 0x5D,
        ["Numpad0"] = 0x60,
        ["NumpadDecimal"] = 0x6E,
        ["NumpadAdd"] = 0x6B,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["F13"] = 0x7C,
        ["F14"] = 0x7D,
        ["F15"] = 0x7E,
        ["F16"] = 0x7F,
        ["F17"] = 0x80,
        ["F18"] = 0x81,
        ["F19"] = 0x82,
        ["F20"] = 0x83,
        ["F21"] = 0x84,
        ["F22"] = 0x85,
        ["F23"] = 0x86,
        ["F24"] = 0x87,
    };

    public static IReadOnlyList<string> Names { get; } = Named.Keys.ToArray();

    /// <summary>Accepts a name from <see cref="Names"/>, a hex code like "0xA3", or a decimal code.</summary>
    public static bool TryParse(string? text, out int virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (Named.TryGetValue(text, out virtualKey))
        {
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

    /// <summary>True for modifier keys, where swallowing the key would break Ctrl/Alt/Shift shortcuts.</summary>
    public static bool IsModifier(int virtualKey) => virtualKey is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
}
