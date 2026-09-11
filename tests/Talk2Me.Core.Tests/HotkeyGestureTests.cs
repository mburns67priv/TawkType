using Talk2Me.Core.Input;

namespace Talk2Me.Core.Tests;

/// <summary>
/// The key sequences that are impossible to produce reliably by hand, and that broke in practice:
/// auto repeat, letting go of a modifier first, and changing the hotkey while one is held.
/// </summary>
public class HotkeyGestureTests
{
    private const int Space = 0x20;
    private const int A = 0x41;

    private static HotkeyGesture Combination(bool suppress = false)
        => new(Hotkey.ParseOrDefault("Ctrl + Shift + Space"), suppress);

    private static HotkeyGesture BareModifier(bool suppress = false)
        => new(Hotkey.ParseOrDefault("Right Ctrl"), suppress);

    [Fact]
    public void A_bare_modifier_starts_and_ends_on_its_own_key()
    {
        var gesture = BareModifier();

        Assert.True(gesture.Handle(VirtualKey.RightControl, isDown: true).Pressed);
        Assert.True(gesture.IsActive);
        Assert.True(gesture.Handle(VirtualKey.RightControl, isDown: false).Released);
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void A_combination_starts_only_once_its_modifiers_are_held()
    {
        var gesture = Combination();

        Assert.False(gesture.Handle(Space, isDown: true).Pressed);
        Assert.False(gesture.IsActive);

        gesture.Handle(Space, isDown: false);
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);

        Assert.True(gesture.Handle(Space, isDown: true).Pressed);
    }

    [Fact]
    public void The_trigger_alone_is_left_for_the_application()
    {
        var decision = Combination(suppress: true).Handle(Space, isDown: true);

        Assert.False(decision.Swallow);
        Assert.False(decision.Pressed);
    }

    [Fact]
    public void Letting_go_of_a_modifier_first_ends_the_dictation()
    {
        var gesture = Combination();
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);
        gesture.Handle(Space, isDown: true);

        Assert.True(gesture.Handle(VirtualKey.LeftShift, isDown: false).Released);
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void It_does_not_end_twice_when_the_rest_of_the_combination_comes_up()
    {
        var gesture = Combination();
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);
        gesture.Handle(Space, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: false);

        Assert.False(gesture.Handle(Space, isDown: false).Released);
        Assert.False(gesture.Handle(VirtualKey.LeftControl, isDown: false).Released);
    }

    /// <summary>
    /// The repeat leak: holding the key sends a stream of key-downs, and only the first was being
    /// swallowed, so a held letter typed itself into whatever was in front.
    /// </summary>
    [Fact]
    public void Auto_repeat_stays_swallowed_for_as_long_as_the_key_is_held()
    {
        var gesture = Combination(suppress: true);
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);

        Assert.True(gesture.Handle(Space, isDown: true).Swallow);

        for (var repeat = 0; repeat < 5; repeat++)
        {
            var decision = gesture.Handle(Space, isDown: true);
            Assert.True(decision.Swallow);
            Assert.False(decision.Pressed);
        }
    }

    /// <summary>A key hidden on the way down must be hidden on the way up, or the app sees a stray key-up.</summary>
    [Fact]
    public void A_swallowed_key_has_its_release_swallowed_too()
    {
        var gesture = Combination(suppress: true);
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);
        gesture.Handle(Space, isDown: true);

        // The modifier goes up first, so the gesture ends before the trigger is released.
        gesture.Handle(VirtualKey.LeftShift, isDown: false);

        Assert.True(gesture.Handle(Space, isDown: false).Swallow);
    }

    [Fact]
    public void Nothing_is_swallowed_when_suppression_is_off()
    {
        var gesture = Combination();
        gesture.Handle(VirtualKey.LeftControl, isDown: true);
        gesture.Handle(VirtualKey.LeftShift, isDown: true);

        Assert.False(gesture.Handle(Space, isDown: true).Swallow);
        Assert.False(gesture.Handle(Space, isDown: false).Swallow);
    }

    [Fact]
    public void Changing_the_hotkey_mid_hold_ends_the_dictation_rather_than_stranding_it()
    {
        var gesture = BareModifier();
        gesture.Handle(VirtualKey.RightControl, isDown: true);

        Assert.True(gesture.Rebind(Hotkey.ParseOrDefault("F9"), suppress: false));
        Assert.False(gesture.IsActive);

        // The old release arrives for a combination nobody is watching, and is simply ignored.
        Assert.False(gesture.Handle(VirtualKey.RightControl, isDown: false).Released);
    }

    [Fact]
    public void Rebinding_while_idle_reports_nothing_to_end()
    {
        Assert.False(BareModifier().Rebind(Hotkey.ParseOrDefault("F9"), suppress: false));
    }

    [Fact]
    public void Reset_forgets_keys_whose_releases_will_never_arrive()
    {
        var gesture = BareModifier();
        gesture.Handle(VirtualKey.RightControl, isDown: true);
        gesture.Reset();

        Assert.False(gesture.IsActive);
        Assert.False(gesture.Handle(VirtualKey.RightControl, isDown: false).Released);
    }

    [Fact]
    public void Keys_that_are_no_part_of_the_combination_are_ignored()
    {
        var gesture = Combination(suppress: true);

        Assert.Equal(KeyDecision.Nothing, gesture.Handle(A, isDown: true));
        Assert.Equal(KeyDecision.Nothing, gesture.Handle(A, isDown: false));
    }
}
