using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Input;

namespace Talk2Me.Windows.Input;

/// <summary>
/// The Win32 end of the push-to-talk keys: installs the low-level hook, hands each event to
/// <see cref="Activation"/>, and raises whatever that says happened.
///
/// Every decision — hold, toggle, Escape, auto repeat, which keys were swallowed — lives in Core,
/// where it is pure and tested. The sequences that broke in practice are impossible to produce
/// reliably by hand, so they are worth testing without a real keyboard.
/// </summary>
public sealed class PushToTalkHotkey : IPushToTalkHotkey
{
    private readonly ISettingsProvider _settings;
    private readonly ILogger<PushToTalkHotkey> _logger;
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly Activation _activation = new(Hotkey.Default);

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

    public event EventHandler? CancelRequested;

    public bool DictationInProgress
    {
        get => _activation.DictationInProgress;
        set => _activation.DictationInProgress = value;
    }

    public void Start()
    {
        _hook.Install();
        _logger.LogInformation(
            "Keyboard hook installed; hold = {Hold}, toggle = {Toggle}",
            Hotkey.ParseOrDefault(_settings.Current.Hotkey),
            string.IsNullOrWhiteSpace(_settings.Current.ToggleHotkey) ? "off" : _settings.Current.ToggleHotkey);
    }

    public void Stop()
    {
        _hook.Uninstall();

        // The key-ups for anything held will never arrive now.
        if (_activation.Reset())
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }

    private void ApplySettings()
    {
        var text = _settings.Current.Hotkey;
        if (!Hotkey.TryParse(text, out var hold))
        {
            _logger.LogWarning("Unknown hotkey '{Hotkey}'; falling back to {Default}", text, Hotkey.Default);
            hold = Hotkey.Default;
        }

        Hotkey? toggle = null;
        var toggleText = _settings.Current.ToggleHotkey;
        if (!string.IsNullOrWhiteSpace(toggleText))
        {
            if (Hotkey.TryParse(toggleText, out var parsed))
            {
                toggle = parsed;
            }
            else
            {
                _logger.LogWarning("Unknown toggle hotkey '{Hotkey}'; leaving toggle off", toggleText);
            }
        }

        var suppress = _settings.Current.SuppressHotkey && !hold.IsBareModifier;

        // Changing the keys while one is held strands the dictation: the release we are waiting for
        // belongs to a combination we are no longer watching.
        if (_activation.Rebind(hold, suppress, toggle))
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

        var decision = _activation.Handle(e.VirtualKey, e.IsDown);
        e.Handled = decision.Swallow;

        if (decision.Cancel)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (decision.Pressed)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (decision.Released)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }
}
