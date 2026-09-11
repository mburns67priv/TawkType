using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TawkType.Desktop.Views;

public partial class BrandMark : UserControl
{
    public static readonly DependencyProperty BubbleBrushProperty = DependencyProperty.Register(
        nameof(BubbleBrush), typeof(Brush), typeof(BrandMark), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty BarsBrushProperty = DependencyProperty.Register(
        nameof(BarsBrush), typeof(Brush), typeof(BrandMark), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x6D, 0x5D, 0xFF))));

    public BrandMark()
    {
        InitializeComponent();
    }

    public Brush BubbleBrush
    {
        get => (Brush)GetValue(BubbleBrushProperty);
        set => SetValue(BubbleBrushProperty, value);
    }

    public Brush BarsBrush
    {
        get => (Brush)GetValue(BarsBrushProperty);
        set => SetValue(BarsBrushProperty, value);
    }
}
