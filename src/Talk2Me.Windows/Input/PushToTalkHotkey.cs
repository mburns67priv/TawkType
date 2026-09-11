using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Input;

namespace Talk2Me.Windows.Input;

/// <summary>
/// Turns raw keyboard-hook events into clean Pressed / Released pairs for the configured combination.
///
/// The hook sees every key, so it also keeps the set of keys currently held: a combination fires when
/// its trigger goes down and everything else it needs is already down, and stops the moment any of
/// them comes up. That means releasing Shift ends the dictation just as releasing the trigger does,
/// which is what holding a combination feels like.
/// </summary>
public sealed class PushToTalkHotkey : IPushToTalkHotkey
{
    private readonly ISettingsProvider _settings;
    private readonly ILogger<PushToTalkHotkey> _logger;
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly HashSet<int> _held = [];

    private Hotkey _hotkey = Hotkey.Default;
    private bool _suppress;
    private bool _isDown;

    public PushToTalkHotkey(ISettingsProvider settings, ILogger<PushToTalkHotkey> logger)
    {
        _settings = settings;
        _logger = logger;
        _settings.Changed += (_, _) => ApplySettings();
        _hook.KeyEvent += OnKeyEvent;
        ApplySettings();
    }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public void Start()
    {
        _hook.Install();
        _logger.LogInformation("Keyboard hook installed; push-to-talk = {Hotkey}", _hotkey);
    }

    public void Stop()
    {
        _hook.Uninstall();
        _held.Clear();
        _isDown = false;
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }

    private void ApplySettings()
    {
        var text = _settings.Current.Hotkey;
        if (!Hotkey.TryParse(text, out var hotkey))
        {
            _logger.LogWarning("Unknown hotkey '{Hotkey}'; falling back to {Default}", text, Hotkey.Default);
            hotkey = Hotkey.Default;
        }

        _hotkey = hotkey;
        _suppress = _settings.Current.SuppressHotkey && !hotkey.IsBareModifier;
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e)
    {
        if (e.IsInjected)
        {
            return;
        }

        if (e.IsDown)
        {
            _held.Add(e.VirtualKey);
        }
        else
        {
            _held.Remove(e.VirtualKey);
        }

        // While the combination is held, anything in it going up ends the dictation — including a
        // modifier the user happened to let go of first.
        if (_isDown && !e.IsDown && IsPartOfHotkey(e.VirtualKey))
        {
            _isDown = false;
            e.Handled = _suppress && e.VirtualKey == _hotkey.Trigger;
            Released?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.VirtualKey != _hotkey.Trigger)
        {
            return;
        }

        if (!e.IsDown || _isDown)
        {
            return; // the release above already dealt with it, or this is auto-repeat
        }

        if (!ModifiersHeld())
        {
            return; // the trigger without its modifiers is just that key, and not ours to take
        }

        e.Handled = _suppress;
        _isDown = true;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsPartOfHotkey(int virtualKey)
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
