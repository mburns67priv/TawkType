using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;
using TawkType.Desktop.ViewModels;

namespace TawkType.Desktop.Views;

/// <summary>
/// The floating status bar. It takes mouse input — toolbar buttons, and dragging to move it — but it
/// must never take *focus*: the caret has to stay in whatever the user is dictating into.
///
/// WS_EX_NOACTIVATE is what makes both true at once. Windows still delivers clicks to the window, but
/// never activates it, so pressing a button here does not deactivate the user's editor. WS_EX_TRANSPARENT
/// is deliberately NOT set any more (it would send the clicks straight through), and the bar is raised
/// with SWP_NOACTIVATE rather than Activate() for the same reason.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly OverlayViewModel _viewModel;
    private readonly ISettingsProvider _settings;

    private bool _dragging;

    public OverlayWindow(OverlayViewModel viewModel, ISettingsProvider settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.AttentionRequested += OnAttentionRequested;
        settings.Changed += OnSettingsChanged;
        SizeChanged += OnSizeChanged;

        MouseLeftButtonDown += OnDragStart;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // The view model may already be resting on screen before the window exists to hear about it.
        if (viewModel.IsVisible)
        {
            ShowWithoutStealingFocus();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new nint(style | WsExToolWindow | WsExNoActivate));

        Reposition();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.AttentionRequested -= OnAttentionRequested;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosing(e);
    }

    /// <summary>Anywhere on the bar that is not a button drags it.</summary>
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            _dragging = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the button was already released; nothing to recover from.
        }
        finally
        {
            _dragging = false;
            if (RememberedPlacement.Capture(this) is { } spot)
            {
                _viewModel.SavePlacement(spot.Left, spot.Top);
            }
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Collapsing to compact changes the width; keep the bar where the user put it rather than
        // letting it drift, but do not fight a drag in progress.
        if (!_dragging)
        {
            Reposition();
        }
    }

    /// <summary>A monitor was unplugged, switched off, or rearranged. Rescue the bar if it is stranded.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (!RememberedPlacement.EnsureOnScreen(this))
            {
                MoveToDefaultPosition();
            }
        });

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Reposition);

    /// <summary>
    /// The tray menu or the taskbar button asked for the bar. Show and raise it unconditionally: it may
    /// already be visible but buried, or stranded on a monitor that has since gone away.
    /// </summary>
    private void OnAttentionRequested(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(ShowWithoutStealingFocus);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverlayViewModel.IsVisible):
                if (_viewModel.IsVisible)
                {
                    ShowWithoutStealingFocus();
                }
                else
                {
                    Hide();
                }

                break;

            // Resting -> active means a dictation just started: come back to the front, still without
            // taking focus, in case another topmost window has since been put above us.
            case nameof(OverlayViewModel.IsResting) when !_viewModel.IsResting && _viewModel.IsVisible:
                RaiseWithoutActivating();
                break;
        }
    }

    private void ShowWithoutStealingFocus()
    {
        Show(); // ShowActivated="False" in XAML, so this cannot pull focus off the user's window.
        Reposition();
        RaiseWithoutActivating();
    }

    /// <summary>
    /// Re-asserts topmost z-order without activating. Deliberately not SetForegroundWindow or Activate:
    /// either would move focus, which is the one thing this window must never do.
    /// </summary>
    private void RaiseWithoutActivating()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
    }

    /// <summary>
    /// Where the user dragged it wins, as long as a monitor still covers that spot; otherwise the
    /// configured corner on the primary screen, which always exists.
    /// </summary>
    private void Reposition()
    {
        var overlay = _settings.Current.Overlay;

        if (overlay.WindowLeft is { } savedLeft && overlay.WindowTop is { } savedTop
            && RememberedPlacement.TryRestore(this, savedLeft, savedTop))
        {
            return;
        }

        MoveToDefaultPosition();
    }

    private void MoveToDefaultPosition()
    {
        var area = SystemParameters.WorkArea;
        var overlay = _settings.Current.Overlay;
        var margin = overlay.Margin;
        var center = area.Left + ((area.Width - ActualWidth) / 2);
        var bottom = area.Bottom - ActualHeight - margin;
        var top = area.Top + margin;

        (Left, Top) = overlay.Position switch
        {
            OverlayPosition.BottomRight => (area.Right - ActualWidth - margin, bottom),
            OverlayPosition.BottomLeft => (area.Left + margin, bottom),
            OverlayPosition.TopCenter => (center, top),
            OverlayPosition.TopRight => (area.Right - ActualWidth - margin, top),
            OverlayPosition.TopLeft => (area.Left + margin, top),
            _ => (center, bottom),
        };
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
