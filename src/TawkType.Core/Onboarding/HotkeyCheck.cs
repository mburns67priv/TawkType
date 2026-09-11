using TawkType.Core.Input;

namespace TawkType.Core.Onboarding;

public enum HotkeyVerdict
{
    /// <summary>Nothing known stands in its way.</summary>
    Fine,

    /// <summary>It will work, and it will also do something else the user did not ask for.</summary>
    Warning,

    /// <summary>TawkType cannot bind it, or it already means something else here.</summary>
    Unusable,
}

/// <summary>A verdict and the sentence to show under the recorder.</summary>
public readonly record struct HotkeyAdvice(HotkeyVerdict Verdict, string Message)
{
    public bool Usable => Verdict != HotkeyVerdict.Unusable;
}

/// <summary>
/// The obvious conflicts, checked before the user finds them the hard way — which for a dictation key
/// means discovering that every sentence they speak also copies, closes, or opens the Start menu.
///
/// Deliberately not exhaustive. Windows has no way to ask what a combination is already bound to, so
/// this names the ones nearly every user has, and says nothing about the rest rather than implying a
/// combination is free when TawkType cannot know that.
/// </summary>
public static class HotkeyCheck
{
    private const int Escape = 0x1B;
    private const int CapsLock = 0x14;
    private const int Tab = 0x09;
    private const int F4 = 0x73;

    /// <summary>Ctrl + these are the shortcuts it is safe to assume an application has.</summary>
    private static readonly Dictionary<int, string> CommonCtrlShortcuts = new()
    {
        [(int)'A'] = "select all",
        [(int)'C'] = "copy",
        [(int)'F'] = "find",
        [(int)'N'] = "new",
        [(int)'P'] = "print",
        [(int)'S'] = "save",
        [(int)'V'] = "paste",
        [(int)'W'] = "close",
        [(int)'X'] = "cut",
        [(int)'Z'] = "undo",
    };

    /// <summary>
    /// Judges the combination the user has just recorded, against the other two TawkType binds and
    /// against whether the key is being swallowed before the focused application sees it.
    /// </summary>
    public static HotkeyAdvice For(string? hold, string? toggle = null, string? mode = null, bool suppressed = false)
    {
        if (!Hotkey.TryParse(hold, out var key))
        {
            return new(HotkeyVerdict.Unusable, "Click the box and hold the keys you want to dictate with.");
        }

        if (Same(key, toggle))
        {
            return new(HotkeyVerdict.Unusable, "That is already the key that starts and stops a dictation with two presses.");
        }

        if (Same(key, mode))
        {
            return new(HotkeyVerdict.Unusable, "That is already the key that switches dictation mode.");
        }

        if (key.Trigger == Escape)
        {
            return new(HotkeyVerdict.Unusable, "Esc cancels a dictation in progress, so it cannot also start one.");
        }

        if (key.Modifiers.Any(IsWindows) || key.Trigger is VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return new(
                HotkeyVerdict.Warning,
                "Windows opens the Start menu when the Windows key is let go, whatever else was held with it.");
        }

        if (key.Modifiers.Any(IsControl) && CommonCtrlShortcuts.TryGetValue(key.Trigger, out var what))
        {
            return new(
                HotkeyVerdict.Warning,
                $"{key} is {what} in most applications. Dictating with it will do that as well, every time.");
        }

        if (key.Modifiers.Any(IsAlt) && key.Trigger == Tab)
        {
            return new(HotkeyVerdict.Warning, "Alt + Tab switches windows, which would move your text somewhere else mid-dictation.");
        }

        if (key.Modifiers.Any(IsAlt) && key.Trigger == F4)
        {
            return new(HotkeyVerdict.Warning, "Alt + F4 closes the window you are dictating into.");
        }

        if (key.Trigger == CapsLock)
        {
            return new(
                HotkeyVerdict.Warning,
                "Caps Lock still turns capitals on and off when you press it, whether or not TawkType swallows it.");
        }

        // A key with nothing held alongside it is the case that bites hardest: every dictation also
        // types the letter, or spaces, or scrolls the page, unless the key is being swallowed first.
        if (key.Modifiers.Count == 0 && !key.IsBareModifier && !suppressed)
        {
            return new(
                HotkeyVerdict.Warning,
                $"{key} on its own still reaches whatever you are typing into. Turn on “Hide the key from other apps” below, "
                + "or hold a modifier with it.");
        }

        return new(
            HotkeyVerdict.Fine,
            key.IsBareModifier
                ? $"{key} on its own is a good choice: it is a modifier, so nothing is typed while you hold it."
                : $"Hold {key} to dictate, and let go when you have finished.");
    }

    private static bool Same(Hotkey key, string? other)
        => Hotkey.TryParse(other, out var parsed) && parsed == key;

    private static bool IsControl(int code) => code is VirtualKey.LeftControl or VirtualKey.RightControl;

    private static bool IsAlt(int code) => code is VirtualKey.LeftAlt or VirtualKey.RightAlt;

    private static bool IsWindows(int code) => code is VirtualKey.LeftWindows or VirtualKey.RightWindows;
}
