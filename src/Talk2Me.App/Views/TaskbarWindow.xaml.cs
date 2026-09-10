using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Talk2Me.Windows.Shell;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// Gives Talk2Me a taskbar button for as long as it is running.
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

        // Velopack sets a process-wide AppUserModelID, and from then on Windows resolves this button's
        // icon through that identity rather than Window.Icon — showing a generic one. See
        // TaskbarIdentity for why the icon and the window's own ID must be written in that order.
        var exe = Environment.ProcessPath;
        IconDiagnostics = exe is null
            ? "no process path"
            : TaskbarIdentity.SetTaskbarIcon(new WindowInteropHelper(this).Handle, exe);
    }

    /// <summary>The taskbar button was clicked (or the window alt-tabbed to).</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Closing the taskbar button means closing the app.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Set during shutdown so the window actually closes instead of asking to quit again.</summary>
    public bool AllowClose { get; set; }

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
            // destroying this window and leaving Talk2Me running with no button.
            e.Cancel = true;
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
