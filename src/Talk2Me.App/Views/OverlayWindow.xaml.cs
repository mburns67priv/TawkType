using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;
using Talk2Me.Desktop.ViewModels;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// The floating status pill. It never takes focus and never takes the mouse: WS_EX_NOACTIVATE keeps
/// Windows from activating it, WS_EX_TRANSPARENT sends clicks straight through to whatever is behind,
/// and it is raised with SWP_NOACTIVATE so coming to the front cannot move the caret out of the window
/// the user clicked into.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly OverlayViewModel _viewModel;
    private readonly ISettingsProvider _settings;

    public OverlayWindow(OverlayViewModel viewModel, ISettingsProvider settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        settings.Changed += OnSettingsChanged;
        SizeChanged += (_, _) => Reposition();

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
        SetWindowLongPtr(handle, GwlExStyle, new nint(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _settings.Changed -= OnSettingsChanged;
        base.OnClosing(e);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Reposition);

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

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        var overlay = _settings.Current.Overlay;
        var margin = overlay.Margin;

        // The pill's own Border carries a 24px margin for its drop shadow; that is inside ActualWidth.
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
