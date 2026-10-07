using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TawkType.Windows.Injection;

/// <summary>Thin SendInput wrapper. Unicode key events work in any app that accepts keyboard input.</summary>
internal static class NativeInput
{
    public const ushort VkReturn = 0x0D;
    public const ushort VkControl = 0x11;
    public const ushort VkMenu = 0x12; // Alt
    public const ushort VkShift = 0x10;
    public const ushort VkLWin = 0x5B;
    public const ushort VkRWin = 0x5C;
    public const ushort VkV = 0x56;
    public const int VkCapital = 0x14;

    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const uint KeyEventFExtendedKey = 0x0001;
    private const uint MapVkVkToVscEx = 4;
    private const int ShiftStateShift = 1;

    public static Input UnicodeDown(char c) => Keyboard(0, c, KeyEventFUnicode);

    public static Input UnicodeUp(char c) => Keyboard(0, c, KeyEventFUnicode | KeyEventFKeyUp);

    public static Input KeyDown(ushort vk) => Keyboard(vk, 0, 0);

    public static Input KeyUp(ushort vk) => Keyboard(vk, 0, KeyEventFKeyUp);

    /// <summary>
    /// A key event that carries its scan code as well as its virtual key. Whatever reads the hardware
    /// view of the keyboard — a VNC viewer forwarding keys to another machine — reads the scan code,
    /// and a zero there is a key that does not exist.
    /// </summary>
    public static Input KeyDown(ushort vk, ushort scan, bool extended) => Keyboard(vk, scan, extended ? KeyEventFExtendedKey : 0);

    public static Input KeyUp(ushort vk, ushort scan, bool extended) =>
        Keyboard(vk, scan, KeyEventFKeyUp | (extended ? KeyEventFExtendedKey : 0));

    /// <summary>
    /// The key and Shift state that type <paramref name="c"/> on <paramref name="layout"/>, or null
    /// when it needs Ctrl or Alt (AltGr) or has no key at all. Only Shift is allowed: Ctrl and Alt
    /// combinations are shortcuts on the far side of a remote viewer, not characters.
    /// </summary>
    public static (ushort Vk, bool Shift)? KeyFor(char c, nint layout)
    {
        var result = VkKeyScanEx(c, layout);
        if (result == -1)
        {
            return null;
        }

        var vk = (ushort)(result & 0xFF);
        var state = (result >> 8) & 0xFF;
        if ((state & ~ShiftStateShift) != 0)
        {
            return null;
        }

        return (vk, (state & ShiftStateShift) != 0);
    }

    /// <summary>The scan code for <paramref name="vk"/>, and whether it is an E0 extended key.</summary>
    public static (ushort Scan, bool Extended) ScanCodeFor(ushort vk, nint layout)
    {
        var code = MapVirtualKeyEx(vk, MapVkVkToVscEx, layout);
        return ((ushort)(code & 0xFF), (code & 0xFF00) == 0xE000);
    }

    public static void Send(IReadOnlyList<Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var array = inputs as Input[] ?? inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
        if (sent != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput sent {sent}/{array.Length} events");
        }
    }

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Whether Caps Lock is toggled on. The low bit of GetKeyState is the toggle, not the press.</summary>
    public static bool IsCapsLockOn() => (GetKeyState(VkCapital) & 1) != 0;

    /// <summary>
    /// Waits until Ctrl/Alt/Shift/Win are all up so typed characters are not turned into shortcuts.
    /// Gives up after <paramref name="timeout"/> and proceeds anyway.
    /// </summary>
    public static async Task WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsKeyDown(VkControl) && !IsKeyDown(VkMenu) && !IsKeyDown(VkShift) && !IsKeyDown(VkLWin) && !IsKeyDown(VkRWin))
            {
                return;
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Input Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = vk,
                ScanCode = scan,
                Flags = flags,
            },
        },
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanEx(char ch, nint dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
