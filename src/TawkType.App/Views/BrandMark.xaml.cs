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

    /// <summary>
    /// Draws the bubble as an outline instead of a filled shape. Off everywhere the mark is the brand
    /// — on a gradient chip, at a window's title — and on for the status bar, where a filled mark was
    /// a grey slab sitting among line icons and reading as a blob rather than as the mark.
    /// </summary>
    public static readonly DependencyProperty BubbleStrokeProperty = DependencyProperty.Register(
        nameof(BubbleStroke), typeof(Brush), typeof(BrandMark), new PropertyMetadata(null));

    /// <summary>In the mark's own 24-unit grid, so it scales with Width and Height like everything else.</summary>
    public static readonly DependencyProperty BubbleStrokeThicknessProperty = DependencyProperty.Register(
        nameof(BubbleStrokeThickness), typeof(double), typeof(BrandMark), new PropertyMetadata(1.5));

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

    public Brush? BubbleStroke
    {
        get => (Brush?)GetValue(BubbleStrokeProperty);
        set => SetValue(BubbleStrokeProperty, value);
    }

    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }
}
