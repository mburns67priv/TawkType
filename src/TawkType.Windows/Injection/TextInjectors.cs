using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Windows.Injection;

/// <summary>
/// Types text character by character with KEYEVENTF_UNICODE. Reliable everywhere, but slow for long
/// text, and single-line only: see <see cref="Delivery"/> for why it will not press Return.
/// </summary>
public sealed class UnicodeTypingInjector : ITextInjector
{
    private const int CharsPerBatch = 32;
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(750);

    public async Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        await NativeInput.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false);

        // Never a Return keypress. Multiline results are routed to the clipboard before they get here;
        // this fold is the backstop for anything that reaches it anyway.
        text = Delivery.SingleLine(text);

        var batch = new List<NativeInput.Input>(CharsPerBatch * 2);
        foreach (var c in text)
        {
            batch.Add(NativeInput.UnicodeDown(c));
            batch.Add(NativeInput.UnicodeUp(c));

            if (batch.Count >= CharsPerBatch * 2)
            {
                NativeInput.Send(batch);
                batch.Clear();
                await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            }
        }

        NativeInput.Send(batch);
    }
}

/// <summary>
/// Borrows the clipboard to paste the text, and gives it back.
///
/// "Borrows" is the whole design. Every format is copied aside first, not just the text, so an image
/// or a copied file survives a dictation; the clipboard is only put back if it still holds what we
/// put there, so a copy the user makes while we are pasting is never overwritten; and it is put back
/// in a finally, so a paste that throws does not strand the transcript on their clipboard.
/// </summary>
public sealed class ClipboardPasteInjector : ITextInjector
{
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// How long the target is given to consume the clipboard before it is handed back.
    ///
    /// Nothing here establishes that it has: Windows offers no signal for "somebody pasted", and
    /// restoring too early means the target pastes the user's *old* clipboard instead. The number is
    /// unmeasured and deliberately unchanged — the way out of the race is delayed rendering, where
    /// WM_RENDERFORMAT says exactly when the target asked for the data, not a bigger guess.
    /// </summary>
    private static readonly TimeSpan PasteSettleTime = TimeSpan.FromMilliseconds(200);

    private readonly ILogger<ClipboardPasteInjector> _logger;

    public ClipboardPasteInjector(ILogger<ClipboardPasteInjector> logger)
    {
        _logger = logger;
    }

    public async Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        await NativeInput.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false);

        var borrowed = NativeClipboard.Capture();
        if (!borrowed.IsComplete)
        {
            // The user is about to lose something. It is in the log rather than on screen because the
            // alternative is a dialog over whatever they are dictating into — and it fires only on a
            // real loss, not on the formats Windows regenerates by itself, or it would fire on every
            // screenshot and stop being worth reading.
            _logger.LogWarning(
                "Clipboard formats {Lost} cannot be copied aside and will not survive this paste ({Formats} formats, {Bytes} bytes kept)",
                string.Join(", ", borrowed.Lost),
                borrowed.Entries.Count,
                borrowed.TotalBytes);
        }

        var write = ClipboardWrite.NotOpened;
        var sequenceAfterWrite = 0u;

        try
        {
            write = NativeClipboard.TrySetText(text, allowClipboardHistory: false);
            if (write != ClipboardWrite.Written)
            {
                throw new InvalidOperationException("Could not put the dictation on the clipboard to paste it");
            }

            sequenceAfterWrite = NativeClipboard.SequenceNumber();

            NativeInput.Send(new[]
            {
                NativeInput.KeyDown(NativeInput.VkControl),
                NativeInput.KeyDown(NativeInput.VkV),
                NativeInput.KeyUp(NativeInput.VkV),
                NativeInput.KeyUp(NativeInput.VkControl),
            });

            await Task.Delay(PasteSettleTime, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            GiveBack(borrowed, write, sequenceAfterWrite);
        }
    }

    private void GiveBack(ClipboardSnapshot borrowed, ClipboardWrite write, uint sequenceAfterWrite)
    {
        try
        {
            var changed = write == ClipboardWrite.Written && NativeClipboard.SequenceNumber() != sequenceAfterWrite;

            switch (ClipboardRestore.Decide(write, borrowed.HasContent, changed))
            {
                case ClipboardAftermath.Restore when !NativeClipboard.Restore(borrowed):
                    _logger.LogWarning("Could not put the previous clipboard content back");
                    break;

                case ClipboardAftermath.Clear when !NativeClipboard.Clear():
                    _logger.LogWarning("Could not take the dictation back off the clipboard");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hand the clipboard back after pasting");
        }
    }
}

/// <summary>Chooses an injector per <see cref="TawkTypeSettings.InjectionMode"/>.</summary>
public sealed class AutoTextInjector : ITextInjector
{
    private readonly ISettingsProvider _settings;
    private readonly UnicodeTypingInjector _typing;
    private readonly ClipboardPasteInjector _paste;

    public AutoTextInjector(ISettingsProvider settings, UnicodeTypingInjector typing, ClipboardPasteInjector paste)
    {
        _settings = settings;
        _typing = typing;
        _paste = paste;
    }

    public Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        return Delivery.ShouldPaste(text, _settings.Current.InjectionMode)
            ? _paste.InjectAsync(text, cancellationToken)
            : _typing.InjectAsync(text, cancellationToken);
    }
}
