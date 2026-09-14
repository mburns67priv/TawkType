using System.IO;
using Microsoft.Win32;
using TawkType.Core.Settings;

namespace TawkType.Windows.Audio;

/// <summary>One sound the user's Windows scheme has an entry for.</summary>
/// <param name="EventName">The registry key, e.g. <c>Notification.Default</c>. What gets stored.</param>
/// <param name="Label">What Windows calls it in the Sound control panel, e.g. "Notification".</param>
/// <param name="Path">The .wav it currently points at.</param>
public readonly record struct SchemeSound(string EventName, string Label, string Path);

/// <summary>
/// The user's Windows sound scheme, read from the registry.
///
/// <c>System.Media.SystemSounds</c> exposes five sounds and every one of them is an alert, which is
/// why TawkType used to sound like an error however it was configured. The scheme itself holds
/// dozens, each with a name the user already recognises from the Sound control panel, and several of
/// them are the soft notification chimes this wants.
///
/// It also gives us the .wav path, which is the only way to play a Windows sound at a chosen volume:
/// <c>SystemSound.Play()</c> has no level control at all.
///
/// Read fresh rather than cached at startup. Changing a sound scheme is exactly the kind of thing
/// somebody does *because* they are setting this up, and a list captured at launch would not show it.
/// </summary>
public static class WindowsSoundScheme
{
    private const string AppsKey = @"AppEvents\Schemes\Apps\.Default";

    private const string LabelsKey = @"AppEvents\EventLabels";

    /// <summary>
    /// Every event that currently has a sound assigned, by label. Events set to "(None)" are left
    /// out: they are silence, and TawkType has its own way of saying silence.
    /// </summary>
    public static IReadOnlyList<SchemeSound> List()
    {
        var found = new List<SchemeSound>();

        try
        {
            using var apps = Registry.CurrentUser.OpenSubKey(AppsKey);
            if (apps is null)
            {
                return found;
            }

            using var labels = Registry.CurrentUser.OpenSubKey(LabelsKey);

            foreach (var eventName in apps.GetSubKeyNames())
            {
                if (ReadPath(apps, eventName) is not { } path)
                {
                    continue;
                }

                found.Add(new SchemeSound(eventName, ReadLabel(labels, eventName), path));
            }
        }
        catch (Exception)
        {
            // A locked-down or damaged hive. An empty list is handled: the picker falls back to
            // offering silence, and a cue that cannot be resolved simply does not play.
            return found;
        }

        // Suggested sounds first, then everything else by label the way a person reads it. Plain
        // alphabetical opened the picker on "Alarm 1, Alarm 10, Alarm 2" with the useful chimes
        // somewhere past the middle.
        return found
            .OrderBy(sound => SoundCue.Rank(sound.EventName))
            .ThenBy(sound => sound.Label, Comparer<string>.Create(SoundCue.CompareLabels))
            .ToList();
    }

    /// <summary>The .wav for one event name, or null when it has none, is silent, or has gone missing.</summary>
    public static string? PathFor(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        try
        {
            using var apps = Registry.CurrentUser.OpenSubKey(AppsKey);
            return apps is null ? null : ReadPath(apps, eventName.Trim());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The path under <c>&lt;event&gt;\.Current</c>, expanded and checked.
    ///
    /// The value is usually a REG_EXPAND_SZ holding something like
    /// <c>%SystemRoot%\media\Windows Notify.wav</c>, so it is expanded here. A scheme can also point
    /// at a file that has since been deleted, which is why this checks rather than trusting it: a
    /// missing file would otherwise be discovered inside the player, on the first dictation.
    /// </summary>
    private static string? ReadPath(RegistryKey apps, string eventName)
    {
        using var current = apps.OpenSubKey(eventName + @"\.Current");
        if (current?.GetValue(string.Empty) is not string raw || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(raw.Trim());
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The friendly name, falling back to the raw key. A scheme can carry an event this version of
    /// Windows has no label for, and "AppGPFault" in the list is better than dropping a sound that
    /// works.
    /// </summary>
    private static string ReadLabel(RegistryKey? labels, string eventName)
    {
        using var label = labels?.OpenSubKey(eventName);
        return label?.GetValue(string.Empty) as string is { Length: > 0 } name ? name : eventName;
    }
}
