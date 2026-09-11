using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;

namespace TawkType.Core.Settings;

/// <summary>Loads and saves <see cref="TawkTypeSettings"/> from the app data folder's settings.json.</summary>
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

    /// <summary>
    /// %LOCALAPPDATA%\TawkType.
    ///
    /// Deliberately *not* the folder the installer extracts into. Velopack installs to
    /// %LOCALAPPDATA%\&lt;packId&gt; and clears it first, so the two must never be the same path — the
    /// packId is <c>TawkTypeApp</c> precisely so this one can be <c>TawkType</c>. Getting that wrong
    /// destroys settings, history, an encrypted API key and gigabytes of models on every install;
    /// it has happened once already. <c>DataFolderTests</c> asserts they stay apart.
    /// </summary>
    public static string AppDataDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TawkType");

    public static string DefaultPath { get; } = System.IO.Path.Combine(AppDataDirectory, "settings.json");

    public string Path { get; }

    public TawkTypeSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(TawkTypeSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        Current = settings.Clone();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private TawkTypeSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<TawkTypeSettings>(File.ReadAllText(Path), JsonOptions);
                if (loaded is not null)
                {
                    return Migrate(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read settings from {Path}; using defaults", Path);
        }

        return new TawkTypeSettings();
    }

    /// <summary>
    /// Brings older files forward. The vocabulary used to live under AI cleanup, because that was the
    /// only thing that read it; it applies locally now, so it belongs to the app rather than to the
    /// model. The old list is left in place, so downgrading does not lose it.
    /// </summary>
    public static TawkTypeSettings Migrate(TawkTypeSettings loaded)
    {
        loaded.Vocabulary ??= new VocabularySettings();
        loaded.Cleanup ??= new CleanupSettings();

        if (loaded.Vocabulary.Spellings.Length == 0 && loaded.Cleanup.Vocabulary.Length > 0)
        {
            loaded.Vocabulary.Spellings = (string[])loaded.Cleanup.Vocabulary.Clone();
        }

        // Filler removal, the trailing space and caret fitting used to be single settings for the
        // whole app. They belong to a mode now, so a file written before modes existed hands its
        // three answers to every mode rather than losing them — a user who had turned the trailing
        // space off must not find it back on because this version reorganised where it lives.
        if (loaded.Modes is null || loaded.Modes.Length == 0)
        {
            loaded.Modes = DictationModes.BuiltIn();

            foreach (var mode in loaded.Modes)
            {
                mode.RemoveFillerWords &= loaded.RemoveFillerWords;
                mode.AppendTrailingSpace &= loaded.AppendTrailingSpace;
                mode.FitToCaret &= loaded.FitToCaret;
            }
        }

        if (string.IsNullOrWhiteSpace(loaded.ActiveMode))
        {
            loaded.ActiveMode = DictationModes.CleanProse;
        }

        return loaded;
    }
}
