namespace Talk2Me.Core.Input;

/// <summary>What the hook should do about one key event.</summary>
/// <param name="Swallow">Keep the key from reaching the application it was headed for.</param>
/// <param name="Pressed">The combination just became held.</param>
/// <param name="Released">The combination just stopped being held.</param>
public readonly record struct KeyDecision(bool Swallow, bool Pressed, bool Released)
{
    public static KeyDecision Nothing { get; }
}

/// <summary>
/// Decides, for each key going down or up, whether the push-to-talk combination has started, ended,
/// and whether the key should be hidden from the application underneath.
///
/// Pure and synchronous so it can be tested without a keyboard hook: the sequences that matter — auto
/// repeat, letting go of a modifier first, changing the hotkey while one is held — are exactly the
/// ones that are impossible to produce reliably by hand.
///
/// It tracks which keys it has swallowed, not just whether the gesture is active. A key hidden on the
/// way down must have its release hidden too, or the application receives a key-up for a key it never
/// saw pressed, which some of them treat as a real keystroke.
/// </summary>
public sealed class HotkeyGesture
{
    private readonly HashSet<int> _held = [];
    private readonly HashSet<int> _swallowed = [];

    private Hotkey _hotkey;
    private bool _suppress;

    public HotkeyGesture(Hotkey hotkey, bool suppress = false)
    {
        _hotkey = hotkey;
        _suppress = suppress;
    }

    /// <summary>True while the combination is held and a dictation should be running.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Points the gesture at a different combination. Returns true when a dictation was in progress
    /// and has therefore ended: the keys being held are no longer the ones we are watching, and the
    /// release that would have ended it will not be recognised.
    /// </summary>
    public bool Rebind(Hotkey hotkey, bool suppress)
    {
        _hotkey = hotkey;
        _suppress = suppress;

        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    /// <summary>Forgets everything held. For when the hook stops and key-ups will never arrive.</summary>
    public void Reset()
    {
        _held.Clear();
        _swallowed.Clear();
        IsActive = false;
    }

    public KeyDecision Handle(int virtualKey, bool isDown)
    {
        if (isDown)
        {
            _held.Add(virtualKey);
            return Down(virtualKey);
        }

        _held.Remove(virtualKey);
        return Up(virtualKey);
    }

    private KeyDecision Down(int virtualKey)
    {
        if (virtualKey != _hotkey.Trigger)
        {
            return KeyDecision.Nothing;
        }

        // Auto-repeat while the combination is held. The gesture has already started, but the key is
        // still arriving, and letting the repeats through means a held letter types itself.
        if (IsActive)
        {
            return new KeyDecision(Swallow(virtualKey), Pressed: false, Released: false);
        }

        if (!ModifiersHeld())
        {
            // The trigger on its own is just that key. Choosing Space as part of a combination must
            // not cost the user their space bar.
            return KeyDecision.Nothing;
        }

        IsActive = true;
        return new KeyDecision(Swallow(virtualKey), Pressed: true, Released: false);
    }

    private KeyDecision Up(int virtualKey)
    {
        var swallow = _swallowed.Remove(virtualKey);

        if (!IsActive || !IsPartOfCombination(virtualKey))
        {
            return new KeyDecision(swallow, Pressed: false, Released: false);
        }

        // Any key in the combination coming up ends it, including a modifier the user happened to let
        // go of first. That is what holding a combination feels like.
        IsActive = false;
        return new KeyDecision(swallow, Pressed: false, Released: true);
    }

    private bool Swallow(int virtualKey)
    {
        if (!_suppress)
        {
            return false;
        }

        _swallowed.Add(virtualKey);
        return true;
    }

    private bool IsPartOfCombination(int virtualKey)
        => virtualKey == _hotkey.Trigger || _hotkey.Required.Contains(virtualKey);

    private bool ModifiersHeld()
    {
        foreach (var modifier in _hotkey.Required)
        {
            if (!_held.Contains(modifier))
            {
                return false;
            }
        }

        return true;
    }
}
