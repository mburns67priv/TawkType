using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;
using Talk2Me.Core.Text;

namespace Talk2Me.Windows.Injection;

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

/// <summary>Puts the text on the clipboard, sends Ctrl+V, then restores the previous clipboard text.</summary>
public sealed class ClipboardPasteInjector : ITextInjector
{
    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PasteSettleTime = TimeSpan.FromMilliseconds(200);

    private readonly ILogger<ClipboardPasteInjector> _logger;

    public ClipboardPasteInjector(ILogger<ClipboardPasteInjector> logger)
    {
        _logger = logger;
    }

    public async Task InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        await NativeInput.WaitForModifiersReleasedAsync(ModifierTimeout, cancellationToken).ConfigureAwait(false);

        // Only text is preserved; images or files on the clipboard are lost. Acceptable for now.
        var previous = NativeClipboard.TryGetText();
        NativeClipboard.SetText(text);

        NativeInput.Send(new[]
        {
            NativeInput.KeyDown(NativeInput.VkControl),
            NativeInput.KeyDown(NativeInput.VkV),
            NativeInput.KeyUp(NativeInput.VkV),
            NativeInput.KeyUp(NativeInput.VkControl),
        });

        await Task.Delay(PasteSettleTime, cancellationToken).ConfigureAwait(false);

        if (previous is not null)
        {
            try
            {
                NativeClipboard.SetText(previous);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not restore the previous clipboard text");
            }
        }
    }
}

/// <summary>Chooses an injector per <see cref="Talk2MeSettings.InjectionMode"/>.</summary>
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
