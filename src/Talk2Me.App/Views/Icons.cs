using System.Windows.Media;

namespace Talk2Me.Desktop.Views;

/// <summary>
/// Line icons for the settings nav and home cards, drawn on a 24-unit grid to match the brand mark.
/// Stroked rather than filled, so one geometry works on any background and takes the row's colour.
/// </summary>
public static class Icons
{
    public static Geometry Home { get; } = Parse("M3,10 L12,3 L21,10 V20 H14 V14 H10 V20 H3 Z");

    public static Geometry Microphone { get; } =
        Parse("M12,3 A3,3 0 0 1 15,6 V12 A3,3 0 0 1 9,12 V6 A3,3 0 0 1 12,3 Z M5,11 A7,7 0 0 0 19,11 M12,18 V21 M8.5,21 H15.5");

    public static Geometry Keyboard { get; } =
        Parse("M2.5,6.5 H21.5 V17.5 H2.5 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M8,14 H16");

    public static Geometry Palette { get; } =
        Parse("M12,3 A9,9 0 1 0 12,21 A2,2 0 0 0 12,17 A2,2 0 0 1 12,13 H16 A5,5 0 0 0 12,3 Z M7.5,9 H7.51 M11,7 H11.01 M15.5,9.5 H15.51");

    public static Geometry Sparkle { get; } =
        Parse("M12,3 L13.8,9.6 L20.5,12 L13.8,14.4 L12,21 L10.2,14.4 L3.5,12 L10.2,9.6 Z");

    public static Geometry Clock { get; } =
        Parse("M12,3 A9,9 0 1 1 11.99,3 Z M12,7.5 V12.4 L15.2,14.2");

    public static Geometry Folder { get; } =
        Parse("M3,6 H10 L12,8.5 H21 V19 H3 Z");

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze(); // shared across every window; freezing makes that safe and cheap
        return geometry;
    }
}
