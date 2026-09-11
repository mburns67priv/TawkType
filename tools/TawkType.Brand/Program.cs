using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TawkType.Brand;

/// <summary>
/// Source of truth for the rendered brand assets. The geometry and palette here must match
/// src/TawkType.App/App.xaml (Brand.* resources) and branding/mark.svg.
///
/// usage: dotnet run --project tools/TawkType.Brand [repoRoot]
/// </summary>
internal static class Program
{
    private static readonly Color Violet = Parse("#7867FF");
    private static readonly Color AccentTint = Parse("#B4A9FF");
    private static readonly Color Coral = Parse("#FF8A9E");
    private static readonly Color Ink = Parse("#171925");
    private static readonly Color LightAccent = Parse("#5740CC");

    // 24-unit design grid. A speech bubble with a tail at the bottom-left, holding three voice bars
    // and an I-beam text cursor: speech going in, text coming out, and no microphone anywhere.
    private static readonly Geometry Bubble = Geometry.Parse(
        "M7.13,2.63 H16.88 Q21.38,2.63 21.38,7.13 V14.63 Q21.38,19.13 16.88,19.13 H10.13 L4.88,22.5 "
        + "V18.38 Q2.63,17.25 2.63,14.63 V7.13 Q2.63,2.63 7.13,2.63 Z");

    private static readonly Geometry Bars = new GeometryGroup
    {
        Children =
        {
            new RectangleGeometry(new Rect(6.75, 8.63, 1.5, 4.88), 0.75, 0.75),
            new RectangleGeometry(new Rect(9.38, 7.13, 1.5, 7.88), 0.75, 0.75),
            new RectangleGeometry(new Rect(12, 8.63, 1.5, 4.88), 0.75, 0.75),
            new RectangleGeometry(new Rect(15, 7.13, 4.5, 1.13), 0.56, 0.56),
            new RectangleGeometry(new Rect(16.69, 7.13, 1.13, 8.25), 0.56, 0.56),
            new RectangleGeometry(new Rect(15, 14.25, 4.5, 1.13), 0.56, 0.56),
        },
    };

    /// <summary>
    /// Below this, the three voice bars and the I-beam's serifs merge into a smudge. The brand package
    /// asks for separately adjusted small sizes rather than a shrunk 512; this is the simplest honest
    /// version of that — a bubble and a single cursor stem, which still reads as "speech into text".
    /// </summary>
    private const int SmallestFullMark = 24;

    private static readonly Geometry SmallBars = new GeometryGroup
    {
        Children =
        {
            new RectangleGeometry(new Rect(7.5, 8.25, 1.88, 7.5), 0.94, 0.94),
            new RectangleGeometry(new Rect(11.25, 6.75, 1.88, 10.5), 0.94, 0.94),
            new RectangleGeometry(new Rect(15, 8.25, 1.88, 7.5), 0.94, 0.94),
        },
    };

    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
        var exports = Path.Combine(root, "branding", "exports");
        var assets = Path.Combine(root, "src", "TawkType.App", "Assets");
        Directory.CreateDirectory(exports);
        Directory.CreateDirectory(assets);

        // App icon: every size the Windows shell asks for, PNG-compressed inside one .ico.
        var frames = IconSizes.Select(size => (Size: size, Png: EncodePng(RenderIcon(size)))).ToList();
        var icoPath = Path.Combine(assets, "tawktype.ico");
        WriteIco(icoPath, frames);
        Console.WriteLine($"wrote {icoPath} ({frames.Count} sizes)");

        foreach (var size in new[] { 256, 512, 1024 })
        {
            Save(Path.Combine(exports, $"icon-{size}.png"), RenderIcon(size));
        }

        Save(Path.Combine(exports, "mark-white-512.png"), RenderMark(512, Brushes.White, Brushes.White));
        Save(Path.Combine(exports, "mark-violet-512.png"), RenderMark(512, new SolidColorBrush(Violet), new SolidColorBrush(Violet)));
        Save(Path.Combine(exports, "logo-on-dark.png"), RenderLogo(Brushes.White));
        Save(Path.Combine(exports, "logo-on-light.png"), RenderLogo(new SolidColorBrush(Ink), new SolidColorBrush(LightAccent)));
        Save(Path.Combine(exports, "social-1200x630.png"), RenderSocialCard());

        Console.WriteLine($"exports in {exports}");
        return 0;
    }

    // ---- renderers -------------------------------------------------------------------------

    private static RenderTargetBitmap RenderIcon(int size)
    {
        return Render(size, size, dc =>
        {
            double s = size;
            var radius = s * 0.225;
            dc.DrawRoundedRectangle(VoiceGradient(), null, new Rect(0, 0, s, s), radius, radius);

            // Soft inner highlight at the top so large sizes do not look flat.
            if (size >= 64)
            {
                var sheen = new LinearGradientBrush(
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 1));
                dc.DrawRoundedRectangle(sheen, null, new Rect(0, 0, s, s * 0.55), radius, radius);
            }

            // Mark fills ~70% of the tile, nudged up a touch because the tail hangs below the bubble.
            var scale = s * 0.72 / 24;
            var offset = (s - (24 * scale)) / 2;
            dc.PushTransform(new TransformGroup
            {
                Children = { new ScaleTransform(scale, scale), new TranslateTransform(offset, offset - (s * 0.015)) },
            });

            if (size >= 48)
            {
                dc.PushTransform(new TranslateTransform(0, 0.5));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(56, 0, 0, 0)), null, Bubble);
                dc.Pop();
            }

            dc.DrawGeometry(Brushes.White, null, Bubble);

            // The full mark below 24px is a smudge: the three voice bars and the I-beam's serifs are
            // sub-pixel and merge. The small variant drops the cursor and widens the bars instead.
            dc.DrawGeometry(new SolidColorBrush(Violet), null, size >= SmallestFullMark ? Bars : SmallBars);
            dc.Pop();
        });
    }

    private static RenderTargetBitmap RenderMark(int size, Brush bubble, Brush bars)
    {
        return Render(size, size, dc =>
        {
            var scale = size / 24.0;
            dc.PushTransform(new ScaleTransform(scale, scale));

            // Single-colour glyph: the bars are cut out of the bubble so the background shows through.
            var glyph = new CombinedGeometry(GeometryCombineMode.Exclude, Bubble, Bars);
            dc.DrawGeometry(bubble, null, glyph);
            _ = bars;
            dc.Pop();
        });
    }

    private static RenderTargetBitmap RenderLogo(Brush textBrush, Brush? tint = null)
    {
        const double tile = 360;
        const double margin = 60;
        const double gap = 70;
        const int height = 480;

        // Measured rather than fixed: "TawkType" is wider than the name this canvas was first sized
        // for, and a hard-coded width silently clipped the last letters.
        var text = Wordmark(300, textBrush, tint);
        var width = (int)Math.Ceiling(margin + tile + gap + text.WidthIncludingTrailingWhitespace + margin);

        return Render(width, height, dc =>
        {
            dc.DrawImage(RenderIcon((int)tile), new Rect(margin, (height - tile) / 2, tile, tile));
            dc.DrawText(text, new Point(margin + tile + gap, ((height - text.Height) / 2) - 8));
        });
    }

    private static RenderTargetBitmap RenderSocialCard()
    {
        const int width = 1200;
        const int height = 630;
        return Render(width, height, dc =>
        {
            dc.DrawRectangle(new SolidColorBrush(Ink), null, new Rect(0, 0, width, height));

            // Faint gradient glow in the corner.
            var glow = new RadialGradientBrush(Color.FromArgb(90, 109, 93, 255), Color.FromArgb(0, 14, 15, 22))
            {
                Center = new Point(0.85, 0.2),
                GradientOrigin = new Point(0.85, 0.2),
                RadiusX = 0.6,
                RadiusY = 0.9,
            };
            dc.DrawRectangle(glow, null, new Rect(0, 0, width, height));

            const double tile = 200;
            dc.DrawImage(RenderIcon((int)tile), new Rect(100, 150, tile, tile));

            var name = Wordmark(150, Brushes.White);
            dc.DrawText(name, new Point(340, 165));

            var tagline = new FormattedText(
                "You talk. It types.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                58,
                new SolidColorBrush(Parse("#9CA1B8")),
                1.0);
            dc.DrawText(tagline, new Point(346, 340));

            var sub = new FormattedText(
                "Local voice typing for Windows. Hold a key, speak, release.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                30,
                new SolidColorBrush(Parse("#6F7590")),
                1.0);
            dc.DrawText(sub, new Point(348, 430));
        });
    }

    /// <summary>
    /// "Tawk" in the text colour, "Type" in the accent. The accent differs by background: the pale
    /// tint is for dark grounds and is far too weak on white, where the brand package's light accent
    /// is the one that carries.
    /// </summary>
    private static FormattedText Wordmark(double size, Brush brush, Brush? tint = null)
    {
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText("TawkType", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);

        // "Tawk" stays neutral and "Type" takes the violet tint, as the brand package specifies. A
        // single-colour wordmark is also valid, which is what a caller passing a flat brush gets if
        // this line is removed.
        text.SetForegroundBrush(tint ?? new SolidColorBrush(AccentTint), 4, 4);
        return text;
    }

    // ---- plumbing --------------------------------------------------------------------------

    private static LinearGradientBrush VoiceGradient() => new(Violet, Coral, new Point(0, 0), new Point(1, 1));

    private static RenderTargetBitmap Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Save(string path, BitmapSource source)
    {
        File.WriteAllBytes(path, EncodePng(source));
        Console.WriteLine($"wrote {path}");
    }

    private static void WriteIco(string path, IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, png) in frames)
        {
            var dim = (byte)(size >= 256 ? 0 : size);
            writer.Write(dim);
            writer.Write(dim);
            writer.Write((byte)0); // palette colours
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            writer.Write(png);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TawkType.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("TawkType.sln not found above " + AppContext.BaseDirectory);
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
