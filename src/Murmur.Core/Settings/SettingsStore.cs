using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Settings;

/// <summary>Loads and saves <see cref="MurmurSettings"/> from %LOCALAPPDATA%/Murmur/settings.json.</summary>
public sealed class SettingsStore : ISettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(logger, DefaultPath)
    {
    }

    public SettingsStore(ILogger<SettingsStore> logger, string path)
    {
        _logger = logger;
        Path = path;
        Current = Load();
    }

    public static string AppDataDirectory { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");

    public static string DefaultPath { get; } = System.IO.Path.Combine(AppDataDirectory, "settings.json");

    public string Path { get; }

    public MurmurSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(MurmurSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        Current = settings.Clone();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MurmurSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<MurmurSettings>(File.ReadAllText(Path), JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read settings from {Path}; using defaults", Path);
        }

        return new MurmurSettings();
    }
}
