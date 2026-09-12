using System.Runtime.InteropServices;
using TawkType.Core.Text;

namespace TawkType.Windows.Injection;

/// <summary>What becomes of one format when the clipboard is borrowed.</summary>
public enum ClipboardFormatFate
{
    /// <summary>Copied aside, byte for byte, and written back.</summary>
    Copied,

    /// <summary>Not copied, and not needed: Windows regenerates it from one that was.</summary>
    Synthesised,

    /// <summary>Not copied, and nothing will bring it back. A metafile is the realistic case.</summary>
    Lost,
}

/// <summary>What is on the clipboard, in one format.</summary>
public sealed record ClipboardFormatReport(uint Format, string Name, int Bytes, ClipboardFormatFate Fate);

/// <summary>What happened when the clipboard was borrowed and given back.</summary>
public sealed record ClipboardRoundTripReport(
    IReadOnlyList<ClipboardFormatReport> Before,
    bool SnapshotComplete,
    ClipboardWrite Write,
    bool OurTextArrived,
    ClipboardAftermath Decision,
    bool HandedBack,
    IReadOnlyList<string> Differences)
{
    /// <summary>Whether the clipboard came back exactly as it was found.</summary>
    public bool Faithful => SnapshotComplete && Differences.Count == 0;
}

/// <summary>
/// The clipboard round trip, exposed so it can be run against a real clipboard with real content on
/// it. None of this can be unit tested: the formats an application puts on the clipboard, and whether
/// they survive being copied aside and put back, are facts about other people's software.
///
/// <c>tools/TawkType.Clip</c> is the harness. It needs no synthetic keystrokes — the paste itself is
/// the one part it leaves out — so it can be run over whatever the user actually has copied.
/// </summary>
public static class ClipboardDiagnostics
{
    /// <summary>Everything currently on the clipboard, and whether TawkType could copy it aside.</summary>
    public static IReadOnlyList<ClipboardFormatReport> Describe()
    {
        var snapshot = NativeClipboard.Capture();
        var kept = snapshot.Entries.ToDictionary(entry => entry.Format, entry => entry.Bytes.Length);
        var lost = snapshot.Lost.ToHashSet();

        return NativeClipboard.ListFormats()
            .Select(format => new ClipboardFormatReport(
                format,
                NameOf(format),
                kept.TryGetValue(format, out var bytes) ? bytes : 0,
                kept.ContainsKey(format) ? ClipboardFormatFate.Copied
                    : lost.Contains(format) ? ClipboardFormatFate.Lost
                    : ClipboardFormatFate.Synthesised))
            .ToList();
    }

    /// <summary>
    /// Does exactly what a pasted dictation does to the clipboard, minus the Ctrl+V: copies it aside,
    /// writes the text, then hands it back — and reports whether what came back is what was there.
    /// </summary>
    public static ClipboardRoundTripReport RoundTrip(string text)
    {
        var before = Describe();
        var borrowed = NativeClipboard.Capture();

        var write = ClipboardWrite.NotOpened;
        var ourTextArrived = false;
        var decision = ClipboardAftermath.LeaveAlone;
        var handedBack = false;

        try
        {
            write = NativeClipboard.TrySetText(text, allowClipboardHistory: false);
            ourTextArrived = write == ClipboardWrite.Written && NativeClipboard.TryGetText() == text;
        }
        finally
        {
            // changedSinceWrite is false by construction: this harness does the round trip with no
            // paste and nothing else in between, so the only hands on the clipboard are its own.
            decision = ClipboardRestore.Decide(write, borrowed.HasContent, changedSinceWrite: false);

            handedBack = decision switch
            {
                ClipboardAftermath.Restore => NativeClipboard.Restore(borrowed),
                ClipboardAftermath.Clear => NativeClipboard.Clear(),
                _ => true,
            };
        }

        return new ClipboardRoundTripReport(
            before,
            borrowed.IsComplete,
            write,
            ourTextArrived,
            decision,
            handedBack,
            Compare(before, Describe()));
    }

    private static IReadOnlyList<string> Compare(
        IReadOnlyList<ClipboardFormatReport> before,
        IReadOnlyList<ClipboardFormatReport> after)
    {
        var differences = new List<string>();
        var afterByFormat = after.ToDictionary(report => report.Format);

        foreach (var report in before)
        {
            if (!afterByFormat.TryGetValue(report.Format, out var now))
            {
                differences.Add($"{report.Name} is gone");
            }
            else if (now.Bytes != report.Bytes)
            {
                differences.Add($"{report.Name} came back {now.Bytes} bytes, was {report.Bytes}");
            }
        }

        var beforeFormats = before.Select(report => report.Format).ToHashSet();
        foreach (var report in after.Where(report => !beforeFormats.Contains(report.Format)))
        {
            differences.Add($"{report.Name} appeared");
        }

        // Order is not decoration: an application pasting takes the first format it recognises, which
        // is why the most descriptive one is meant to be first. The formats we copy keep their order,
        // but one Windows synthesises is added after them rather than where it used to be — which for
        // an image means CF_DIB is now offered ahead of CF_BITMAP, the order the documentation asks
        // for anyway. Reported rather than fixed, because it cannot be fixed from this side.
        if (differences.Count == 0 && !before.Select(r => r.Format).SequenceEqual(after.Select(r => r.Format)))
        {
            differences.Add(
                "same formats, different order: "
                + string.Join(", ", before.Select(r => r.Name)) + " → " + string.Join(", ", after.Select(r => r.Name)));
        }

        return differences;
    }

    private static string NameOf(uint format)
    {
        if (Standard.TryGetValue(format, out var name))
        {
            return name;
        }

        var buffer = new char[256];
        var length = GetClipboardFormatName(format, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : $"format {format}";
    }

    private static readonly Dictionary<uint, string> Standard = new()
    {
        [1] = "CF_TEXT",
        [2] = "CF_BITMAP",
        [3] = "CF_METAFILEPICT",
        [4] = "CF_SYLK",
        [5] = "CF_DIF",
        [6] = "CF_TIFF",
        [7] = "CF_OEMTEXT",
        [8] = "CF_DIB",
        [9] = "CF_PALETTE",
        [10] = "CF_PENDATA",
        [11] = "CF_RIFF",
        [12] = "CF_WAVE",
        [13] = "CF_UNICODETEXT",
        [14] = "CF_ENHMETAFILE",
        [15] = "CF_HDROP",
        [16] = "CF_LOCALE",
        [17] = "CF_DIBV5",
        [0x80] = "CF_OWNERDISPLAY",
        [0x81] = "CF_DSPTEXT",
        [0x82] = "CF_DSPBITMAP",
        [0x83] = "CF_DSPMETAFILEPICT",
        [0x8E] = "CF_DSPENHMETAFILE",
    };

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClipboardFormatName(uint format, [Out] char[] lpszFormatName, int cchMaxCount);
}
