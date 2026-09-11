using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Views;

/// <summary>
/// Gives TawkType a taskbar button for as long as it is running.
///
/// The status bar cannot provide one itself: it is a WS_EX_TOOLWINDOW that hides whenever it is
/// minimised or set not to rest on screen, which would take the button away at exactly the moment the
/// user needs something to click. So this window exists purely to hold the button. It is one pixel,
/// transparent, parked off-screen and permanently minimised, so it is never seen — only its taskbar
/// button is.
///
/// Clicking that button restores it, which is the signal we want: bring the status bar back, then drop
/// straight back to minimised so the button stays put.
/// </summary>
public partial class TaskbarWindow : Window
{
    /// <summary>What the icon call did, for the log. This has been fragile enough to be worth tracing.</summary>
    public string IconDiagnostics { get; private set; } = "not attempted";

    public TaskbarWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Velopack gives the process an AppUserModelID, and from then on Windows resolves this
        // button's icon through identity rather than Window.Icon. See TaskbarIdentity for what brings
        // the real icon back — including why the icon it reads cannot live with the installed app.
        if (Environment.GetEnvironmentVariable("TAWKTYPE_TEST_SKIPPROP") is not null)
        {
            IconDiagnostics = "skipped by probe";
            return;
        }

        var icon = StageIcon();
        if (icon is null)
        {
            IconDiagnostics = "could not stage the icon";
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
        IconDiagnostics = TaskbarIdentity.SetTaskbarIcon(handle, icon);
    }

    /// <summary>
    /// Answers a click on the taskbar button without ever leaving the minimised state.
    ///
    /// Letting the window restore and then minimising it again works, but Windows plays both
    /// animations, and it draws them at full size however small the window really is — so a click
    /// produced a rectangle that flew up the screen and dropped back to the taskbar, which looks like
    /// the app appearing and then being swallowed. Refusing the restore outright leaves nothing to
    /// animate; the click still means "bring the bar back", so that is all it does.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int WmSysCommand = 0x0112;
        const int ScRestore = 0xF120;

        // The low four bits of a system command are reserved for the system's own use.
        if (message == WmSysCommand && ((int)wParam & 0xFFF0) == ScRestore)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }

    /// <summary>
    /// Drops a copy of the application icon in the temp folder and returns its path, or null if that
    /// fails. The shell will not read an icon out of %LOCALAPPDATA%, where an installed TawkType and all
    /// its data live, so the copy is not redundant — it is the only one the taskbar can see. It is
    /// rewritten at every start, which is also what makes it survive a temp folder being cleared.
    /// </summary>
    private static string? StageIcon()
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "TawkType");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "taskbar.ico");

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tawktype.ico"));
            if (resource is null)
            {
                return null;
            }

            using var source = resource.Stream;
            using var target = File.Create(path);
            source.CopyTo(target);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The taskbar button was clicked (or the window alt-tabbed to).</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Closing the taskbar button means closing the app.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Set during shutdown so the window actually closes instead of asking to quit again.</summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// A safety net for the ways back in that do not send SC_RESTORE — alt-tab, for one. The window
    /// message above handles the taskbar button itself and this never runs for it.
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState != WindowState.Minimized)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
            WindowState = WindowState.Minimized;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // Close from the taskbar button's system menu. Quitting is what that means for a taskbar
            // button, but route it through the app so shutdown runs properly rather than just
            // destroying this window and leaving TawkType running with no button.
            e.Cancel = true;
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
