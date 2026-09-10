using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;

namespace Talk2Me.Windows.Input;

/// <summary>
/// Works out whether the focused element can accept typed text, using UI Automation — the same
/// information screen readers use, which is why it works across Win32, WinForms, WPF, UWP, Chromium and
/// Office rather than only classic edit controls.
///
/// Two rules govern everything here. It must never throw, and it must never hang: it runs while the user
/// is speaking, and a wedged accessibility tree in some other application must not take the dictation
/// down with it. Anything unclear returns <see cref="FocusVerdict.Unknown"/>, which types as before.
/// </summary>
public sealed class UiaFocusProbe : IFocusProbe
{
    /// <summary>Generous, because it runs during speech — but bounded, because some apps never answer.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorAccessDenied = 5;

    private readonly ILogger<UiaFocusProbe> _logger;

    public UiaFocusProbe(ILogger<UiaFocusProbe> logger)
    {
        _logger = logger;
    }

    public FocusTarget Probe(CancellationToken cancellationToken = default)
    {
        // The elevation check is cheap, local, and explains a failure that has no other symptom:
        // synthetic input to a higher-privilege window is discarded by Windows without an error.
        var foreground = ForegroundProcess();
        var process = foreground?.Name;

        if (foreground is { Elevated: true })
        {
            return new FocusTarget(FocusVerdict.Elevated, process, "elevated window");
        }

        // UI Automation calls cross into the target process and can block indefinitely if it is busy,
        // so the whole thing runs on a pool thread we are prepared to abandon.
        var work = Task.Run(() => Inspect(process), cancellationToken);

        try
        {
            return work.Wait(Budget) ? work.Result : Abandon(process);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UI Automation focus probe failed");
            return FocusTarget.Unknown;
        }
    }

    private FocusTarget Abandon(string? process)
    {
        _logger.LogDebug("UI Automation did not answer within {Seconds}s; assuming typeable", Budget.TotalSeconds);
        return new FocusTarget(FocusVerdict.Unknown, process, "probe timed out");
    }

    private FocusTarget Inspect(string? process)
    {
        AutomationElement? focused;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
            return new FocusTarget(FocusVerdict.Unknown, process, "focus moved");
        }

        if (focused is null)
        {
            // Nothing has keyboard focus at all — the desktop, or a window that takes no input.
            return new FocusTarget(FocusVerdict.NotEditable, process, "nothing focused");
        }

        var control = SafeControlType(focused);
        var description = $"{control?.ProgrammaticName ?? "unknown"} in {process ?? "?"}";

        // A read-only value is a real negative: a disabled box, or a document that cannot be edited.
        if (TryGetPattern<ValuePattern>(focused, ValuePattern.Pattern) is { } value)
        {
            return value.Current.IsReadOnly
                ? new FocusTarget(FocusVerdict.NotEditable, process, description + " (read-only)")
                : new FocusTarget(FocusVerdict.Editable, process, description);
        }

        // TextPattern without ValuePattern is the usual shape for rich editors — Word, Chromium
        // contenteditable, code editors.
        if (TryGetPattern<TextPattern>(focused, TextPattern.Pattern) is not null)
        {
            return new FocusTarget(FocusVerdict.Editable, process, description);
        }

        if (control is not null && IsDefinitelyNotText(control))
        {
            return new FocusTarget(FocusVerdict.NotEditable, process, description);
        }

        // Focused, but exposes nothing that settles it. Plenty of real text fields land here, so this
        // has to type.
        return new FocusTarget(FocusVerdict.Unknown, process, description);
    }

    /// <summary>
    /// Controls that cannot hold text under any implementation. Kept short on purpose — every entry is
    /// a chance to refuse a window that would in fact have accepted the text.
    /// </summary>
    private static bool IsDefinitelyNotText(ControlType control)
        => control == ControlType.Button
           || control == ControlType.CheckBox
           || control == ControlType.RadioButton
           || control == ControlType.MenuItem
           || control == ControlType.Slider
           || control == ControlType.TabItem
           || control == ControlType.TreeItem
           || control == ControlType.Hyperlink
           || control == ControlType.ScrollBar;

    private static ControlType? SafeControlType(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static T? TryGetPattern<T>(AutomationElement element, AutomationPattern pattern)
        where T : BasePattern
    {
        try
        {
            return element.TryGetCurrentPattern(pattern, out var found) ? (T)found : null;
        }
        catch (Exception)
        {
            // InvalidOperation, ElementNotAvailable, COM failures — all mean "cannot tell".
            return null;
        }
    }

    /// <summary>The foreground window's process, and whether we are locked out of it by privilege.</summary>
    private static (string Name, bool Elevated)? ForegroundProcess()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            // Access denied here means the target runs at a higher integrity level than we do, which is
            // exactly the case where SendInput would be dropped silently.
            var elevated = Marshal.GetLastWin32Error() == ErrorAccessDenied;
            return (NameOf(processId), elevated);
        }

        CloseHandle(handle);
        return (NameOf(processId), false);
    }

    private static string NameOf(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return "?";
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
