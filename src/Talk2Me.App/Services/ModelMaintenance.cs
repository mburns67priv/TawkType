using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;
using Talk2Me.Transcription;

namespace Talk2Me.Desktop.Services;

/// <summary>One downloaded model, described for the UI.</summary>
/// <param name="Name">The storage key passed back to <see cref="ModelMaintenance.DeleteAsync"/>.</param>
/// <param name="Label">Friendly name, e.g. "Whisper large-v3-turbo".</param>
/// <param name="InUse">True when the current settings would load this model on the next dictation.</param>
public sealed record InstalledModel(string Name, string Label, long Bytes, bool InUse)
{
    public string SizeText => ModelStorage.FormatSize(Bytes);
}

/// <summary>Shared by the Settings window and the tray menu: describe and delete downloaded models.</summary>
public sealed class ModelMaintenance
{
    private readonly ModelStorage _storage;
    private readonly ModelManager _whisperModels;
    private readonly TranscriberRouter _router;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<ModelMaintenance> _logger;

    public ModelMaintenance(
        ModelStorage storage,
        ModelManager whisperModels,
        TranscriberRouter router,
        ISettingsProvider settings,
        ILogger<ModelMaintenance> logger)
    {
        _storage = storage;
        _whisperModels = whisperModels;
        _router = router;
        _settings = settings;
        _logger = logger;
    }

    public string ModelsDirectory => _storage.ModelsDirectory;

    public string Describe()
    {
        var total = _storage.GetTotalBytes();
        return total == 0
            ? "No models downloaded yet."
            : $"{ModelStorage.FormatSize(total)} on disk.";
    }

    /// <summary>Everything under the models folder, newest naming resolved to friendly labels.</summary>
    public IReadOnlyList<InstalledModel> List()
    {
        var active = _router.ActiveEngine;
        var activeWhisperFile = Path.GetFileName(
            _whisperModels.GetModelPath(ModelManager.ParseModelType(_settings.Current.Model)));

        return _storage.List()
            .Select(entry => new InstalledModel(
                entry.Name,
                Label(entry),
                entry.Bytes,
                InUse: entry.IsDirectory
                    ? active == TranscriptionEngine.Parakeet
                    : active == TranscriptionEngine.Whisper
                      && string.Equals(entry.Name, activeWhisperFile, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>Asks the user, unloads both engines, deletes the named models. Returns true if anything went.</summary>
    public async Task<bool> DeleteAsync(IReadOnlyList<InstalledModel> models, Window? owner = null)
    {
        if (models.Count == 0)
        {
            Show(owner, "Select the models you want to delete first.", MessageBoxImage.Information, MessageBoxButton.OK);
            return false;
        }

        var bytes = models.Sum(m => m.Bytes);
        var list = string.Join("\n", models.Select(m => $"  • {m.Label} ({m.SizeText})"));
        var message = $"Delete {models.Count} model{(models.Count == 1 ? string.Empty : "s")}, "
            + $"{ModelStorage.FormatSize(bytes)}?\n\n{list}\n\n{_storage.ModelsDirectory}";

        if (models.Any(m => m.InUse))
        {
            message += "\n\nOne of these is the model your current settings use. It will download again "
                + "the next time you dictate.";
        }

        return await ConfirmAndDeleteAsync(
            message,
            owner,
            () =>
            {
                foreach (var model in models)
                {
                    _storage.Delete(model.Name);
                }

                _logger.LogInformation(
                    "Deleted {Count} model(s) ({Size}): {Names}",
                    models.Count,
                    ModelStorage.FormatSize(bytes),
                    string.Join(", ", models.Select(m => m.Name)));
            });
    }

    /// <summary>The tray shortcut: everything at once.</summary>
    public async Task<bool> DeleteAllWithConfirmationAsync(Window? owner = null)
    {
        var bytes = _storage.GetTotalBytes();
        if (bytes == 0)
        {
            Show(owner, "There are no downloaded models to delete.", MessageBoxImage.Information, MessageBoxButton.OK);
            return false;
        }

        var message =
            $"Delete {ModelStorage.FormatSize(bytes)} of downloaded models?\n\n{_storage.ModelsDirectory}\n\n" +
            "The active engine will download its model again the next time you dictate.";

        return await ConfirmAndDeleteAsync(
            message,
            owner,
            () =>
            {
                _storage.DeleteAll();
                _logger.LogInformation("Deleted all downloaded models ({Size})", ModelStorage.FormatSize(bytes));
            });
    }

    private async Task<bool> ConfirmAndDeleteAsync(string message, Window? owner, Action delete)
    {
        if (Show(owner, message, MessageBoxImage.Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            // Both engines hold their model files open; unload before touching the disk.
            await _router.UnloadAsync();
            delete();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting models failed");
            Show(owner, "Could not delete the models:\n" + ex.Message, MessageBoxImage.Error, MessageBoxButton.OK);
            return false;
        }
    }

    private static MessageBoxResult Show(Window? owner, string text, MessageBoxImage image, MessageBoxButton buttons)
        => owner is null
            ? MessageBox.Show(text, "Talk2Me", buttons, image)
            : MessageBox.Show(owner, text, "Talk2Me", buttons, image);

    private static string Label(ModelEntry entry)
    {
        if (entry.Name.Equals(ParakeetModelManager.ModelName, StringComparison.OrdinalIgnoreCase))
        {
            return "Parakeet TDT 0.6B v3 (int8)";
        }

        // ggml-large-v3-turbo.bin -> Whisper large-v3-turbo
        if (entry.Name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return "Whisper " + entry.Name[5..^4];
        }

        return entry.Name;
    }
}
