namespace TawkType.Core.Models;

/// <summary>How a finished dictation actually reached the user.</summary>
public enum DictationDelivery
{
    /// <summary>Typed into the focused window, the normal path.</summary>
    Typed,

    /// <summary>The focused window could not accept it, so it went to the clipboard instead.</summary>
    CopiedToClipboard,

    /// <summary>Recognised, not yet delivered. The state a result is in while it is being handed over.</summary>
    Pending,

    /// <summary>Delivery was attempted and failed. The words still exist; they just did not arrive.</summary>
    Failed,
}
