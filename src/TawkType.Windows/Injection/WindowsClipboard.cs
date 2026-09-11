using TawkType.Core.Abstractions;

namespace TawkType.Windows.Injection;

/// <summary>
/// Clipboard access over the same Win32 helper the paste injector uses, rather than WPF's Clipboard —
/// this has to work from the dictation pipeline's thread pool thread, not just the UI thread.
/// </summary>
public sealed class WindowsClipboard : IClipboard
{
    public void SetText(string text) => NativeClipboard.SetText(text);
}
