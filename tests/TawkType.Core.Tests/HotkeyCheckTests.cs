using TawkType.Core.Onboarding;

namespace TawkType.Core.Tests;

/// <summary>
/// What first run says about the key the user just recorded. The interesting half is what it refuses
/// to claim: Windows cannot be asked what a combination is already bound to, so anything not named
/// here comes back as usable rather than as free.
/// </summary>
public class HotkeyCheckTests
{
    [Fact]
    public void The_default_is_a_good_choice_and_says_so()
    {
        var advice = HotkeyCheck.For("Right Ctrl");

        Assert.Equal(HotkeyVerdict.Fine, advice.Verdict);
        Assert.Contains("Right Ctrl", advice.Message);
    }

    [Fact]
    public void Nothing_recorded_is_not_a_hotkey()
        => Assert.Equal(HotkeyVerdict.Unusable, HotkeyCheck.For(string.Empty).Verdict);

    /// <summary>
    /// The two other keys TawkType binds. Reusing one would leave the user with a key that toggles and
    /// holds at once, and no way to tell which of the two behaviours they are getting.
    /// </summary>
    [Fact]
    public void The_toggle_key_cannot_also_be_the_hold_key()
    {
        var advice = HotkeyCheck.For("F9", toggle: "F9");

        Assert.Equal(HotkeyVerdict.Unusable, advice.Verdict);
        Assert.Contains("starts and stops", advice.Message);
    }

    [Fact]
    public void The_mode_key_cannot_also_be_the_hold_key()
        => Assert.Equal(HotkeyVerdict.Unusable, HotkeyCheck.For("F8", mode: "F8").Verdict);

    /// <summary>Esc already means "abandon this dictation", and it cannot mean both.</summary>
    [Fact]
    public void Escape_is_refused()
    {
        var advice = HotkeyCheck.For("Esc");

        Assert.Equal(HotkeyVerdict.Unusable, advice.Verdict);
        Assert.Contains("cancels", advice.Message);
    }

    [Theory]
    [InlineData("Ctrl + C", "copy")]
    [InlineData("Ctrl + V", "paste")]
    [InlineData("Right Ctrl + S", "save")]
    [InlineData("Ctrl + Z", "undo")]
    public void A_shortcut_every_application_has_is_warned_about(string hotkey, string what)
    {
        var advice = HotkeyCheck.For(hotkey);

        Assert.Equal(HotkeyVerdict.Warning, advice.Verdict);
        Assert.Contains(what, advice.Message);
        Assert.True(advice.Usable); // a warning is advice, not a refusal
    }

    [Fact]
    public void Alt_Tab_would_move_the_text_somewhere_else()
        => Assert.Equal(HotkeyVerdict.Warning, HotkeyCheck.For("Alt + Tab").Verdict);

    [Fact]
    public void Alt_F4_would_close_the_window_being_dictated_into()
        => Assert.Equal(HotkeyVerdict.Warning, HotkeyCheck.For("Alt + F4").Verdict);

    /// <summary>Windows opens the Start menu on release, whatever was held with it.</summary>
    [Fact]
    public void The_Windows_key_is_warned_about_in_any_combination()
    {
        Assert.Equal(HotkeyVerdict.Warning, HotkeyCheck.For("Left Win + Space").Verdict);
        Assert.Equal(HotkeyVerdict.Warning, HotkeyCheck.For("Right Win").Verdict);
    }

    [Fact]
    public void Caps_Lock_still_turns_capitals_on()
    {
        var advice = HotkeyCheck.For("Caps Lock");

        Assert.Equal(HotkeyVerdict.Warning, advice.Verdict);
        Assert.Contains("capitals", advice.Message);
    }

    /// <summary>
    /// The failure a new user hits hardest: a plain key types into their document every time they
    /// dictate. Suppression is the fix, so the warning goes away once it is on.
    /// </summary>
    [Fact]
    public void A_plain_key_that_is_not_hidden_is_warned_about()
    {
        Assert.Equal(HotkeyVerdict.Warning, HotkeyCheck.For("F9", suppressed: false).Verdict);
        Assert.Equal(HotkeyVerdict.Fine, HotkeyCheck.For("F9", suppressed: true).Verdict);
    }

    /// <summary>A modifier types nothing on its own, so there is nothing to hide and nothing to warn about.</summary>
    [Fact]
    public void A_bare_modifier_needs_no_suppression()
        => Assert.Equal(HotkeyVerdict.Fine, HotkeyCheck.For("Right Alt", suppressed: false).Verdict);

    /// <summary>An unheard-of combination is reported as usable, never as proven free.</summary>
    [Fact]
    public void An_unknown_combination_is_left_alone()
    {
        var advice = HotkeyCheck.For("Ctrl + Shift + Space");

        Assert.Equal(HotkeyVerdict.Fine, advice.Verdict);
        Assert.DoesNotContain("nothing else", advice.Message, StringComparison.OrdinalIgnoreCase);
    }
}
