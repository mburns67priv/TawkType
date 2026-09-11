using Microsoft.Win32;

namespace TawkType.Windows.Startup;

/// <summary>
/// Start-with-Windows, via the per-user Run key. Per-user rather than a machine-wide entry or a
/// scheduled task: TawkType installs per-user, needs no elevation, and this is the one place users can
/// see and undo for themselves, in Task Manager's Startup tab.
/// </summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TawkType";

    /// <summary>
    /// What the value was called before the rename. Deleted whenever startup is written, because an
    /// orphan here starts the app a second time and there is nothing in the UI that would turn it off.
    /// </summary>
    private const string LegacyValueName = "Talk2Me";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);

            // The old name counts as enabled: an upgrading install has startup on under that name, and
            // reporting it off would show the user a switch that disagrees with what their PC does.
            return (key?.GetValue(ValueName) ?? key?.GetValue(LegacyValueName)) is string value
                && value.Length > 0;
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

            // Either way, the pre-rename value goes. Left behind while startup is on it would launch
            // a second copy from the old path; left behind while it is off it would keep launching one
            // the user believes they have turned off.
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

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
