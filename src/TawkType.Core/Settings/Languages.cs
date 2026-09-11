namespace TawkType.Core.Settings;

/// <summary>One language the user can pick, as a code to store and a name to show.</summary>
public sealed record Language(string Code, string Name)
{
    /// <summary>The picker shows this; the templated ComboBox ignores DisplayMemberPath.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// The languages TawkType offers, which is what Whisper understands — Parakeet's 25 are a subset, and
/// it works the language out for itself anyway. Codes are ISO 639-1 so they mean the same thing to
/// both engines and to anyone who edits settings.json by hand.
/// </summary>
public static class Languages
{
    /// <summary>Stored when the user wants the engine to work it out.</summary>
    public const string AutoCode = "auto";

    public static Language Auto { get; } = new(AutoCode, "Detect automatically");

    /// <summary>
    /// Offered above the rest because between them they cover most of the world's speakers. Ordered
    /// deliberately rather than alphabetically; change the order here and the picker follows.
    /// </summary>
    public static IReadOnlyList<string> MostSpokenCodes { get; } = ["en", "zh", "hi", "es", "ar"];

    /// <summary>Every language, by name, A to Z. Does not include <see cref="Auto"/>.</summary>
    public static IReadOnlyList<Language> All { get; } = Catalogue()
        .OrderBy(language => language.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    /// <summary>The five above, in the order of <see cref="MostSpokenCodes"/>.</summary>
    public static IReadOnlyList<Language> MostSpoken { get; } = MostSpokenCodes
        .Select(code => All.First(language => language.Code == code))
        .ToArray();

    /// <summary>Everything not already offered at the top, still A to Z.</summary>
    public static IReadOnlyList<Language> Rest { get; } = All
        .Where(language => !MostSpokenCodes.Contains(language.Code))
        .ToArray();

    /// <summary>The language with this code, or null. Case- and whitespace-insensitive.</summary>
    public static Language? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        return string.Equals(trimmed, AutoCode, StringComparison.OrdinalIgnoreCase)
            ? Auto
            : All.FirstOrDefault(language => string.Equals(language.Code, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static Language[] Catalogue() =>
    [
        new("af", "Afrikaans"),
        new("sq", "Albanian"),
        new("am", "Amharic"),
        new("ar", "Arabic"),
        new("hy", "Armenian"),
        new("as", "Assamese"),
        new("az", "Azerbaijani"),
        new("ba", "Bashkir"),
        new("eu", "Basque"),
        new("be", "Belarusian"),
        new("bn", "Bengali"),
        new("bs", "Bosnian"),
        new("br", "Breton"),
        new("bg", "Bulgarian"),
        new("my", "Burmese"),
        new("ca", "Catalan"),
        new("zh", "Chinese"),
        new("hr", "Croatian"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("nl", "Dutch"),
        new("en", "English"),
        new("et", "Estonian"),
        new("fo", "Faroese"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("gl", "Galician"),
        new("ka", "Georgian"),
        new("de", "German"),
        new("el", "Greek"),
        new("gu", "Gujarati"),
        new("ht", "Haitian Creole"),
        new("ha", "Hausa"),
        new("haw", "Hawaiian"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hu", "Hungarian"),
        new("is", "Icelandic"),
        new("id", "Indonesian"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("jw", "Javanese"),
        new("kn", "Kannada"),
        new("kk", "Kazakh"),
        new("km", "Khmer"),
        new("ko", "Korean"),
        new("lo", "Lao"),
        new("la", "Latin"),
        new("lv", "Latvian"),
        new("ln", "Lingala"),
        new("lt", "Lithuanian"),
        new("lb", "Luxembourgish"),
        new("mk", "Macedonian"),
        new("mg", "Malagasy"),
        new("ms", "Malay"),
        new("ml", "Malayalam"),
        new("mt", "Maltese"),
        new("mi", "Maori"),
        new("mr", "Marathi"),
        new("mn", "Mongolian"),
        new("ne", "Nepali"),
        new("no", "Norwegian"),
        new("nn", "Norwegian Nynorsk"),
        new("oc", "Occitan"),
        new("ps", "Pashto"),
        new("fa", "Persian"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("pa", "Punjabi"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sa", "Sanskrit"),
        new("sr", "Serbian"),
        new("sn", "Shona"),
        new("sd", "Sindhi"),
        new("si", "Sinhala"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("so", "Somali"),
        new("es", "Spanish"),
        new("su", "Sundanese"),
        new("sw", "Swahili"),
        new("sv", "Swedish"),
        new("tl", "Tagalog"),
        new("tg", "Tajik"),
        new("ta", "Tamil"),
        new("tt", "Tatar"),
        new("te", "Telugu"),
        new("th", "Thai"),
        new("bo", "Tibetan"),
        new("tr", "Turkish"),
        new("tk", "Turkmen"),
        new("uk", "Ukrainian"),
        new("ur", "Urdu"),
        new("uz", "Uzbek"),
        new("vi", "Vietnamese"),
        new("cy", "Welsh"),
        new("yi", "Yiddish"),
        new("yo", "Yoruba"),
    ];
}
