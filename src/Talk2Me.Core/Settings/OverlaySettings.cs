namespace Talk2Me.Core.Settings;

public enum OverlayPosition
{
    BottomCenter,
    BottomRight,
    BottomLeft,
    TopCenter,
    TopRight,
    TopLeft,
}

/// <summary>The floating status pill. It is click-through and never takes focus in any of these modes.</summary>
public sealed class OverlaySettings
{
    /// <summary>
    /// Keep the pill on screen while idle instead of hiding it. It rests dimmed showing the hotkey, and
    /// comes back to full strength — and back to the front — the moment dictation starts.
    /// </summary>
    public bool AlwaysVisible { get; set; } = true;

    /// <summary>How faint the pill goes while resting. 1.0 is the same as when it is active.</summary>
    public double RestingOpacity { get; set; } = 0.45;

    public OverlayPosition Position { get; set; } = OverlayPosition.BottomCenter;

    /// <summary>Gap in pixels between the pill and the edges of the work area.</summary>
    public double Margin { get; set; } = 36;

    /// <summary>
    /// Where the user dragged the bar to. Null means "use <see cref="Position"/>", which is also what
    /// happens when the saved point is no longer on any connected screen.
    /// </summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public OverlaySettings Clone() => (OverlaySettings)MemberwiseClone();
}
