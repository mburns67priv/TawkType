namespace Talk2Me.Core.Abstractions;

/// <summary>Where dictated text goes when it cannot be typed into the focused window.</summary>
public interface IClipboard
{
    /// <summary>Puts the text on the clipboard. Throws if the clipboard is held open by another process.</summary>
    void SetText(string text);
}
