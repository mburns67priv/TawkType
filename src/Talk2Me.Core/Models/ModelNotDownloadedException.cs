namespace Talk2Me.Core.Models;

/// <summary>
/// The engine the user picked has no model on disk, and Talk2Me will not fetch one behind their back:
/// these are hundreds of megabytes, and a dictation app that silently starts a long download the first
/// time you hold the hotkey is not a good citizen. Settings → Transcription has the button.
/// </summary>
public sealed class ModelNotDownloadedException(string engine)
    : Exception($"The {engine} model is not downloaded. Download it in Settings → Transcription.")
{
    /// <summary>Friendly engine name, for messages that want to name it.</summary>
    public string Engine { get; } = engine;
}
