using Talk2Me.Core.Settings;

namespace Talk2Me.Transcription;

/// <summary>One downloaded model: a Whisper ggml file, or the Parakeet folder.</summary>
/// <param name="Name">The entry's name directly under the models directory. Also the delete key.</param>
public sealed record ModelEntry(string Name, long Bytes, bool IsDirectory);

/// <summary>Reports on and clears everything under %LOCALAPPDATA%/Talk2Me/models (Whisper ggml files and the Parakeet folder).</summary>
public sealed class ModelStorage
{
    public ModelStorage()
        : this(Path.Combine(SettingsStore.AppDataDirectory, "models"))
    {
    }

    /// <summary>Overload for tests, which point at a temporary directory.</summary>
    public ModelStorage(string modelsDirectory)
    {
        ModelsDirectory = modelsDirectory;
    }

    public string ModelsDirectory { get; }

    public long GetTotalBytes()
    {
        if (!Directory.Exists(ModelsDirectory))
        {
            return 0;
        }

        return new DirectoryInfo(ModelsDirectory)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    /// <summary>Top-level entries: one per Whisper model file and one per Parakeet folder.</summary>
    public IReadOnlyList<ModelEntry> List()
    {
        if (!Directory.Exists(ModelsDirectory))
        {
            return Array.Empty<ModelEntry>();
        }

        var directory = new DirectoryInfo(ModelsDirectory);
        var files = directory.EnumerateFiles()
            .Select(f => new ModelEntry(f.Name, f.Length, IsDirectory: false));
        var folders = directory.EnumerateDirectories()
            .Select(d => new ModelEntry(
                d.Name,
                d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length),
                IsDirectory: true));
        return files.Concat(folders).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Deletes one entry returned by <see cref="List"/>. Unload the engines first so no file is still
    /// mapped. Returns false when the entry is already gone.
    /// </summary>
    public bool Delete(string name)
    {
        // Only ever delete something List() actually reported: never a caller-composed path.
        var entry = List().FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return false;
        }

        var path = Path.Combine(ModelsDirectory, entry.Name);

        if (entry.IsDirectory)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }

        return true;
    }

    /// <summary>Deletes every downloaded model. Unload the engines first so no file is still mapped.</summary>
    public void DeleteAll()
    {
        if (Directory.Exists(ModelsDirectory))
        {
            Directory.Delete(ModelsDirectory, recursive: true);
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F0} MB",
        _ => $"{bytes / 1024.0:F0} KB",
    };
}
