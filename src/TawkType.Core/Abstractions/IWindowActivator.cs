namespace TawkType.Core.Abstractions;

/// <summary>
/// Brings a window the user was dictating into back to the front, so text can be sent there after the
/// fact. A seam rather than a direct call, because it is the one piece of the recovery box that needs
/// Win32 and Core has none of it.
/// </summary>
public interface IWindowActivator
{
    /// <summary>
    /// Focuses <paramref name="window"/> and waits until it is actually in front, or gives up.
    /// Returns false if the window is gone, or Windows refused to hand it the foreground — both are
    /// ordinary outcomes, not errors, and the caller falls back to the clipboard.
    /// </summary>
    Task<bool> ActivateAsync(long window, CancellationToken cancellationToken = default);
}
