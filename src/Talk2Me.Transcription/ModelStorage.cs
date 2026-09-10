using Talk2Me.Core.Settings;

namespace Talk2Me.Transcription;

/// <summary>Reports on and clears everything under %LOCALAPPDATA%/Talk2Me/models (Whisper ggml files and the Parakeet folder).</summary>
public sealed class ModelStorage
{
    public string ModelsDirectory { get; } = Path.Combine(SettingsStore.AppDataDirectory, "models");

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
    public IReadOnlyList<(string Name, long Bytes)> List()
    {
        if (!Directory.Exists(ModelsDirectory))
        {
            return Array.Empty<(string, long)>();
        }

        var directory = new DirectoryInfo(ModelsDirectory);
        var files = directory.EnumerateFiles().Select(f => (f.Name, f.Length));
        var folders = directory.EnumerateDirectories()
            .Select(d => (d.Name, d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)));
        return files.Concat(folders).OrderBy(e => e.Item1).ToArray();
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
