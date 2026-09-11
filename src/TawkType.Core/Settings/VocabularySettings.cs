namespace TawkType.Core.Settings;

/// <summary>A phrase the recogniser produces, and the exact text it should become.</summary>
/// <param name="From">What was heard, e.g. "talk to me".</param>
/// <param name="To">What to type instead, e.g. "TawkType". Used verbatim, casing and all.</param>
public sealed record TextReplacement(string From = "", string To = "");

/// <summary>Saved text, inserted by saying "insert" and the trigger.</summary>
/// <param name="Trigger">What follows "insert", e.g. "my signature".</param>
/// <param name="Text">Exactly what to type. May run to several lines.</param>
public sealed record Snippet(string Trigger = "", string Text = "");

/// <summary>
/// Words TawkType should get right, and text it can type for you. All of it applies locally, with no
/// model involved: someone who never turns the Claude pass on still has to be able to stop correcting
/// the same name every day.
/// </summary>
public sealed class VocabularySettings
{
    /// <summary>
    /// Names, products and acronyms written the way they should be typed — "TawkType", "Jupitor Studio".
    /// Matched however they were capitalised and rewritten to exactly this.
    /// </summary>
    public string[] Spellings { get; set; } = [];

    /// <summary>Heard-this, type-that. For the mishearings a spelling cannot fix.</summary>
    public TextReplacement[] Replacements { get; set; } = [];

    /// <summary>Signatures, addresses, templates: saved text, spoken for.</summary>
    public Snippet[] Snippets { get; set; } = [];

    /// <summary>
    /// This vocabulary with another laid on top — a mode's own words added to the main list.
    ///
    /// Additive, and the extra list goes first so that where both name the same phrase, the more
    /// specific one wins: a technical mode should be able to say what "the client" means without the
    /// user having to remove their general entry for it.
    /// </summary>
    public VocabularySettings With(VocabularySettings? extra)
    {
        if (extra is null || (extra.Spellings.Length == 0 && extra.Replacements.Length == 0 && extra.Snippets.Length == 0))
        {
            return this;
        }

        return new VocabularySettings
        {
            Spellings = [.. extra.Spellings, .. Spellings],
            Replacements = [.. extra.Replacements, .. Replacements],
            Snippets = [.. extra.Snippets, .. Snippets],
        };
    }

    public VocabularySettings Clone() => new()
    {
        Spellings = (string[])Spellings.Clone(),
        Replacements = Replacements.Select(r => r with { }).ToArray(),
        Snippets = Snippets.Select(s => s with { }).ToArray(),
    };
}
