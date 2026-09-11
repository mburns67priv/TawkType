using Talk2Me.Core.Input;

namespace Talk2Me.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("RightControl", "Right Ctrl")]
    [InlineData("0xA3", "Right Ctrl")]
    [InlineData("F9", "F9")]
    [InlineData("CapsLock", "Caps Lock")]
    public void Settings_written_by_older_versions_still_parse(string stored, string expected)
    {
        Assert.True(Hotkey.TryParse(stored, out var hotkey));
        Assert.Equal(expected, hotkey.ToString());
    }

    [Theory]
    [InlineData("Ctrl + Shift + Space")]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("  Ctrl +Shift+ Space ")]
    public void Spacing_around_the_separator_does_not_matter(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal("Left Ctrl + Left Shift + Space", hotkey.ToString());
    }

    [Fact]
    public void What_it_writes_is_what_it_reads()
    {
        var original = new Hotkey([VirtualKey.RightAlt, VirtualKey.LeftShift], 0x51 /* Q */);

        Assert.True(Hotkey.TryParse(original.ToString(), out var round));
        Assert.Equal(original, round);
        Assert.Equal("Right Alt + Left Shift + Q", original.ToString());
    }

    [Fact]
    public void The_sides_are_different_keys()
    {
        Assert.NotEqual(Hotkey.ParseOrDefault("Left Ctrl"), Hotkey.ParseOrDefault("Right Ctrl"));
    }

    [Fact]
    public void A_bare_modifier_is_its_own_trigger_and_needs_nothing_held()
    {
        var hotkey = Hotkey.ParseOrDefault("Right Ctrl");

        Assert.True(hotkey.IsBareModifier);
        Assert.Equal(VirtualKey.RightControl, hotkey.Trigger);
        Assert.Empty(hotkey.Required);
    }

    [Fact]
    public void A_combination_triggers_on_its_ordinary_key_with_the_modifiers_held()
    {
        var hotkey = Hotkey.ParseOrDefault("Ctrl + Shift + Space");

        Assert.False(hotkey.IsBareModifier);
        Assert.Equal(0x20, hotkey.Trigger);
        Assert.Equal([VirtualKey.LeftControl, VirtualKey.LeftShift], hotkey.Required);
    }

    [Fact]
    public void Two_modifiers_alone_trigger_on_the_second_one()
    {
        var hotkey = Hotkey.ParseOrDefault("Ctrl + Shift");

        Assert.Equal(VirtualKey.LeftShift, hotkey.Trigger);
        Assert.Equal([VirtualKey.LeftControl], hotkey.Required);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nonsense")]
    [InlineData("A + B")]
    public void Anything_it_cannot_hold_is_rejected(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
        Assert.Equal(Hotkey.Default, Hotkey.ParseOrDefault(text));
    }

    /// <summary>Every key this can name, plus the ranges Describe handles without a table entry.</summary>
    public static IEnumerable<int> EveryKey()
    {
        foreach (var code in VirtualKey.AllNamed)
        {
            yield return code;
        }

        for (var digit = 0x30; digit <= 0x39; digit++) { yield return digit; }   // 0-9
        for (var letter = 0x41; letter <= 0x5A; letter++) { yield return letter; } // A-Z
        for (var pad = 0x60; pad <= 0x69; pad++) { yield return pad; }           // numpad digits
        for (var f = 0x70; f <= 0x87; f++) { yield return f; }                   // F1-F24
    }

    /// <summary>
    /// "+" separates the keys in a combination, so a key whose own name contains one cannot be read
    /// back. "Numpad +" used to, and recording that key silently reverted the hotkey to the default.
    /// </summary>
    [Fact]
    public void No_key_name_contains_the_separator()
    {
        var offenders = EveryKey().Select(VirtualKey.Describe).Where(name => name.Contains('+')).ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_key_survives_a_round_trip_inside_a_combination()
    {
        foreach (var code in EveryKey())
        {
            var original = VirtualKey.IsModifier(code)
                ? new Hotkey([code], 0)
                : new Hotkey([VirtualKey.LeftControl], code);

            Assert.True(Hotkey.TryParse(original.ToString(), out var round), original.ToString());
            Assert.Equal(original, round);
        }
    }

    [Theory]
    [InlineData("NumpadAdd")]
    [InlineData("Numpad .")]
    public void The_older_numpad_spellings_still_parse(string stored)
    {
        Assert.True(Hotkey.TryParse(stored, out _));
    }
}
