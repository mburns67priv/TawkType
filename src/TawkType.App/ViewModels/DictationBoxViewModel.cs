using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.History;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// The dictation box: an editing surface for a dictation that did not reach where it was going.
///
/// It exists because "copied to your clipboard" is only half a recovery. The user still has to find
/// the words, decide whether they are right, and get them somewhere — and if anything else touches the
/// clipboard in between, they are gone. Here the text is held, editable, until they are done with it.
///
/// It never opens itself. The bar says something went wrong and the user comes here if they want to;
/// a window that appeared over whatever they were typing into would be a worse interruption than the
/// failure it is reporting.
/// </summary>
public sealed partial class DictationBoxViewModel(
    LastDictation last,
    IClipboard clipboard,
    ITextInjector injector,
    IWindowActivator windows,
    ILogger<DictationBoxViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _headline = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    /// <summary>Transient feedback for the buttons, cleared by the next action.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendBackCommand))]
    private bool _canSendBack;

    /// <summary>Shown only when something actually went wrong; opening the box by hand shows no banner.</summary>
    [ObservableProperty]
    private bool _hasProblem;

    private long? _target;

    /// <summary>The window should close itself — the text has gone where the user wanted it.</summary>
    public event EventHandler? Finished;

    /// <summary>
    /// Fills the box from the last dictation. Called every time it is opened rather than once, because
    /// the interesting case is the dictation that just failed, not the one it was opened with.
    /// </summary>
    public void Load(DictationCompleted? completed)
    {
        Status = string.Empty;
        Text = completed?.CleanText ?? last.Value?.FinalText ?? string.Empty;

        var offer = completed is null ? RecoveryOffer.None : Recovery.For(completed);

        HasProblem = offer.Needed;
        Headline = offer.Headline;
        Explanation = offer.Explanation;
        _target = offer.Route == RecoveryRoute.SendBack ? completed?.Target?.Window : null;
        CanSendBack = _target is not null;

        if (Text.Length == 0)
        {
            Status = "Nothing dictated yet";
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (Text.Length == 0)
        {
            Status = "Nothing to copy";
            return;
        }

        try
        {
            clipboard.SetText(Text);
            Status = "Copied";
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open. Worth saying, never worth a dialog.
            logger.LogWarning(ex, "Could not put the dictation on the clipboard");
            Status = "Could not copy";
        }
    }

    /// <summary>
    /// Hands the foreground back to the window the dictation was aimed at and types it there. This is
    /// allowed only because the user pressed a button in a window that is already in front — Windows
    /// lets a process give the foreground away, never take it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendBack))]
    private async Task SendBackAsync()
    {
        if (_target is not { } window || Text.Length == 0)
        {
            return;
        }

        Status = "Sending…";

        try
        {
            if (!await windows.ActivateAsync(window).ConfigureAwait(true))
            {
                // The window has gone, or Windows would not switch to it. Leave the text here.
                Status = "That window is no longer there — copy it instead";
                CanSendBack = false;
                return;
            }

            await injector.InjectAsync(Text).ConfigureAwait(true);
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the dictation back to its window");
            Status = "Could not send it — copy it instead";
        }
    }
}
