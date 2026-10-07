using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Input;
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
/// Types text by pressing the real key for each character, with Shift where the layout needs it.
///
/// For remote desktop viewers, which forward keys rather than characters (see <see cref="RemoteViewers"/>).
/// Every event carries its scan code, because that is what the viewer reads. The far machine turns
/// the keys back into text with its own layout, so this is only right when the two layouts agree.
/// Characters with no key never get here: <see cref="Delivery.Route"/> sends that text to the
/// clipboard instead.
/// </summary>
internal sealed class KeyPressTypingInjector
{
    private const ushort VkLeftShift = 0xA0;
    private const int KeysPerBatch = 16;
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(750);

    public async Task InjectAsync(string text, nint layout, CancellationToken cancellationToken = default)
    {
        await NativeInput.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false);

        text = Delivery.SingleLine(text);
        var (shiftScan, _) = NativeInput.ScanCodeFor(VkLeftShift, layout);

        // Caps Lock reverses Shift for letters on the far side too: the viewer passes the lock key
        // through, so the remote machine's state follows this one. Without this, "Testing" arrives as
        // "tESTING". Read once — the user is not pressing Caps Lock while the text is being typed.
        var capsLock = NativeInput.IsCapsLockOn();

        var batch = new List<NativeInput.Input>(KeysPerBatch * 4);
        foreach (var c in text)
        {
            if (NativeInput.KeyFor(c, layout) is not { } key)
            {
                continue; // Route has already checked; a layout switch mid-dictation is all that lands here
            }

            var (scan, extended) = NativeInput.ScanCodeFor(key.Vk, layout);
            var shift = key.Shift ^ (capsLock && char.IsLetter(c));
            if (shift)
            {
                batch.Add(NativeInput.KeyDown(VkLeftShift, shiftScan, extended: false));
            }

            batch.Add(NativeInput.KeyDown(key.Vk, scan, extended));
            batch.Add(NativeInput.KeyUp(key.Vk, scan, extended));

            if (shift)
            {
                batch.Add(NativeInput.KeyUp(VkLeftShift, shiftScan, extended: false));
            }

            // A viewer sends each key over the network as it arrives; small batches with a pause keep
            // a slow link from receiving a burst it reorders or drops.
            if (batch.Count >= KeysPerBatch * 4)
            {
                NativeInput.Send(batch);
                batch.Clear();
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
        }

        NativeInput.Send(batch);
    }
}

/// <summary>
/// Puts the text on the clipboard and presses the remote machine's paste shortcut through the viewer.
///
/// Unlike <see cref="ClipboardPasteInjector"/> it does not hand the clipboard back, and that is
/// deliberate. A VNC viewer only tells the server the clipboard has changed; the server fetches the
/// text when something on the far side actually pastes, which is a network round trip after the
/// shortcut. Restoring the old clipboard before then pastes the old clipboard. So the dictation stays
/// on the clipboard, exactly as if the user had copied it and pasted it themselves.
/// </summary>
internal sealed class RemotePasteInjector
{
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Time for the viewer to notice the new clipboard and announce it before the shortcut follows it
    /// down the same connection. Unmeasured; long enough to be past one message-loop turn.
    /// </summary>
    private static readonly TimeSpan AnnounceTime = TimeSpan.FromMilliseconds(100);

    public async Task InjectAsync(string text, Hotkey pasteKey, nint layout, CancellationToken cancellationToken = default)
    {
        await NativeInput.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false);

        if (NativeClipboard.TrySetText(text, allowClipboardHistory: false) != ClipboardWrite.Written)
        {
            throw new InvalidOperationException("Could not put the dictation on the clipboard to paste it");
        }

        await Task.Delay(AnnounceTime, cancellationToken).ConfigureAwait(false);

        var keys = pasteKey.Modifiers.Append(pasteKey.Key).Where(vk => vk != 0).Select(vk => (ushort)vk).ToArray();
        var chord = new List<NativeInput.Input>(keys.Length * 2);
        foreach (var vk in keys)
        {
            var (scan, extended) = NativeInput.ScanCodeFor(vk, layout);
            chord.Add(NativeInput.KeyDown(vk, scan, extended));
        }

        // A loop, not keys.Reverse(): on a newer compiler an array's Reverse() binds to the in-place
        // Span overload, which returns nothing. The release build broke on exactly that (gotcha 66).
        for (var i = keys.Length - 1; i >= 0; i--)
        {
            var vk = keys[i];
            var (scan, extended) = NativeInput.ScanCodeFor(vk, layout);
            chord.Add(NativeInput.KeyUp(vk, scan, extended));
        }

        NativeInput.Send(chord);
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
                "Clipboard formats {Lost} cannot be copied aside, and {Withheld} will not be handed back without them; this paste will not preserve them ({Formats} formats, {Bytes} bytes kept)",
                string.Join(", ", borrowed.Lost),
                string.Join(", ", borrowed.Withheld),
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
    private readonly ILogger<AutoTextInjector> _logger;
    private readonly KeyPressTypingInjector _keys = new();
    private readonly RemotePasteInjector _remotePaste = new();

    public AutoTextInjector(
        ISettingsProvider settings,
        UnicodeTypingInjector typing,
        ClipboardPasteInjector paste,
        ILogger<AutoTextInjector> logger)
    {
        _settings = settings;
        _typing = typing;
        _paste = paste;
        _logger = logger;
    }

    public Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var target = ForegroundApp.Current();

        if (RemoteViewers.Matches(target.ProcessName, settings.RemoteViewerApps))
        {
            return InjectRemoteAsync(text, settings, target, cancellationToken);
        }

        var route = Delivery.Route(text, settings.InjectionMode);

        // Which way the text went, and why. The engine's line says a dictation was "Typed", meaning it
        // reached the focused window — by keystrokes or by Ctrl+V, which are very different things when
        // something goes wrong. Nothing else records which, and since the clipboard is now put back
        // faithfully, a paste leaves no trace of itself anywhere else either.
        _logger.LogDebug("Delivering {Chars} chars — {Route}", text.Length, Delivery.Describe(route));

        return route == DeliveryRoute.Typed
            ? _typing.InjectAsync(text, cancellationToken)
            : _paste.InjectAsync(text, cancellationToken);
    }

    /// <summary>
    /// The same decision for a window onto another computer, made with real keys: typed text is
    /// pressed key by key, and anything that cannot be — a line break, a character with no key, or too
    /// much of it — goes through the clipboard and the remote machine's own paste shortcut.
    /// </summary>
    private Task InjectRemoteAsync(string text, TawkTypeSettings settings, ForegroundApp target, CancellationToken cancellationToken)
    {
        var layout = target.KeyboardLayout;
        var route = Delivery.Route(text, settings.InjectionMode, c => NativeInput.KeyFor(c, layout) is not null);

        if (!Hotkey.TryParse(settings.RemotePasteKey, out var pasteKey))
        {
            _logger.LogWarning("Remote paste key {Key} is not a combination TawkType can press; using {Default}",
                settings.RemotePasteKey, RemoteViewers.DefaultPasteKey);
            pasteKey = Hotkey.ParseOrDefault(RemoteViewers.DefaultPasteKey);
        }

        _logger.LogDebug("Delivering {Chars} chars to remote viewer {App} — {Route}",
            text.Length, target.ProcessName, route == DeliveryRoute.Typed ? "typed as key presses" : $"{Delivery.Describe(route)}, with {pasteKey}");

        return route == DeliveryRoute.Typed
            ? _keys.InjectAsync(text, layout, cancellationToken)
            : _remotePaste.InjectAsync(text, pasteKey, layout, cancellationToken);
    }
}
