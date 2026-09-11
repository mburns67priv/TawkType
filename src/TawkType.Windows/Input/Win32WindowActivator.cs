using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;

namespace TawkType.Windows.Input;

/// <summary>
/// Hands the foreground back to the window a dictation was aimed at.
///
/// Windows only allows a process to *give away* the foreground, not to take it, so this works because
/// TawkType is in front when the user presses the button and is therefore entitled to hand it on. There
/// is no way to make it work from the background, and no attempt is made to: a dictation box that could
/// steal focus would be a worse thing than the problem it solves.
/// </summary>
public sealed class Win32WindowActivator(ILogger<Win32WindowActivator> logger) : IWindowActivator
{
    /// <summary>
    /// How long to let the target settle. `SetForegroundWindow` returns before the switch has finished,
    /// and typing into a window that is not yet in front loses the first characters.
    /// </summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(25);

    public async Task<bool> ActivateAsync(long window, CancellationToken cancellationToken = default)
    {
        var handle = (nint)window;

        if (!IsWindow(handle))
        {
            logger.LogInformation("The window the dictation was aimed at is gone");
            return false;
        }

        // A minimised target has to be restored first, or it takes the foreground while still iconic.
        if (IsIconic(handle))
        {
            ShowWindow(handle, SW_RESTORE);
        }

        SetForegroundWindow(handle);

        var deadline = DateTime.UtcNow + SettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetForegroundWindow() == handle)
            {
                return true;
            }

            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Windows did not hand the foreground to the dictation's target window");
        return false;
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
}
