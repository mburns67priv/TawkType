using Microsoft.Win32;

namespace Talk2Me.Windows.Startup;

/// <summary>
/// Start-with-Windows, via the per-user Run key. Per-user rather than a machine-wide entry or a
/// scheduled task: Talk2Me installs per-user, needs no elevation, and this is the one place users can
/// see and undo for themselves, in Task Manager's Startup tab.
/// </summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Talk2Me";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Returns false when the registry refused, which is not worth interrupting the user over.</summary>
    public static bool Set(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
