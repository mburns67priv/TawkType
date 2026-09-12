namespace TawkType.Windows.Injection;

/// <summary>
/// A copy of everything on the clipboard, taken so a dictation can borrow it and give it back.
///
/// The old code kept the text and nothing else, so a dictation that pasted destroyed an image, a
/// copied file, or the formatting on copied text — invisibly, and more often since multiline results
/// started always pasting. This keeps every format whose data is a memory handle, which is all of them
/// in practice: images travel as CF_DIB, files as CF_HDROP, styled text as HTML Format and RTF.
/// </summary>
internal sealed class ClipboardSnapshot
{
    /// <summary>A clipboard that could not be read at all. Restoring it does nothing.</summary>
    public static readonly ClipboardSnapshot Unreadable = new(Array.Empty<Entry>(), complete: false);

    public ClipboardSnapshot(IReadOnlyList<Entry> entries, bool complete)
    {
        Entries = entries;
        IsComplete = complete;
    }

    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Whether everything on the clipboard was copied. False means a restore will not be faithful.</summary>
    public bool IsComplete { get; }

    /// <summary>Whether there is anything to put back.</summary>
    public bool HasContent => Entries.Count > 0;

    public int TotalBytes => Entries.Sum(entry => entry.Bytes.Length);

    public sealed record Entry(uint Format, byte[] Bytes);
}
