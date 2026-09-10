using System.IO;
using Talk2Me.Core.Settings;

namespace Talk2Me.Desktop.Services;

/// <summary>Moves data from the pre-rename %LOCALAPPDATA%\Murmur folder so models are not downloaded twice.</summary>
internal static class LegacyMigration
{
    public static void Run()
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");
        var current = SettingsStore.AppDataDirectory;

        if (!Directory.Exists(legacy) || Directory.Exists(current))
        {
            return;
        }

        try
        {
            Directory.Move(legacy, current);
        }
        catch
        {
            // Leave the old folder in place; the app simply downloads fresh models.
        }
    }
}
