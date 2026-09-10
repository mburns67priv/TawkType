using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Talk2Me.Brand;

/// <summary>
/// Source of truth for the rendered brand assets. The geometry and palette here must match
/// src/Talk2Me.App/App.xaml (Brand.* resources) and branding/mark.svg.
///
/// usage: dotnet run --project tools/Talk2Me.Brand [repoRoot]
/// </summary>
internal static class Program
{
    private static readonly Color Violet = Parse("#6D5DFF");
    private static readonly Color Coral = Parse("#FF6A8A");
    private static readonly Color Ink = Parse("#0E0F16");

    // 24-unit design grid. Speech bubble with a tail at the bottom-left, three sound bars inside.
    private static readonly Geometry Bubble = Geometry.Parse(
        "M6,3 H18 A4,4 0 0 1 22,7 V13 A4,4 0 0 1 18,17 H10 L6,21 V17 A4,4 0 0 1 2,13 V7 A4,4 0 0 1 6,3 Z");

    private static readonly Geometry Bars = new GeometryGroup
    {
        Children =
        {
            new RectangleGeometry(new Rect(8, 8, 2, 4), 1, 1),
            new RectangleGeometry(new Rect(11, 6, 2, 8), 1, 1),
            new RectangleGeometry(new Rect(14, 7, 2, 6), 1, 1),
        },
    };

    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
        var exports = Path.Combine(root, "branding", "exports");
        var assets = Path.Combine(root, "src", "Talk2Me.App", "Assets");
        Directory.CreateDirectory(exports);
        Directory.CreateDirectory(assets);

        // App icon: every size the Windows shell asks for, PNG-compressed inside one .ico.
        var frames = IconSizes.Select(size => (Size: size, Png: EncodePng(RenderIcon(size)))).ToList();
        var icoPath = Path.Combine(assets, "talk2me.ico");
        WriteIco(icoPath, frames);
        Console.WriteLine($"wrote {icoPath} ({frames.Count} sizes)");

        foreach (var size in new[] { 256, 512, 1024 })
        {
            Save(Path.Combine(exports, $"icon-{size}.png"), RenderIcon(size));
        }

        Save(Path.Combine(exports, "mark-white-512.png"), RenderMark(512, Brushes.White, Brushes.White));
        Save(Path.Combine(exports, "mark-violet-512.png"), RenderMark(512, new SolidColorBrush(Violet), new SolidColorBrush(Violet)));
        Save(Path.Combine(exports, "logo-on-dark.png"), RenderLogo(Brushes.White));
        Save(Path.Combine(exports, "logo-on-light.png"), RenderLogo(new SolidColorBrush(Ink)));
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
            dc.DrawGeometry(new SolidColorBrush(Violet), null, Bars);
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

    private static RenderTargetBitmap RenderLogo(Brush textBrush)
    {
        const int width = 1600;
        const int height = 480;
        return Render(width, height, dc =>
        {
            const double tile = 360;
            var icon = RenderIcon((int)tile);
            dc.DrawImage(icon, new Rect(60, (height - tile) / 2, tile, tile));

            var text = Wordmark(300, textBrush);
            dc.DrawText(text, new Point(60 + tile + 70, ((height - text.Height) / 2) - 8));
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
                "Hold. Speak. Done.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                58,
                new SolidColorBrush(Parse("#9CA1B8")),
                1.0);
            dc.DrawText(tagline, new Point(346, 340));

            var sub = new FormattedText(
                "Push-to-talk dictation for Windows. Local. Private. Fast.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                30,
                new SolidColorBrush(Parse("#6F7590")),
                1.0);
            dc.DrawText(sub, new Point(348, 430));
        });
    }

    private static FormattedText Wordmark(double size, Brush brush)
    {
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText("Talk2Me", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);
        text.SetForegroundBrush(VoiceGradient(), 4, 1); // the "2"
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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Talk2Me.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Talk2Me.sln not found above " + AppContext.BaseDirectory);
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
