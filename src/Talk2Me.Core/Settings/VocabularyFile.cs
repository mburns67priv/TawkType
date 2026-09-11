using System.Text.Json;

namespace Talk2Me.Core.Settings;

/// <summary>
/// A vocabulary on its own, as JSON, for moving between machines or keeping a copy of.
///
/// Separate from settings.json because it is the part of Talk2Me that is genuinely the user's own
/// work — the names they have corrected and the text they have saved — and it should not be trapped
/// inside a file full of window positions and model choices.
/// </summary>
public static class VocabularyFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string Write(VocabularySettings vocabulary) => JsonSerializer.Serialize(vocabulary, Options);

    /// <summary>
    /// Reads one back, or null when the file is not one of these. Anything that parses as JSON but
    /// carries none of the three lists is rejected rather than silently imported as empty.
    /// </summary>
    public static VocabularySettings? Read(string json)
    {
        var loaded = JsonSerializer.Deserialize<VocabularySettings>(json, Options);

        if (loaded is null)
        {
            return null;
        }

        loaded.Spellings = (loaded.Spellings ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        loaded.Replacements = (loaded.Replacements ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
            .ToArray();
        loaded.Snippets = (loaded.Snippets ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Trigger) && !string.IsNullOrWhiteSpace(s.Text))
            .ToArray();

        var empty = loaded.Spellings.Length == 0 && loaded.Replacements.Length == 0 && loaded.Snippets.Length == 0;
        return empty ? null : loaded;
    }
}
