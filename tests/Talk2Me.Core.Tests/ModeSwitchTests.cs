using Talk2Me.Core.Input;

namespace Talk2Me.Core.Tests;

/// <summary>
/// The mode key, as the pure reducer sees it. It shares every rule the hold and toggle keys already
/// have — suppression, auto-repeat, an orphaned release — because it is the same gesture type.
/// </summary>
public class ModeSwitchTests
{
    private static readonly Hotkey Hold = Hotkey.ParseOrDefault("Right Ctrl");
    private static readonly Hotkey Mode = Hotkey.ParseOrDefault("Ctrl + Alt + M");

    private static Activation Create() => new(Hold, suppress: false, toggle: null, mode: Mode);

    /// <summary>
    /// Presses the combination and returns the decision from the last key. <c>Required</c> rather than
    /// <c>Modifiers</c>, because a bare modifier like Right Ctrl *is* its own trigger — pressing both
    /// would send the trigger twice, and the second one is auto-repeat.
    /// </summary>
    private static ActivationDecision Press(Activation activation, Hotkey hotkey)
    {
        foreach (var modifier in hotkey.Required)
        {
            activation.Handle(modifier, isDown: true);
        }

        return activation.Handle(hotkey.Trigger, isDown: true);
    }

    [Fact]
    public void The_mode_key_asks_for_the_next_mode()
    {
        var activation = Create();

        var decision = Press(activation, Mode);

        Assert.True(decision.NextMode);
        Assert.False(decision.Pressed);
        Assert.False(decision.Released);
    }

    /// <summary>Or the letter would be typed into whatever the user is working in.</summary>
    [Fact]
    public void The_mode_key_is_taken_from_the_application_underneath()
        => Assert.True(Press(Create(), Mode).Swallow);

    /// <summary>
    /// Holding the key down repeats it. Cycling once per repeat would run through every mode in a
    /// second and land somewhere arbitrary.
    /// </summary>
    [Fact]
    public void Holding_the_mode_key_only_cycles_once()
    {
        var activation = Create();
        Press(activation, Mode);

        Assert.False(activation.Handle(Mode.Trigger, isDown: true).NextMode);
        Assert.False(activation.Handle(Mode.Trigger, isDown: true).NextMode);
    }

    [Fact]
    public void Releasing_the_mode_key_does_nothing()
    {
        var activation = Create();
        Press(activation, Mode);

        var release = activation.Handle(Mode.Trigger, isDown: false);

        Assert.False(release.NextMode);
        Assert.False(release.Released);
    }

    /// <summary>
    /// Changing how the words will be treated halfway through saying them is not something anyone
    /// means, and it would silently reinterpret a recording already in progress.
    /// </summary>
    [Fact]
    public void Cycling_does_not_disturb_a_dictation_in_progress()
    {
        var activation = Create();
        Press(activation, Hold);
        Assert.True(activation.IsActive);

        Press(activation, Mode);

        Assert.True(activation.IsActive);
        Assert.True(activation.Handle(Hold.Trigger, isDown: false).Released);
    }

    [Fact]
    public void With_no_mode_key_configured_nothing_asks_for_a_mode()
    {
        var activation = new Activation(Hold);

        Assert.False(Press(activation, Mode).NextMode);
    }

    /// <summary>
    /// One key cannot mean both "hold this to talk" and "switch mode". The hold combination wins,
    /// because it is the one that is always configured.
    /// </summary>
    [Fact]
    public void A_mode_key_that_is_the_hold_key_is_ignored()
    {
        var activation = new Activation(Hold, suppress: false, toggle: null, mode: Hold);

        var decision = Press(activation, Hold);

        Assert.False(decision.NextMode);
        Assert.True(decision.Pressed);
    }
}
