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
}
