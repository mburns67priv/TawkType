namespace Murmur.Core.Settings;

/// <summary>Decides which speech engine handles a given language. Pure so it can be unit tested.</summary>
public static class EngineSelection
{
    /// <summary>The 25 languages covered by Parakeet TDT 0.6B v3 (ISO 639-1).</summary>
    public static IReadOnlySet<string> ParakeetLanguages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de", "el", "hu", "it",
        "lv", "lt", "mt", "pl", "pt", "ro", "sk", "sl", "es", "sv", "ru", "uk",
    };

    public static bool ParakeetSupports(string? language)
        => !string.IsNullOrWhiteSpace(language) && ParakeetLanguages.Contains(language.Trim());

    /// <summary>Resolves <see cref="TranscriptionEngine.Auto"/> into a concrete engine for the language.</summary>
    public static TranscriptionEngine Resolve(TranscriptionEngine requested, string? language) => requested switch
    {
        TranscriptionEngine.Parakeet => TranscriptionEngine.Parakeet,
        TranscriptionEngine.Whisper => TranscriptionEngine.Whisper,
        _ => ParakeetSupports(language) ? TranscriptionEngine.Parakeet : TranscriptionEngine.Whisper,
    };
}
