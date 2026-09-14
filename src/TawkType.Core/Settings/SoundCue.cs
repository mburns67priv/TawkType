namespace TawkType.Core.Settings;

/// <summary>
/// The rules about sound cues, with no Windows in them, so they can be tested.
///
/// Everything here is about a cue *name* and a volume number. Finding the .wav a name points at, and
/// making a noise with it, is the Windows layer's job.
/// </summary>
public static class SoundCue
{
    /// <summary>A cue that plays nothing. Stored as an empty string, so an absent setting means silence.</summary>
    public const string Silent = "";

    /// <summary>
    /// Windows' generic notification. A soft two-note chime rather than an alert, which is what a
    /// dictation starting should sound like.
    /// </summary>
    public const string DefaultStart = "Notification.Default";

    /// <summary>
    /// Silence, deliberately. The start cue tells you the thing is listening, which is the half worth
    /// knowing; a sound at both ends of every sentence is twice as much sound as anybody wants, and
    /// the text arriving is its own announcement.
    /// </summary>
    public const string DefaultStop = Silent;

    /// <summary>Windows' critical stop. A failure is the one case where an alert is the right sound.</summary>
    public const string DefaultError = "SystemHand";

    public const int DefaultVolume = 70;

    public const int MinimumVolume = 0;

    public const int MaximumVolume = 100;

    public static bool IsSilent(string? cue) => string.IsNullOrWhiteSpace(cue);

    /// <summary>Trims a cue name and turns anything blank into <see cref="Silent"/>.</summary>
    public static string Normalise(string? cue) => IsSilent(cue) ? Silent : cue!.Trim();

    public static int ClampVolume(int volume) => Math.Clamp(volume, MinimumVolume, MaximumVolume);

    /// <summary>
    /// The slider position as a gain to multiply samples by.
    ///
    /// Squared, not linear. Loudness is roughly logarithmic, so a linear gain puts almost all of the
    /// audible change in the bottom of the slider and makes the top half feel dead: halfway sounds
    /// nearly as loud as full. Squaring spreads the useful range across the whole travel, and is the
    /// same curve a volume control anywhere else uses.
    /// </summary>
    public static float Gain(int volume)
    {
        var scaled = ClampVolume(volume) / (float)MaximumVolume;
        return scaled * scaled;
    }

    /// <summary>
    /// The few sounds worth offering first, in this order.
    ///
    /// The scheme is 40-odd events sorted by label, which opens on "Alarm 1, Alarm 10, Alarm 2" and
    /// buries the handful anybody would actually pick behind a wall of alarms. These are the soft
    /// notification and device chimes — the ones that say something happened without saying something
    /// is wrong, which is the whole complaint that started this.
    ///
    /// Names that are not in the user's scheme are skipped, so a trimmed-down or third-party scheme
    /// simply gets fewer suggestions rather than dead entries.
    /// </summary>
    public static readonly string[] Suggested =
    [
        "Notification.Default",
        "Notification.IM",
        "DeviceConnect",
        "DeviceDisconnect",
        ".Default",
    ];

    /// <summary>
    /// Where a sound sits in the list. Suggested ones first in their own order, everything else after.
    /// </summary>
    public static int Rank(string? eventName)
    {
        var name = Normalise(eventName);

        for (var i = 0; i < Suggested.Length; i++)
        {
            if (string.Equals(Suggested[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Compares two labels the way a person reads them, so "Alarm 2" comes before "Alarm 10".
    ///
    /// Plain alphabetical ordering compares the "1" of 10 against the "2" of 2 and puts them the wrong
    /// way round. Windows' own sound list is full of numbered families, so this is not a detail.
    /// </summary>
    public static int CompareLabels(string? left, string? right)
    {
        var a = left ?? string.Empty;
        var b = right ?? string.Empty;
        int i = 0, j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                // Compared as text with leading zeroes stripped, so a run of digits longer than any
                // int cannot throw and "007" still equals "7".
                var numberA = a[startA..i].TrimStart('0');
                var numberB = b[startB..j].TrimStart('0');

                if (numberA.Length != numberB.Length)
                {
                    return numberA.Length - numberB.Length;
                }

                var digits = string.CompareOrdinal(numberA, numberB);
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            var letters = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
            if (letters != 0)
            {
                return letters;
            }

            i++;
            j++;
        }

        return (a.Length - i) - (b.Length - j);
    }

    /// <summary>
    /// What the slider says next to it. Percentages invite the question "percent of what", and the
    /// answer here is "of whatever this .wav was recorded at", which is not worth a number.
    /// </summary>
    public static string DescribeVolume(int volume) => ClampVolume(volume) switch
    {
        0 => "Muted",
        <= 25 => "Quiet",
        <= 70 => "Medium",
        <= 90 => "Loud",
        _ => "Full",
    };
}
