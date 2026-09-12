namespace TawkType.Core.Text;

/// <summary>How far a write to the clipboard got. Emptying it and failing is not the same as not touching it.</summary>
public enum ClipboardWrite
{
    /// <summary>The clipboard could not be opened, so nothing about it changed.</summary>
    NotOpened,

    /// <summary>It was emptied — the user's content is already gone — but the new content did not go on.</summary>
    Emptied,

    /// <summary>The new content is on the clipboard.</summary>
    Written,
}

/// <summary>What to do with the clipboard once a paste has been attempted.</summary>
public enum ClipboardAftermath
{
    /// <summary>Put back what was there before.</summary>
    Restore,

    /// <summary>Take the dictation off and leave the clipboard empty, which is how it was found.</summary>
    Clear,

    /// <summary>Touch nothing. Whatever is on the clipboard belongs to somebody else.</summary>
    LeaveAlone,
}

/// <summary>
/// The rule for handing the clipboard back after borrowing it to paste a dictation. Pure, because the
/// three ways of getting this wrong all destroy something the user cannot get back: overwriting a copy
/// they made while we were pasting, leaving the transcript sitting on an empty clipboard, or losing
/// their content to a write that emptied it and then failed.
/// </summary>
public static class ClipboardRestore
{
    /// <param name="write">How far our own write got.</param>
    /// <param name="hadPrevious">Whether there was anything to put back.</param>
    /// <param name="changedSinceWrite">
    /// Whether the clipboard has changed since we wrote to it — the sequence number on Windows. True
    /// means somebody else owns what is on there now, and it is not ours to replace.
    /// </param>
    public static ClipboardAftermath Decide(ClipboardWrite write, bool hadPrevious, bool changedSinceWrite) => write switch
    {
        // Nothing we did took effect, so there is nothing to undo. Re-setting the previous content
        // here would be a no-op that still bumps the sequence number and leaves a duplicate in the
        // user's clipboard history.
        ClipboardWrite.NotOpened => ClipboardAftermath.LeaveAlone,

        // The clipboard was emptied and then the write failed, so the user's content is already gone
        // whatever happens next. Nothing else has had a chance to claim the clipboard in between.
        ClipboardWrite.Emptied => hadPrevious ? ClipboardAftermath.Restore : ClipboardAftermath.LeaveAlone,

        // Somebody copied something while we were pasting. Theirs is newer than ours and they are
        // watching for it; putting the old content back would take it away in front of them.
        _ when changedSinceWrite => ClipboardAftermath.LeaveAlone,

        // The ordinary ending: hand back exactly what was borrowed. With nothing to hand back, the
        // clipboard was empty when we found it, so leaving the transcript on it is a change too.
        _ => hadPrevious ? ClipboardAftermath.Restore : ClipboardAftermath.Clear,
    };
}
