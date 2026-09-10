namespace Talk2Me.Core.Models;

/// <summary>How a finished dictation actually reached the user.</summary>
public enum DictationDelivery
{
    /// <summary>Typed into the focused window, the normal path.</summary>
    Typed,

    /// <summary>The focused window could not accept it, so it went to the clipboard instead.</summary>
    CopiedToClipboard,
}
