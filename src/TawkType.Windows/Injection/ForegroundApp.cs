using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TawkType.Windows.Injection;

/// <summary>
/// The window text is about to be delivered to: which process owns it, and the keyboard layout its
/// thread is using. Read at delivery rather than at key-down, because the injector has to decide how
/// to type for the window that is actually in front.
/// </summary>
internal readonly record struct ForegroundApp(string? ProcessName, nint KeyboardLayout)
{
    public static ForegroundApp Current()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return new(null, GetKeyboardLayout(0));
        }

        var threadId = GetWindowThreadProcessId(window, out var processId);
        return new(NameOf(processId), GetKeyboardLayout(threadId));
    }

    private static string? NameOf(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);
}
