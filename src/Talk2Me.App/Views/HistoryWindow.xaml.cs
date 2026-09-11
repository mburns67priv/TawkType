using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using Microsoft.Win32;
using Talk2Me.Core.Settings;
using Talk2Me.Desktop.Services;
using Talk2Me.Desktop.ViewModels;
using Talk2Me.Windows.Shell;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// The always-available dictation log. Closing it hides it rather than tearing it down, so reopening
/// from the tray is instant and the view model keeps following new dictations either way.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;
    private readonly HistorySettings _placement;

    public HistoryWindow(HistoryViewModel viewModel, HistorySettings placement)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _placement = placement;

        Width = placement.WindowWidth;
        Height = placement.WindowHeight;

        // The real positioning happens in OnSourceInitialized: it needs the window handle to work out
        // which monitor the saved point is on, and whether that monitor is still connected.
        WindowStartupLocation = placement.WindowLeft is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.Manual;

        SetBinding(TopmostProperty, new Binding(nameof(HistoryViewModel.AlwaysOnTop)));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Set on shutdown so the last close actually closes instead of hiding.</summary>
    public bool AllowClose { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

        if (_placement.WindowLeft is { } left && _placement.WindowTop is { } top
            && !RememberedPlacement.TryRestore(this, left, top))
        {
            CentreOnPrimary();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();

        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosing(e);
    }

    /// <summary>A monitor was unplugged, switched off, or rearranged. Rescue the window if stranded.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (!RememberedPlacement.EnsureOnScreen(this))
            {
                CentreOnPrimary();
            }
        });

    /// <summary>The primary monitor is the one place guaranteed to exist.</summary>
    private void CentreOnPrimary()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - Width) / 2);
        Top = area.Top + ((area.Height - Height) / 2);
    }

    private void SavePlacement()
    {
        // Size stays in WPF units; the position is captured in physical pixels so it can be checked
        // against the real monitor rectangles next time, without any DPI conversion.
        var size = WindowState == WindowState.Normal
            ? new Size(Width, Height)
            : new Size(RestoreBounds.Width, RestoreBounds.Height);

        if (size.Width > 0 && size.Height > 0 && RememberedPlacement.Capture(this) is { } spot)
        {
            _viewModel.SavePlacement(spot.Left, spot.Top, size.Width, size.Height);
        }
    }
}
