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
    public static readonly ClipboardSnapshot Unreadable =
        new(Array.Empty<Entry>(), Array.Empty<uint>(), Array.Empty<uint>());

    public ClipboardSnapshot(IReadOnlyList<Entry> entries, IReadOnlyList<uint> lost, IReadOnlyList<uint> withheld)
    {
        Entries = entries;
        Lost = lost;
        Withheld = withheld;
    }

    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>
    /// Formats that were on the clipboard, were not copied, and will not come back on their own.
    ///
    /// Not every format that is skipped belongs here. Windows synthesises CF_BITMAP and CF_PALETTE from
    /// the CF_DIB we do keep, so an image is whole again after a restore even though two of its formats
    /// were never copied — counting those as losses would put a warning in the log every time somebody
    /// pasted a dictation with a screenshot on their clipboard, which is how a warning stops being read.
    /// </summary>
    public IReadOnlyList<uint> Lost { get; }

    /// <summary>
    /// Formats that were copied and are deliberately not being put back, because something they depend
    /// on could not be. Kept apart from <see cref="Lost"/> so a report can say which of the two it is:
    /// "we could not read this" and "we chose not to hand this back" are different facts about the
    /// clipboard, and only the second is TawkType's decision.
    /// </summary>
    public IReadOnlyList<uint> Withheld { get; }

    /// <summary>Whether everything on the clipboard will survive the round trip.</summary>
    public bool IsComplete => Lost.Count == 0 && Withheld.Count == 0;

    /// <summary>Whether there is anything to put back.</summary>
    public bool HasContent => Entries.Count > 0;

    public int TotalBytes => Entries.Sum(entry => entry.Bytes.Length);

    public sealed record Entry(uint Format, byte[] Bytes);
}
