using System.IO;
using TawkType.Core.Settings;

namespace TawkType.Desktop.Services;

/// <summary>
/// Brings forward data from where earlier builds kept it, so nobody re-downloads gigabytes of models
/// after an update or a rename.
///
/// Three moves have happened. The app was called Murmur, and its data lived in %LOCALAPPDATA%\Murmur.
/// Then it kept data in %LOCALAPPDATA%\TawkType — which turned out to be the folder the installer wants
/// for the application itself, so data moved again, into %LOCALAPPDATA%\Jupitor Studio\TawkType. The
/// app is now TawkType, and the folder moved with the name.
///
/// Each old location is still checked, in order, oldest first. Someone who skipped several versions
/// gets their models across in one step, and an upgrade that lands on an empty new folder does not
/// look like a factory reset.
///
/// It runs once and leaves a marker saying so. Deciding by "the destination file is missing" meant
/// clearing the history could make an old copy eligible again, and the next launch would quietly
/// resurrect dictations the user had deleted on purpose.
/// </summary>
internal static class LegacyMigration
{
    /// <summary>Everything the app owns. Named explicitly so nothing of Velopack's is ever touched.</summary>
    private static readonly string[] OwnedFiles = ["settings.json", "history.jsonl", "apikey.dat"];

    private static readonly string[] OwnedFolders = ["models", "logs"];

    /// <summary>Written after a successful pass; its presence means never migrate again.</summary>
    private const string MarkerName = ".migrated";

    public static void Run()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = SettingsStore.AppDataDirectory;
        var marker = Path.Combine(current, MarkerName);

        if (File.Exists(marker))
        {
            return;
        }

        // The whole folder was ours back then, so it can move wholesale.
        TryMoveWholeFolder(Path.Combine(localAppData, "Murmur"), current);

        // This one may now also contain the installed application, so move only what we own.
        MoveOwnedItems(Path.Combine(localAppData, "Talk2Me"), current);

        // The rename to TawkType. Whole-folder move: everything in there was ours.
        TryMoveWholeFolder(Path.Combine(localAppData, "Jupitor Studio", "Talk2Me"), current);

        // ...and if the new folder already existed (a fresh install that ran once before the upgrade),
        // the move above declines and the contents are brought across item by item instead.
        MoveOwnedItems(Path.Combine(localAppData, "Jupitor Studio", "Talk2Me"), current);

        try
        {
            Directory.CreateDirectory(current);
            File.WriteAllText(marker, "TawkType moved its data here. Delete this file to migrate again.");
        }
        catch (Exception)
        {
            // Without the marker migration simply runs again next time, which is what it did before.
        }
    }

    private static void TryMoveWholeFolder(string from, string to)
    {
        if (!Directory.Exists(from) || Directory.Exists(to))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }
        catch
        {
            // Leave it; the app downloads fresh models rather than failing to start.
        }
    }

    private static void MoveOwnedItems(string from, string to)
    {
        if (!Directory.Exists(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(to);

            foreach (var name in OwnedFiles)
            {
                var source = Path.Combine(from, name);
                var target = Path.Combine(to, name);
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Move(source, target);
                }
            }

            foreach (var name in OwnedFolders)
            {
                var source = Path.Combine(from, name);
                var target = Path.Combine(to, name);
                if (Directory.Exists(source) && !Directory.Exists(target))
                {
                    // Same volume, so this is a rename: the models come across instantly.
                    Directory.Move(source, target);
                }
            }
        }
        catch
        {
            // Partial migrations are survivable — whatever did not move is simply re-created.
        }
    }
}
