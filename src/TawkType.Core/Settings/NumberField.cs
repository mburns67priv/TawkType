using System.Globalization;

namespace TawkType.Core.Settings;

/// <summary>What a box was holding: a usable number, or the reason it was not.</summary>
/// <param name="Value">Only meaningful when <paramref name="Error"/> is null.</param>
/// <param name="Error">A sentence to show under the box, or null when the value is fine.</param>
public readonly record struct NumberResult(double Value, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// One numeric settings box: its range, its unit, and the message to show when what was typed is not
/// a number in that range.
///
/// The settings window used to drop a bad value on the floor — the box kept showing what you typed
/// while the setting quietly stayed as it was, so a mistyped timeout looked saved and was not. Every
/// numeric box now says what it wants instead, and Save waits until it gets it.
/// </summary>
/// <param name="Minimum">Inclusive.</param>
/// <param name="Maximum">Inclusive, and deliberately generous: it is here to catch a slipped digit or
/// a pasted phone number, not to argue with someone who wants an unusual value.</param>
/// <param name="Unit">Named in the error so the message stands on its own away from the label.</param>
/// <param name="Whole">Whether a fraction is allowed.</param>
public sealed record NumberField(double Minimum, double Maximum, string Unit, bool Whole = true)
{
    /// <summary>A tap shorter than this is not a dictation. Half a second is already a long tap.</summary>
    public static readonly NumberField MinimumHold = new(0, 5_000, "milliseconds");

    /// <summary>0 turns the safety net off, which the engine already treats as no limit.</summary>
    public static readonly NumberField RecordingLimit = new(0, 600, "minutes", Whole: false);

    /// <summary>Below about a quarter of a second no cleanup call can finish, so it would only ever time out.</summary>
    public static readonly NumberField CleanupTimeout = new(250, 600_000, "milliseconds");

    /// <summary>Keeping none of them is what the Clear button is for.</summary>
    public static readonly NumberField HistoryEntries = new(1, 1_000_000, "entries");

    public string Format(double value)
        => Whole
            ? ((long)Math.Round(value)).ToString(CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);

    public NumberResult Parse(string? text)
    {
        text = text?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return new NumberResult(0, $"Enter a number of {Unit}.");
        }

        if (!TryParse(text, out var value))
        {
            return new NumberResult(0, $"\"{text}\" is not a number.");
        }

        if (Whole && value != Math.Floor(value))
        {
            return new NumberResult(0, $"Use a whole number of {Unit}.");
        }

        if (value < Minimum || value > Maximum)
        {
            return new NumberResult(0, $"Enter between {Format(Minimum)} and {Format(Maximum)} {Unit}.");
        }

        return new NumberResult(value, null);
    }

    /// <summary>
    /// The user's own culture first, then the invariant one, so someone on a comma-decimal machine who
    /// has typed "2.5" out of habit is understood rather than told off.
    /// </summary>
    private static bool TryParse(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
