namespace TawkType.Core.Text;

/// <summary>
/// Which applications are windows onto another computer, where text has to arrive as key presses.
///
/// A VNC viewer forwards physical keys, not characters. TigerVNC's keyboard grab reads the scan code
/// of every event, so a KEYEVENTF_UNICODE character arrives as whatever key sits at the low byte of
/// its code: a space (0x20) becomes D, a full stop (0x2E) becomes C, and lowercase letters, which
/// land past the end of the keyboard, vanish. Typing into one has to press real keys, and pasting
/// has to use the remote machine's paste shortcut rather than Ctrl+V.
/// </summary>
public static class RemoteViewers
{
    /// <summary>
    /// TigerVNC's viewer. A prefix, because its standalone download keeps the version in the file
    /// name — vncviewer64-1.16.2.exe runs as the process "vncviewer64-1.16.2".
    /// </summary>
    public const string Default = "vncviewer";

    /// <summary>What a remote viewer pastes with unless the user says otherwise.</summary>
    public const string DefaultPasteKey = "Left Win + V";

    /// <summary>
    /// True when <paramref name="processName"/> starts with any of the comma-separated names in
    /// <paramref name="list"/>, ignoring case. A prefix rather than an exact name for the version
    /// suffix above; an empty entry matches nothing, so a stray comma cannot match every app.
    /// </summary>
    public static bool Matches(string? processName, string? list)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(list))
        {
            return false;
        }

        return list
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => processName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }
}
