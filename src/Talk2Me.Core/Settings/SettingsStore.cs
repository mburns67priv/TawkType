using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;

namespace Talk2Me.Core.Settings;

/// <summary>Loads and saves <see cref="Talk2MeSettings"/> from %LOCALAPPDATA%/Talk2Me/settings.json.</summary>
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
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talk2Me");

    public static string DefaultPath { get; } = System.IO.Path.Combine(AppDataDirectory, "settings.json");

    public string Path { get; }

    public Talk2MeSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(Talk2MeSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        Current = settings.Clone();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Talk2MeSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<Talk2MeSettings>(File.ReadAllText(Path), JsonOptions);
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

        return new Talk2MeSettings();
    }
}
