using System.Windows;
using Microsoft.Extensions.Logging;
using Murmur.Transcription;

namespace Murmur.Desktop.Services;

/// <summary>Shared by the Settings window and the tray menu: describe and delete downloaded models.</summary>
public sealed class ModelMaintenance
{
    private readonly ModelStorage _storage;
    private readonly TranscriberRouter _router;
    private readonly ILogger<ModelMaintenance> _logger;

    public ModelMaintenance(ModelStorage storage, TranscriberRouter router, ILogger<ModelMaintenance> logger)
    {
        _storage = storage;
        _router = router;
        _logger = logger;
    }

    public string ModelsDirectory => _storage.ModelsDirectory;

    public string Describe()
    {
        var entries = _storage.List();
        if (entries.Count == 0)
        {
            return "No models downloaded yet.";
        }

        var parts = entries.Select(e => $"{e.Name} ({ModelStorage.FormatSize(e.Bytes)})");
        return $"{ModelStorage.FormatSize(_storage.GetTotalBytes())} on disk: {string.Join(", ", parts)}";
    }

    /// <summary>Asks the user, unloads both engines, deletes the models folder. Returns true if deleted.</summary>
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
        if (Show(owner, message, MessageBoxImage.Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            await _router.UnloadAsync();
            _storage.DeleteAll();
            _logger.LogInformation("Deleted downloaded models ({Size})", ModelStorage.FormatSize(bytes));
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
            ? MessageBox.Show(text, "Murmur", buttons, image)
            : MessageBox.Show(owner, text, "Murmur", buttons, image);
}
