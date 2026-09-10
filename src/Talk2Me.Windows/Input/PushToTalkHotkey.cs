using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;

namespace Talk2Me.Windows.Input;

/// <summary>Turns raw keyboard-hook events for the configured key into clean Pressed / Released pairs.</summary>
public sealed class PushToTalkHotkey : IPushToTalkHotkey
{
    private const int DefaultVirtualKey = 0xA3; // Right Ctrl

    private readonly ISettingsProvider _settings;
    private readonly ILogger<PushToTalkHotkey> _logger;
    private readonly LowLevelKeyboardHook _hook = new();

    private int _virtualKey = DefaultVirtualKey;
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
        _logger.LogInformation("Keyboard hook installed; push-to-talk key = 0x{Vk:X2}", _virtualKey);
    }

    public void Stop()
    {
        _hook.Uninstall();
        _isDown = false;
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }

    private void ApplySettings()
    {
        var name = _settings.Current.Hotkey;
        if (!VirtualKeys.TryParse(name, out var vk))
        {
            _logger.LogWarning("Unknown hotkey '{Hotkey}'; falling back to Right Ctrl", name);
            vk = DefaultVirtualKey;
        }

        _virtualKey = vk;
        _suppress = _settings.Current.SuppressHotkey && !VirtualKeys.IsModifier(vk);
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e)
    {
        if (e.IsInjected || e.VirtualKey != _virtualKey)
        {
            return;
        }

        e.Handled = _suppress;

        if (e.IsDown)
        {
            if (_isDown)
            {
                return; // key auto-repeat
            }

            _isDown = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            if (!_isDown)
            {
                return;
            }

            _isDown = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }
}
