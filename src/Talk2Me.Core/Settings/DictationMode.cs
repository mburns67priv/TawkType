namespace Talk2Me.Core.Settings;

/// <summary>
/// A named bundle of post-processing choices, picked per dictation rather than per session.
///
/// Changing settings and choosing how the next dictation should behave are not the same act. Settings
/// are a decision made once; a mode is a decision made because of what is about to be said, and it has
/// to be switchable in the second before saying it.
///
/// Everything here is local. A mode never reaches a model on its own — see <see cref="MayUseLlm"/>.
/// </summary>
public sealed class DictationMode
{
    public string Name { get; set; } = "Clean prose";

    /// <summary>"um", "uh", "erm" and friends.</summary>
    public bool RemoveFillerWords { get; set; } = true;

    /// <summary>
    /// Capitalise the first letter. Off for a mode meant to preserve what the recogniser produced, and
    /// off for chat, where a forced capital reads as stiffer than the medium.
    /// </summary>
    public bool Capitalise { get; set; } = true;

    public bool AppendTrailingSpace { get; set; } = true;

    public bool FitToCaret { get; set; } = true;

    /// <summary>
    /// Whether this mode is *allowed* to use the Claude pass — never whether it turns it on. Choosing
    /// a mode must not authorise sending anything anywhere; only the AI cleanup setting does that, and
    /// this can only ever narrow it.
    /// </summary>
    public bool MayUseLlm { get; set; } = true;

    /// <summary>Only consulted when the Claude pass actually runs.</summary>
    public CleanupStyle Style { get; set; } = CleanupStyle.Natural;

    /// <summary>
    /// Words and phrases this mode adds to the main vocabulary — a technical mode's identifiers, say.
    /// Additive rather than a replacement: corrections the user has taught Talk2Me should not stop
    /// applying because they picked a different mode.
    /// </summary>
    public VocabularySettings Vocabulary { get; set; } = new();

    /// <summary>Built-in, so it cannot be deleted and its name cannot be edited away.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Its name. Worth overriding rather than leaving as the type name: a list of these is what the
    /// mode pickers show, and it is what screen readers and UI Automation read out.
    /// </summary>
    public override string ToString() => Name;

    public DictationMode Clone() => new()
    {
        Name = Name,
        RemoveFillerWords = RemoveFillerWords,
        Capitalise = Capitalise,
        AppendTrailingSpace = AppendTrailingSpace,
        FitToCaret = FitToCaret,
        MayUseLlm = MayUseLlm,
        Style = Style,
        Vocabulary = Vocabulary.Clone(),
        IsBuiltIn = IsBuiltIn,
    };
}

/// <summary>
/// The four modes Talk2Me ships with, and the rules for picking between them.
/// </summary>
public static class DictationModes
{
    public const string CleanProse = "Clean prose";

    /// <summary>
    /// "Literal" means preserving what the recogniser produced. It is not a promise to recover
    /// everything that was said — no engine can offer that, and saying so would be a lie in a label.
    /// </summary>
    public static DictationMode[] BuiltIn() =>
    [
        new()
        {
            Name = "Literal",
            RemoveFillerWords = false,
            Capitalise = false,
            MayUseLlm = false,
            IsBuiltIn = true,
        },
        new()
        {
            Name = CleanProse,
            IsBuiltIn = true,
        },
        new()
        {
            Name = "Chat",
            Capitalise = false,
            AppendTrailingSpace = false,
            Style = CleanupStyle.Casual,
            IsBuiltIn = true,
        },
        new()
        {
            Name = "Technical",
            Capitalise = false,
            Style = CleanupStyle.Verbatim,
            IsBuiltIn = true,
        },
    ];

    /// <summary>
    /// The mode with this name, or the first one there is. Never null: a settings file naming a mode
    /// that has since been deleted must still dictate.
    /// </summary>
    public static DictationMode Resolve(IReadOnlyList<DictationMode> modes, string? name)
    {
        if (modes.Count == 0)
        {
            return new DictationMode();
        }

        return modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.CurrentCultureIgnoreCase))
            ?? modes.FirstOrDefault(m => m.Name == CleanProse)
            ?? modes[0];
    }

    /// <summary>The next one round, so one key can cycle them. Wraps, and copes with an unknown name.</summary>
    public static DictationMode Next(IReadOnlyList<DictationMode> modes, string? current)
    {
        if (modes.Count == 0)
        {
            return new DictationMode();
        }

        var at = modes.ToList().FindIndex(m =>
            string.Equals(m.Name, current, StringComparison.CurrentCultureIgnoreCase));

        return modes[(at + 1) % modes.Count];
    }
}
