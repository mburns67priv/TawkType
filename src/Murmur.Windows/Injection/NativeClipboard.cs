using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Murmur.Windows.Injection;

/// <summary>Raw Win32 clipboard access for CF_UNICODETEXT, usable from any thread without WPF/WinForms.</summary>
internal static class NativeClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public static string? TryGetText()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !TryOpen())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void SetText(string text)
    {
        if (!TryOpen())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the clipboard");
        }

        try
        {
            EmptyClipboard();

            var bytes = (text.Length + 1) * 2;
            var handle = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (handle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc failed");
            }

            var pointer = GlobalLock(handle);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(CfUnicodeText, handle) == 0)
            {
                GlobalFree(handle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData failed");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpen()
    {
        // Another process may hold the clipboard for a few milliseconds; retry briefly.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(0))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);
}
