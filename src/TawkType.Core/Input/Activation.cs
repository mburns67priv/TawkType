namespace TawkType.Core.Input;

/// <summary>What one key event means for dictation.</summary>
/// <param name="Swallow">Keep the key from reaching the application underneath.</param>
/// <param name="Pressed">A dictation should start.</param>
/// <param name="Released">The dictation should finish and be delivered.</param>
/// <param name="Cancel">The dictation should be abandoned: nothing typed, nothing recorded.</param>
/// <param name="NextMode">The user wants the next mode along.</param>
public readonly record struct ActivationDecision(
    bool Swallow,
    bool Pressed,
    bool Released,
    bool Cancel,
    bool NextMode = false)
{
    public static ActivationDecision Nothing { get; }
}

/// <summary>
/// Every way a dictation can be started or stopped from the keyboard, in one place and with no Win32
/// in sight.
///
/// There are three: hold the push-to-talk combination, press an optional toggle combination twice, or
/// press Escape to abandon what is running. The first two produce the same Pressed/Released pair, so
/// nothing downstream needs to know which was used.
///
/// Escape is only taken while a dictation is actually in progress. The rest of the time it belongs to
/// whatever the user is typing into, and quietly swallowing it would be a good way to make dialogs
/// stop closing.
/// </summary>
public sealed class Activation
{
    private const int Escape = 0x1B;

    private readonly HotkeyGesture _hold;

    /// <summary>Null when no toggle combination is configured, which is the default.</summary>
    private HotkeyGesture? _toggle;

    /// <summary>Null when no mode-cycling combination is configured, which is the default.</summary>
    private HotkeyGesture? _mode;

    /// <summary>True between the two presses of a toggled dictation.</summary>
    private bool _toggled;

    public Activation(Hotkey hold, bool suppress = false, Hotkey? toggle = null, Hotkey? mode = null)
    {
        _hold = new HotkeyGesture(hold, suppress);
        _toggle = Secondary(hold, toggle);
        _mode = Secondary(hold, mode);
    }

    /// <summary>Set by the engine while there is something Escape could abandon.</summary>
    public bool DictationInProgress { get; set; }

    /// <summary>True while a dictation is running because of these keys.</summary>
    public bool IsActive => _hold.IsActive || _toggled;

    /// <summary>
    /// Points at different combinations. Returns true when a dictation was in progress and has
    /// therefore ended: the keys being held are no longer the ones being watched, so the release that
    /// would have finished it will never be recognised.
    /// </summary>
    public bool Rebind(Hotkey hold, bool suppress, Hotkey? toggle, Hotkey? mode = null)
    {
        var ended = _hold.Rebind(hold, suppress);
        _mode = Secondary(hold, mode);

        var next = Secondary(hold, toggle);
        if (_toggled && (next is null || _toggle is null))
        {
            ended = true;
            _toggled = false;
        }
        else if (next is not null && _toggle is not null && _toggle.Rebind(next.Hotkey, suppress: true))
        {
            ended = true;
            _toggled = false;
        }

        _toggle = next;
        return ended;
    }

    /// <summary>Forgets everything held. For when the hook stops and key-ups will never arrive.</summary>
    public bool Reset()
    {
        var ended = IsActive;
        _toggled = false;
        _hold.Reset();
        _toggle?.Reset();
        return ended;
    }

    public ActivationDecision Handle(int virtualKey, bool isDown)
    {
        if (DictationInProgress && isDown && virtualKey == Escape)
        {
            // Taken from the focused application on purpose: the user means "stop this", and letting
            // Escape through as well would close whatever is open behind us at the same time.
            //
            // The toggle gesture is reset as well as the flag. Clearing only the flag left the gesture
            // believing its key was still down, so the next press read as auto-repeat and the toggle
            // stopped working until the key was pressed twice more.
            _toggled = false;
            _toggle?.Reset();
            return new ActivationDecision(Swallow: true, Pressed: false, Released: false, Cancel: true);
        }

        var hold = _hold.Handle(virtualKey, isDown);
        var toggle = _toggle?.Handle(virtualKey, isDown) ?? KeyDecision.Nothing;
        var mode = _mode?.Handle(virtualKey, isDown) ?? KeyDecision.Nothing;
        var swallow = hold.Swallow || toggle.Swallow || mode.Swallow;

        // Cycling the mode is about the dictation that has not started yet, so it deliberately does
        // nothing to one already running: changing how the words will be treated halfway through
        // saying them is not something anyone means.
        if (mode.Pressed)
        {
            return new ActivationDecision(swallow, Pressed: false, Released: false, Cancel: false, NextMode: true);
        }

        // The toggle key going down flips the dictation; its release means nothing.
        if (toggle.Pressed)
        {
            _toggled = !_toggled;
            return new ActivationDecision(swallow, Pressed: _toggled, Released: !_toggled, Cancel: false);
        }

        return new ActivationDecision(swallow, hold.Pressed, hold.Released, Cancel: false);
    }

    /// <summary>
    /// A secondary gesture, or null when there is not one to have. One key cannot mean both "hold
    /// this" and something else: the hold combination wins, because it is the one always configured.
    /// </summary>
    private static HotkeyGesture? Secondary(Hotkey hold, Hotkey? other)
        => other is null || other.Equals(hold) ? null : new HotkeyGesture(other, suppress: true);
}
