using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Talk2Me.Core.Settings;
using Talk2Me.Desktop.ViewModels;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// The always-available dictation log. Closing it hides it rather than tearing it down, so reopening
/// from the tray is instant and the view model keeps following new dictations either way.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;

    public HistoryWindow(HistoryViewModel viewModel, HistorySettings placement)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        Width = placement.WindowWidth;
        Height = placement.WindowHeight;

        if (placement.WindowLeft is { } left && placement.WindowTop is { } top && IsOnAScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        SetBinding(TopmostProperty, new Binding(nameof(HistoryViewModel.AlwaysOnTop)));
    }

    /// <summary>Set on shutdown so the last close actually closes instead of hiding.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();

        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void SavePlacement()
    {
        // Left/Top are meaningless while maximised or minimised; RestoreBounds is the normal rectangle.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _viewModel.SavePlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
    }

    /// <summary>Guards against restoring onto a monitor that has since been unplugged.</summary>
    private static bool IsOnAScreen(double left, double top)
        => left > SystemParameters.VirtualScreenLeft - 100
           && top > SystemParameters.VirtualScreenTop - 100
           && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
           && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
}
