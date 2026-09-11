using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TawkType.Windows.Input;

public sealed class KeyHookEventArgs : EventArgs
{
    public KeyHookEventArgs(int virtualKey, bool isDown, bool isInjected)
    {
        VirtualKey = virtualKey;
        IsDown = isDown;
        IsInjected = isInjected;
    }

    public int VirtualKey { get; }

    public bool IsDown { get; }

    /// <summary>True when the event was synthesised (e.g. by our own SendInput) rather than typed.</summary>
    public bool IsInjected { get; }

    /// <summary>Set to true to swallow the key so no other application receives it.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// WH_KEYBOARD_LL hook. Gives us key-down and key-up for every key system-wide, which RegisterHotKey
/// cannot do (it only reports presses). Must be installed from a thread that pumps Windows messages.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;

    // Held in a field so the GC never collects the delegate while Windows still calls it.
    private readonly HookProc _proc;
    private nint _hook;

    public LowLevelKeyboardHook()
    {
        _proc = Callback;
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    public event EventHandler<KeyHookEventArgs>? KeyEvent;

    public bool IsInstalled => _hook != 0;

    public void Install()
    {
        if (IsInstalled)
        {
            return;
        }

        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_KEYBOARD_LL) failed");
        }
    }

    public void Uninstall()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    public void Dispose() => Uninstall();

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;

            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var args = new KeyHookEventArgs((int)data.VkCode, isDown, (data.Flags & LlkhfInjected) != 0);

                try
                {
                    KeyEvent?.Invoke(this, args);
                }
                catch
                {
                    // Never let an exception escape into the hook chain; Windows would drop the hook.
                }

                if (args.Handled)
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
