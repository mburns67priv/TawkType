using System.Runtime.InteropServices;

namespace Talk2Me.Windows.Shell;

/// <summary>
/// Paints a window's title bar dark. WPF only styles the client area, so a dark app keeps a white
/// caption bar — and the border, the close button and the system menu with it — until the window is
/// asked, through DWM, to use the immersive dark mode.
/// </summary>
public static class TitleBarTheme
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE. It was 19 in Windows 10 builds before 20H1, 20 since.</summary>
    private const int UseImmersiveDarkMode = 20;

    private const int UseImmersiveDarkModeBeforeTwentyH1 = 19;

    /// <summary>
    /// Applies or removes the dark caption bar. Safe to call repeatedly and on any Windows version:
    /// an older DWM rejects the attribute, and a title bar of the wrong shade is not worth an error.
    /// </summary>
    public static void Apply(nint window, bool dark)
    {
        if (window == 0)
        {
            return;
        }

        var value = dark ? 1 : 0;

        try
        {
            if (DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(window, UseImmersiveDarkModeBeforeTwentyH1, ref value, sizeof(int));
            }
        }
        catch (Exception)
        {
            // dwmapi is present on everything we support, but this is cosmetic either way.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
