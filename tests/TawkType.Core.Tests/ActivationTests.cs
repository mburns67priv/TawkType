using TawkType.Core.Input;

namespace TawkType.Core.Tests;

/// <summary>
/// The three ways a dictation starts or stops from the keyboard, and the places where they meet:
/// Escape only counting while something is running, a toggle left half-finished, and the same key
/// being asked to mean two things.
/// </summary>
public class ActivationTests
{
    private const int Escape = 0x1B;
    private const int F9 = 0x78;

    private static Hotkey Hold => Hotkey.ParseOrDefault("Right Ctrl");

    private static Hotkey Toggle => Hotkey.ParseOrDefault("F9");

    private static Activation WithToggle(bool suppress = false)
        => new(Hold, suppress, Toggle);

    [Fact]
    public void Holding_the_push_to_talk_key_works_as_it_always_did()
    {
        var activation = WithToggle();

        Assert.True(activation.Handle(VirtualKey.RightControl, isDown: true).Pressed);
        Assert.True(activation.IsActive);
        Assert.True(activation.Handle(VirtualKey.RightControl, isDown: false).Released);
        Assert.False(activation.IsActive);
    }

    [Fact]
    public void The_toggle_key_starts_on_the_first_press_and_finishes_on_the_second()
    {
        var activation = WithToggle();

        var first = activation.Handle(F9, isDown: true);
        Assert.True(first.Pressed);
        Assert.False(first.Released);
        Assert.True(activation.IsActive);

        // Letting go of the toggle key means nothing at all; that is the point of it.
        var up = activation.Handle(F9, isDown: false);
        Assert.False(up.Released);
        Assert.True(activation.IsActive);

        var second = activation.Handle(F9, isDown: true);
        Assert.True(second.Released);
        Assert.False(second.Pressed);
        Assert.False(activation.IsActive);
    }

    [Fact]
    public void The_toggle_key_is_hidden_from_the_application_underneath()
    {
        Assert.True(WithToggle().Handle(F9, isDown: true).Swallow);
    }

    [Fact]
    public void Escape_is_left_alone_while_nothing_is_running()
    {
        var activation = WithToggle();

        var decision = activation.Handle(Escape, isDown: true);

        Assert.False(decision.Cancel);
        Assert.False(decision.Swallow);
    }

    [Fact]
    public void Escape_cancels_while_a_dictation_is_in_progress()
    {
        var activation = WithToggle();
        activation.DictationInProgress = true;

        var decision = activation.Handle(Escape, isDown: true);

        Assert.True(decision.Cancel);
        Assert.True(decision.Swallow);
    }

    /// <summary>
    /// Otherwise the abandoned toggle would still be counted as running, and the next press of the
    /// toggle key would try to finish a dictation that no longer exists.
    /// </summary>
    [Fact]
    public void Escape_leaves_a_toggled_dictation_properly_finished()
    {
        var activation = WithToggle();
        activation.Handle(F9, isDown: true);
        activation.Handle(F9, isDown: false);
        activation.DictationInProgress = true;

        activation.Handle(Escape, isDown: true);
        Assert.False(activation.IsActive);

        // The next press must start a new dictation, not try to finish the abandoned one.
        activation.DictationInProgress = false;
        Assert.True(activation.Handle(F9, isDown: true).Pressed);
    }

    /// <summary>Escape with the toggle key still physically down, which is awkward but allowed.</summary>
    [Fact]
    public void Escape_recovers_even_if_the_toggle_key_is_still_held()
    {
        var activation = WithToggle();
        activation.Handle(F9, isDown: true);
        activation.DictationInProgress = true;

        activation.Handle(Escape, isDown: true);
        activation.Handle(F9, isDown: false);

        activation.DictationInProgress = false;
        Assert.True(activation.Handle(F9, isDown: true).Pressed);
    }

    [Fact]
    public void One_key_cannot_mean_both_hold_and_toggle()
    {
        var activation = new Activation(Hold, suppress: false, toggle: Hold);

        // The hold gesture wins: press and release, not press and press.
        Assert.True(activation.Handle(VirtualKey.RightControl, isDown: true).Pressed);
        Assert.True(activation.Handle(VirtualKey.RightControl, isDown: false).Released);
    }

    [Fact]
    public void Removing_the_toggle_key_ends_a_dictation_it_had_started()
    {
        var activation = WithToggle();
        activation.Handle(F9, isDown: true);

        Assert.True(activation.Rebind(Hold, suppress: false, toggle: null));
        Assert.False(activation.IsActive);
    }

    [Fact]
    public void Changing_the_toggle_key_ends_a_dictation_it_had_started()
    {
        var activation = WithToggle();
        activation.Handle(F9, isDown: true);

        Assert.True(activation.Rebind(Hold, suppress: false, toggle: Hotkey.ParseOrDefault("F10")));
        Assert.False(activation.IsActive);
    }

    [Fact]
    public void Rebinding_while_idle_ends_nothing()
    {
        Assert.False(WithToggle().Rebind(Hold, suppress: false, toggle: Hotkey.ParseOrDefault("F10")));
    }

    [Fact]
    public void Resetting_reports_whether_a_dictation_was_abandoned()
    {
        var running = WithToggle();
        running.Handle(F9, isDown: true);
        Assert.True(running.Reset());

        Assert.False(WithToggle().Reset());
    }

    [Fact]
    public void With_no_toggle_configured_the_toggle_key_is_an_ordinary_key()
    {
        var activation = new Activation(Hold);

        Assert.Equal(ActivationDecision.Nothing, activation.Handle(F9, isDown: true));
    }
}
