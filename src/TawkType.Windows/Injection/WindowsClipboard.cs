using TawkType.Core.Abstractions;

namespace TawkType.Windows.Injection;

/// <summary>
/// Clipboard access over the same Win32 helper the paste injector uses, rather than WPF's Clipboard —
/// this has to work from the dictation pipeline's thread pool thread, not just the UI thread.
/// </summary>
public sealed class WindowsClipboard : IClipboard
{
    /// <summary>
    /// Text the user is meant to paste themselves — a dictation that could not be delivered. It stays
    /// in Windows' clipboard history, because they may well need it more than once and taking it out
    /// of Win+V would be a surprise. It is still kept off the cloud clipboard: syncing a dictation to
    /// the user's other devices would be words leaving this machine without anyone choosing it.
    /// </summary>
    public void SetText(string text) => NativeClipboard.SetText(text, allowClipboardHistory: true);
}
