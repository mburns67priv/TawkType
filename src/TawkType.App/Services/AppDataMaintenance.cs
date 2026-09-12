using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.History;
using TawkType.Core.Settings;
using TawkType.Transcription;

namespace TawkType.Desktop.Services;

/// <summary>What is in the data folder, in the words the confirmation needs.</summary>
/// <param name="Bytes">Everything under the folder, models included.</param>
public sealed record AppDataContents(long Bytes, int ModelCount, int HistoryEntries, bool HasApiKey)
{
    public string SizeText => ModelStorage.FormatSize(Bytes);

    public bool IsEmpty => Bytes == 0 && HistoryEntries == 0 && !HasApiKey;

    /// <summary>One bullet per thing the user would miss, so nothing goes without being named.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var lines = new List<string>();

            if (ModelCount > 0)
            {
                lines.Add($"{ModelCount} downloaded speech model{(ModelCount == 1 ? string.Empty : "s")}");
            }

            if (HistoryEntries > 0)
            {
                lines.Add($"{HistoryEntries} dictation{(HistoryEntries == 1 ? string.Empty : "s")} in the history");
            }

            if (HasApiKey)
            {
                lines.Add("your stored Anthropic API key");
            }

            lines.Add("your settings, vocabulary and window positions");
            return lines;
        }
    }
}

/// <summary>
/// Deletes everything TawkType keeps: models, history, settings, the encrypted key, the logs.
///
/// Two callers, and they want opposite things. The button in Settings asks first and reports what
/// happened. The uninstall hook cannot do either — Velopack's hooks may show no UI and are killed
/// after 30 seconds — so it runs <see cref="DeleteQuietly"/>, which asks nobody and throws at nobody.
/// Both go through <see cref="DataRemoval.Check"/> first; see gotcha 19 for why that matters here more
/// than anywhere else.
/// </summary>
public sealed class AppDataMaintenance
{
    private readonly ModelStorage _storage;
    private readonly DictationHistoryStore _history;
    private readonly IApiKeyStore _apiKeys;
    private readonly TranscriberRouter _router;
    private readonly ILogger<AppDataMaintenance> _logger;

    public AppDataMaintenance(
        ModelStorage storage,
        DictationHistoryStore history,
        IApiKeyStore apiKeys,
        TranscriberRouter router,
        ILogger<AppDataMaintenance> logger)
    {
        _storage = storage;
        _history = history;
        _apiKeys = apiKeys;
        _router = router;
        _logger = logger;
    }

    public static string DataDirectory => SettingsStore.AppDataDirectory;

    /// <summary>
    /// Where the application itself lives, which is never ours to delete. Velopack installs beside the
    /// data folder under a name three letters longer, so this is the value that keeps the two apart.
    /// </summary>
    public static string? InstallDirectory => Path.GetDirectoryName(Environment.ProcessPath);

    /// <summary>The logs folder. Inside the data folder, so deleting the data takes it too.</summary>
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>How much the log is using right now, for a line under the button that clears it.</summary>
    public static long LogBytes
    {
        get
        {
            if (!Directory.Exists(LogsDirectory))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(LogsDirectory))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // A file that vanished between the listing and the measuring. It is a hint, not an audit.
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Deletes the log files. Returns how many bytes went, and how many files would not go.
    ///
    /// A plain file enumeration rather than a folder delete, so this needs no
    /// <see cref="DataRemoval.Check"/>: nothing recursive happens and the folder itself stays, which
    /// matters because the logger keeps writing into it. It appends and closes per line rather than
    /// holding the file open, so a purge lands between writes; anything still locked is reported
    /// instead of being retried, because the next rotation clears it anyway.
    /// </summary>
    public (long Bytes, int Failures) PurgeLogs()
    {
        long freed = 0;
        var failures = 0;

        if (!Directory.Exists(LogsDirectory))
        {
            return (0, 0);
        }

        foreach (var file in Directory.EnumerateFiles(LogsDirectory))
        {
            try
            {
                var size = new FileInfo(file).Length;
                File.Delete(file);
                freed += size;
            }
            catch
            {
                failures++;
            }
        }

        _logger.LogInformation("Cleared the log ({Size})", ModelStorage.FormatSize(freed));
        return (freed, failures);
    }

    public AppDataContents Describe()
    {
        var models = 0;
        long bytes = 0;

        try
        {
            var entries = _storage.List();
            models = entries.Count;
            bytes = DirectorySize(DataDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not measure the data folder");
        }

        var entriesCount = 0;
        try
        {
            entriesCount = _history.Recent.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not count the history");
        }

        return new AppDataContents(bytes, models, entriesCount, _apiKeys.HasKey);
    }

    /// <summary>
    /// The Settings button: name everything that is about to go, take a yes, unload the engines so the
    /// model files are not held open, and delete. Returns true if anything was deleted.
    /// </summary>
    public async Task<bool> DeleteWithConfirmationAsync(Window? owner = null)
    {
        var check = DataRemoval.Check(DataDirectory, InstallDirectory);
        if (!check.CanDelete)
        {
            _logger.LogError("Refusing to delete {Path}: {Reason}", DataDirectory, check.Refusal);
            Show(owner, "TawkType will not delete that folder:\n\n" + check.Refusal, MessageBoxImage.Error);
            return false;
        }

        var contents = Describe();
        if (contents.IsEmpty)
        {
            Show(owner, "There is nothing stored yet.", MessageBoxImage.Information);
            return false;
        }

        var list = string.Join("\n", contents.Lines.Select(line => "  • " + line));

        // Only promise a second download when there is actually a model to lose. This dialog is the
        // last thing read before something irreversible, so every sentence in it has to be true.
        var afterwards = contents.ModelCount > 0
            ? "TawkType keeps working, but it starts again from nothing: the model has to be downloaded "
              + "a second time."
            : "TawkType keeps working; it starts again from nothing.";

        var message = $"Delete everything TawkType has kept — {contents.SizeText}?\n\n{list}\n\n{DataDirectory}\n\n"
            + afterwards + " This cannot be undone.";

        if (Show(owner, message, MessageBoxImage.Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            // Both engines hold their model files open; the history holds nothing, but unloading first
            // is what makes the delete succeed rather than half-succeed.
            await _router.UnloadAsync();
            var failures = DeleteContents(DataDirectory);

            _logger.LogInformation("Deleted the data folder ({Size})", contents.SizeText);

            if (failures > 0)
            {
                Show(
                    owner,
                    $"Most of it is gone, but {failures} file(s) could not be deleted — something else is "
                    + "using them. Closing TawkType and deleting the folder by hand will finish the job:\n\n"
                    + DataDirectory,
                    MessageBoxImage.Warning);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting the data folder failed");
            Show(owner, "Could not delete it:\n" + ex.Message, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// The uninstall hook's version: no dialogs, no exceptions, no dependencies. It runs inside
    /// Velopack's callback, which forbids UI and kills the process after 30 seconds, and by then
    /// there is nobody left to tell anything to.
    /// </summary>
    public static void DeleteQuietly(string dataDirectory, string? installDirectory, Action<string>? note = null)
    {
        try
        {
            var check = DataRemoval.Check(dataDirectory, installDirectory);
            if (!check.CanDelete)
            {
                note?.Invoke("refused: " + check.Refusal);
                return;
            }

            if (!Directory.Exists(dataDirectory))
            {
                note?.Invoke("nothing to delete at " + dataDirectory);
                return;
            }

            var failures = DeleteContents(dataDirectory);
            note?.Invoke(failures == 0
                ? "deleted " + dataDirectory
                : $"deleted {dataDirectory} except {failures} file(s) still in use");
        }
        catch (Exception ex)
        {
            // An uninstall that fails because of us is worse than data left on disk.
            note?.Invoke("failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Empties the folder and removes it, skipping whatever is locked rather than giving up at the
    /// first one. Returns how many entries survived.
    /// </summary>
    private static int DeleteContents(string directory)
    {
        var failures = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                failures++;
            }
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // The folder itself, or a subfolder, is in use. The files are what mattered.
        }

        return failures;
    }

    private static long DirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // Vanished or unreadable; it is a size estimate for a dialog, not an audit.
            }
        }

        return total;
    }

    private static MessageBoxResult Show(
        Window? owner,
        string text,
        MessageBoxImage image,
        MessageBoxButton buttons = MessageBoxButton.OK)
        => owner is null
            ? MessageBox.Show(text, "TawkType", buttons, image)
            : MessageBox.Show(owner, text, "TawkType", buttons, image);
}
