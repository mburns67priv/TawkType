namespace Talk2Me.Core.Text;

/// <summary>
/// The text either side of where a dictation is about to land, as much of it as could be read.
///
/// Everything here is optional and best-effort. Accessibility data is patchy, the read is bounded by a
/// timeout, and plenty of controls expose nothing at all — so <see cref="Unknown"/> has to be an
/// ordinary outcome that produces exactly the behaviour Talk2Me had before any of this existed.
/// </summary>
/// <param name="Before">Text immediately before the caret, or null when it could not be read.</param>
/// <param name="After">Text immediately after the caret, or null when it could not be read.</param>
/// <param name="HasSelection">True when text is selected, and typing will therefore replace it.</param>
public sealed record CaretContext(string? Before, string? After, bool HasSelection = false)
{
    public static CaretContext Unknown { get; } = new(null, null);

    /// <summary>Nothing was readable, so every decision falls back to what it was before.</summary>
    public bool IsUnknown => Before is null && After is null;
}
