namespace TawkType.Core.Settings;

/// <summary>
/// The dictation log. Everything you dictate is kept in plain text under your profile, so this is worth
/// switching off if you dictate anything you would not want written to disk.
/// </summary>
public sealed class HistorySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Older entries are dropped once the log passes this many.</summary>
    public int MaxEntries { get; set; } = 200;

    /// <summary>Keep the history window above other windows, so it is there when you come back to it.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    public bool OpenOnStart { get; set; }

    /// <summary>Last window placement, so a window that lives on screen comes back where you left it.</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 420;

    public double WindowHeight { get; set; } = 520;

    public HistorySettings Clone() => (HistorySettings)MemberwiseClone();
}
