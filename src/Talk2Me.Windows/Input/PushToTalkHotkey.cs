using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Input;

namespace Talk2Me.Windows.Input;

/// <summary>
/// Turns raw keyboard-hook events into clean Pressed / Released pairs for the configured combination.
///
/// The decisions all live in <see cref="HotkeyGesture"/>, which is pure and tested; this class is the
/// wiring between it and the Win32 hook. Everything that made this fragile — auto repeat, releasing a
/// modifier first, changing the hotkey mid-hold — is a sequence of key events, and sequences are worth
/// testing without a real keyboard.
/// </summary>
public sealed class PushToTalkHotkey : IPushToTalkHotkey
{
    private readonly ISettingsProvider _settings;
    private readonly ILogger<PushToTalkHotkey> _logger;
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly HotkeyGesture _gesture = new(Hotkey.Default);

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
        _logger.LogInformation("Keyboard hook installed; push-to-talk = {Hotkey}", Describe());
    }

    public void Stop()
    {
        _hook.Uninstall();

        // The key-ups for anything held will never arrive now.
        if (_gesture.IsActive)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }

        _gesture.Reset();
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }

    private string Describe() => Hotkey.ParseOrDefault(_settings.Current.Hotkey).ToString();

    private void ApplySettings()
    {
        var text = _settings.Current.Hotkey;
        if (!Hotkey.TryParse(text, out var hotkey))
        {
            _logger.LogWarning("Unknown hotkey '{Hotkey}'; falling back to {Default}", text, Hotkey.Default);
            hotkey = Hotkey.Default;
        }

        var suppress = _settings.Current.SuppressHotkey && !hotkey.IsBareModifier;

        // Changing the hotkey while one is held strands the dictation: the release we are waiting for
        // belongs to a combination we are no longer watching.
        if (_gesture.Rebind(hotkey, suppress))
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e)
    {
        if (e.IsInjected)
        {
            return;
        }

        var decision = _gesture.Handle(e.VirtualKey, e.IsDown);
        e.Handled = decision.Swallow;

        if (decision.Pressed)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (decision.Released)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }
}
