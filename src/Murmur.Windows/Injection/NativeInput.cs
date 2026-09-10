using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Murmur.Windows.Injection;

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

    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    public static Input UnicodeDown(char c) => Keyboard(0, c, KeyEventFUnicode);

    public static Input UnicodeUp(char c) => Keyboard(0, c, KeyEventFUnicode | KeyEventFKeyUp);

    public static Input KeyDown(ushort vk) => Keyboard(vk, 0, 0);

    public static Input KeyUp(ushort vk) => Keyboard(vk, 0, KeyEventFKeyUp);

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
