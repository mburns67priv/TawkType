using System.Windows.Media;

namespace TawkType.Desktop.Views;

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

    public static Geometry Gear { get; } =
        Parse("M12,9 A3,3 0 1 1 11.99,9 Z M19.4,13 A7.5,7.5 0 0 0 19.4,11 L21.3,9.6 L19.3,6.1 L17.1,7 " +
              "A7.5,7.5 0 0 0 15.3,6 L15,3.7 H10.9 L10.6,6 A7.5,7.5 0 0 0 8.8,7 L6.6,6.1 L4.6,9.6 " +
              "L6.5,11 A7.5,7.5 0 0 0 6.5,13 L4.6,14.4 L6.6,17.9 L8.8,17 A7.5,7.5 0 0 0 10.6,18 " +
              "L10.9,20.3 H15 L15.3,18 A7.5,7.5 0 0 0 17.1,17 L19.3,17.9 L21.3,14.4 Z");

    public static Geometry Clipboard { get; } =
        Parse("M9,4.5 H15 V7 H9 Z M8,5.5 H6.5 V20 H17.5 V5.5 H16 M9.5,11 H14.5 M9.5,14.5 H14.5");

    /// <summary>Three sliders: a bundle of settings chosen together, which is what a mode is.</summary>
    public static Geometry Modes { get; } =
        Parse("M4,7 H10 M14,7 H20 M4,12 H14 M18,12 H20 M4,17 H8 M12,17 H20 "
            + "M12,7 A2,2 0 1 0 12.01,7 M16,12 A2,2 0 1 0 16.01,12 M10,17 A2,2 0 1 0 10.01,17");

    /// <summary>A tray with the text lifting out of it: the dictation that did not arrive, recovered.</summary>
    public static Geometry Rescue { get; } =
        Parse("M5,13.5 V19 H19 V13.5 M12,4 V14 M8.5,7.5 L12,4 L15.5,7.5");

    public static Geometry Minimize { get; } = Parse("M6,12 H18");

    public static Geometry Close { get; } = Parse("M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5");

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze(); // shared across every window; freezing makes that safe and cheap
        return geometry;
    }
}
