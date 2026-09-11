using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Talk2Me.Core.Input;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// Records a push-to-talk combination by having the user hold it.
///
/// A list of key names cannot express "Right Ctrl" as distinct from "Ctrl", let alone a combination,
/// and it asks the user to recognise their own keyboard in someone else's vocabulary. Holding the
/// keys is the same gesture they will use to dictate, and it shows what it heard as they go.
///
/// Keys are read as virtual-key codes rather than characters so the result does not depend on the
/// keyboard layout, and so the two Ctrls stay different keys.
/// </summary>
public partial class HotkeyBox : UserControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(string),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnHotkeyChanged));

    /// <summary>Keys currently held, in the order they went down, so the display reads as it was typed.</summary>
    private readonly List<int> _pressed = [];

    private bool _recording;

    public HotkeyBox()
    {
        InitializeComponent();
        Show(Hotkey);
    }

    /// <summary>The combination, in the form <see cref="Core.Input.Hotkey"/> reads and writes.</summary>
    public string? Hotkey
    {
        get => (string?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        StartRecording();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
        Show(Hotkey);
    }

    /// <summary>
    /// Tunnelling, so the keys never reach anything else: while this box has focus, Tab is a key like
    /// any other and Alt must not open a menu.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_recording)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        if (e.IsRepeat)
        {
            return;
        }

        var code = KeyInterop.VirtualKeyFromKey(key);
        if (code == 0 || _pressed.Contains(code))
        {
            return;
        }

        // An ordinary key completes the combination; a second one would replace it rather than add to it.
        if (!VirtualKey.IsModifier(code))
        {
            _pressed.RemoveAll(held => !VirtualKey.IsModifier(held));
        }

        _pressed.Add(code);
        Display.Text = string.Join(" + ", _pressed.Select(VirtualKey.Describe));
    }

    /// <summary>
    /// Committing on release is what makes holding work: the combination is whatever was down at the
    /// moment the user started letting go, not whatever survives to the end.
    /// </summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (!_recording || _pressed.Count == 0)
        {
            return;
        }

        e.Handled = true;

        var modifiers = _pressed.Where(VirtualKey.IsModifier).ToArray();
        var key = _pressed.FirstOrDefault(code => !VirtualKey.IsModifier(code));
        var captured = new Hotkey(modifiers, key);

        _pressed.Clear();
        Hotkey = captured.ToString();
        Keyboard.ClearFocus();
    }

    private static void OnHotkeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is HotkeyBox box && !box._recording)
        {
            box.Show(e.NewValue as string);
        }
    }

    private void StartRecording()
    {
        _recording = true;
        _pressed.Clear();
        Display.Text = "Hold the keys you want…";
        Chrome.BorderBrush = (Brush)FindResource("Ui.Accent");
        Chrome.BorderThickness = new Thickness(2);
    }

    private void StopRecording()
    {
        _recording = false;
        _pressed.Clear();
        Chrome.BorderBrush = (Brush)FindResource("Ui.Border");
        Chrome.BorderThickness = new Thickness(1);
    }

    private void Show(string? hotkey)
    {
        if (Display is null)
        {
            return; // still being constructed
        }

        Display.Text = Core.Input.Hotkey.TryParse(hotkey, out var parsed)
            ? parsed.ToString()
            : Core.Input.Hotkey.Default.ToString();
    }
}
