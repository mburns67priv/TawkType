using TawkType.Core.Text;

namespace TawkType.Core.Tests;

public class ClipboardRestoreTests
{
    /// <summary>
    /// The ordinary ending. The user had something on the clipboard, TawkType borrowed it to paste a
    /// dictation, and nothing else touched it in between.
    /// </summary>
    [Fact]
    public void What_was_borrowed_is_given_back()
    {
        Assert.Equal(
            ClipboardAftermath.Restore,
            ClipboardRestore.Decide(ClipboardWrite.Written, hadPrevious: true, changedSinceWrite: false));
    }

    /// <summary>
    /// The race the old code lost: 200 ms is long enough to press Ctrl+C in another window, and the
    /// restore would have put the user's older content back over the copy they had just made.
    /// </summary>
    [Fact]
    public void A_copy_made_while_we_were_pasting_is_never_overwritten()
    {
        Assert.Equal(
            ClipboardAftermath.LeaveAlone,
            ClipboardRestore.Decide(ClipboardWrite.Written, hadPrevious: true, changedSinceWrite: true));

        Assert.Equal(
            ClipboardAftermath.LeaveAlone,
            ClipboardRestore.Decide(ClipboardWrite.Written, hadPrevious: false, changedSinceWrite: true));
    }

    /// <summary>
    /// An empty clipboard is a state worth restoring too. Left alone, every dictation that pasted would
    /// quietly leave its transcript behind for the next Ctrl+V — the words the user has just said, in
    /// whatever they paste into next.
    /// </summary>
    [Fact]
    public void A_clipboard_that_was_empty_is_left_empty()
    {
        Assert.Equal(
            ClipboardAftermath.Clear,
            ClipboardRestore.Decide(ClipboardWrite.Written, hadPrevious: false, changedSinceWrite: false));
    }

    /// <summary>
    /// Emptying the clipboard and then failing to write is the worst case, because the loss has already
    /// happened. It is also the one case where nothing can have claimed the clipboard in between: we
    /// held it open for both halves.
    /// </summary>
    [Fact]
    public void A_write_that_emptied_the_clipboard_and_then_failed_still_puts_it_back()
    {
        Assert.Equal(
            ClipboardAftermath.Restore,
            ClipboardRestore.Decide(ClipboardWrite.Emptied, hadPrevious: true, changedSinceWrite: false));

        Assert.Equal(
            ClipboardAftermath.Restore,
            ClipboardRestore.Decide(ClipboardWrite.Emptied, hadPrevious: true, changedSinceWrite: true));
    }

    [Fact]
    public void An_empty_clipboard_that_was_emptied_again_needs_nothing()
    {
        Assert.Equal(
            ClipboardAftermath.LeaveAlone,
            ClipboardRestore.Decide(ClipboardWrite.Emptied, hadPrevious: false, changedSinceWrite: false));
    }

    /// <summary>
    /// Another process can hold the clipboard open, and then TawkType never touched it. Putting the
    /// previous content back would be a write the user did not ask for: a duplicate entry in their
    /// clipboard history, and one more reason for a clipboard manager to fire.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void A_clipboard_that_was_never_opened_is_never_written_to(bool hadPrevious, bool changedSinceWrite)
    {
        Assert.Equal(
            ClipboardAftermath.LeaveAlone,
            ClipboardRestore.Decide(ClipboardWrite.NotOpened, hadPrevious, changedSinceWrite));
    }
}
